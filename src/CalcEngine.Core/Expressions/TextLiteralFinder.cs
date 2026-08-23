namespace CalcEngine.Core.Expressions;

/// <summary>
/// Collects the text literals of a formula, in source order.
/// </summary>
/// <remarks>
/// This is the visitor that makes "replace inside formulas, but only inside
/// their text" possible. Together with <see cref="SourceSpan"/> it lets Find
/// &amp; Replace change <c>=IF(A1&gt;50,"PASSED","FAILED")</c> into
/// <c>=IF(A1&gt;50,"PASS","FAIL")</c> without any risk of mangling the
/// references or the function name — the failure mode a naive string replace
/// over formula text has, and the reason this class exists.
/// </remarks>
public sealed class TextLiteralFinder : IExpressionVisitor<bool>
{
    private readonly List<TextLiteralExpression> _literals = [];

    private TextLiteralFinder()
    {
    }

    /// <summary>Returns every text literal in <paramref name="expression"/>, in source order.</summary>
    public static IReadOnlyList<TextLiteralExpression> FindAll(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var finder = new TextLiteralFinder();
        expression.Accept(finder);
        finder._literals.Sort(static (a, b) => a.Span.Start.CompareTo(b.Span.Start));
        return finder._literals;
    }

    /// <inheritdoc />
    public bool VisitNumber(NumberLiteralExpression node) => true;

    /// <inheritdoc />
    public bool VisitText(TextLiteralExpression node)
    {
        _literals.Add(node);
        return true;
    }

    /// <inheritdoc />
    public bool VisitBoolean(BooleanLiteralExpression node) => true;

    /// <inheritdoc />
    public bool VisitError(ErrorLiteralExpression node) => true;

    /// <inheritdoc />
    public bool VisitCellReference(CellReferenceExpression node) => true;

    /// <inheritdoc />
    public bool VisitRangeReference(RangeReferenceExpression node) => true;

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
}
