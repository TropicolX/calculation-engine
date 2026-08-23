namespace CalcEngine.Core.Expressions;

/// <summary>The distinct cells and ranges a formula reads.</summary>
public sealed class FormulaReferences
{
    internal FormulaReferences(
        IReadOnlyList<CellReferenceExpression> cells,
        IReadOnlyList<RangeReferenceExpression> ranges)
    {
        Cells = cells;
        Ranges = ranges;
    }

    /// <summary>An empty reference set, shared by every literal-only formula.</summary>
    public static FormulaReferences Empty { get; } = new([], []);

    /// <summary>Distinct single-cell references, in first-appearance order.</summary>
    public IReadOnlyList<CellReferenceExpression> Cells { get; }

    /// <summary>Distinct range references, in first-appearance order.</summary>
    public IReadOnlyList<RangeReferenceExpression> Ranges { get; }

    /// <summary>True when the formula reads nothing and can never go stale.</summary>
    public bool IsEmpty => Cells.Count == 0 && Ranges.Count == 0;
}

/// <summary>
/// Extracts the dependency set of a formula: the visitor that feeds the
/// dependency graph.
/// </summary>
/// <remarks>
/// Ranges are kept whole rather than expanded into their cells. Expanding
/// <c>SUM(B2:B45)</c> into 44 edges is affordable; expanding
/// <c>SUM(A1:A100000)</c> in a hundred formulas is not. The dependency graph
/// stores ranges in a spatial index instead — see
/// docs/adt-specifications.md §7.
/// </remarks>
public sealed class ReferenceCollector : IExpressionVisitor<bool>
{
    private readonly List<CellReferenceExpression> _cells = [];
    private readonly List<RangeReferenceExpression> _ranges = [];
    private readonly HashSet<(string?, Model.CellAddress)> _seenCells = new(CellKeyComparer.Instance);
    private readonly HashSet<(string?, Model.CellRange)> _seenRanges = new(RangeKeyComparer.Instance);

    private ReferenceCollector()
    {
    }

    /// <summary>Collects the distinct references of <paramref name="expression"/>.</summary>
    public static FormulaReferences Collect(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var collector = new ReferenceCollector();
        expression.Accept(collector);
        return collector._cells.Count == 0 && collector._ranges.Count == 0
            ? FormulaReferences.Empty
            : new FormulaReferences(collector._cells, collector._ranges);
    }

    /// <inheritdoc />
    public bool VisitNumber(NumberLiteralExpression node) => true;

    /// <inheritdoc />
    public bool VisitText(TextLiteralExpression node) => true;

    /// <inheritdoc />
    public bool VisitBoolean(BooleanLiteralExpression node) => true;

    /// <inheritdoc />
    public bool VisitError(ErrorLiteralExpression node) => true;

    /// <inheritdoc />
    public bool VisitCellReference(CellReferenceExpression node)
    {
        if (_seenCells.Add((node.SheetName, node.Reference.Address)))
        {
            _cells.Add(node);
        }

        return true;
    }

    /// <inheritdoc />
    public bool VisitRangeReference(RangeReferenceExpression node)
    {
        if (_seenRanges.Add((node.SheetName, node.Range)))
        {
            _ranges.Add(node);
        }

        return true;
    }

    /// <inheritdoc />
    public bool VisitUnary(UnaryOperatorExpression node) => node.Operand.Accept(this);

    /// <inheritdoc />
    public bool VisitBinary(BinaryOperatorExpression node)
    {
        node.Left.Accept(this);
        node.Right.Accept(this);
        return true;
    }

    /// <inheritdoc />
    public bool VisitFunctionCall(FunctionCallExpression node)
    {
        foreach (var argument in node.Arguments)
        {
            argument.Accept(this);
        }

        return true;
    }

    private sealed class CellKeyComparer : IEqualityComparer<(string?, Model.CellAddress)>
    {
        public static CellKeyComparer Instance { get; } = new();

        public bool Equals((string?, Model.CellAddress) x, (string?, Model.CellAddress) y) =>
            x.Item2 == y.Item2 && string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string?, Model.CellAddress) obj) =>
            HashCode.Combine(
                obj.Item1 is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1),
                obj.Item2);
    }

    private sealed class RangeKeyComparer : IEqualityComparer<(string?, Model.CellRange)>
    {
        public static RangeKeyComparer Instance { get; } = new();

        public bool Equals((string?, Model.CellRange) x, (string?, Model.CellRange) y) =>
            x.Item2 == y.Item2 && string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string?, Model.CellRange) obj) =>
            HashCode.Combine(
                obj.Item1 is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1),
                obj.Item2);
    }
}
