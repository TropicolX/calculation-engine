using CalcEngine.Core.Model;

namespace CalcEngine.Core.Expressions;

/// <summary>
/// An A1-style reference as written: a location plus its <c>$</c> markers.
/// </summary>
/// <remarks>
/// <para><b>Abstraction function.</b> AF(r) = the cell AF(r.Address), reached by
/// a reference whose column is absolute iff <c>r.ColumnAbsolute</c> and whose
/// row is absolute iff <c>r.RowAbsolute</c>.</para>
/// <para><b>Representation invariant.</b> Inherited from
/// <see cref="CellAddress"/>; the two flags are independent.</para>
/// <para>Absoluteness does not change which cell is read, so it is not part of
/// the dependency graph; it matters only when a formula is copied or printed.</para>
/// </remarks>
public readonly record struct CellReference(CellAddress Address, bool ColumnAbsolute, bool RowAbsolute)
{
    /// <summary>A relative reference to <paramref name="address"/>.</summary>
    public static CellReference Relative(CellAddress address) => new(address, false, false);

    /// <summary>Renders the reference, including its <c>$</c> markers: <c>$B$2</c>.</summary>
    public string ToA1() =>
        (ColumnAbsolute ? "$" : string.Empty) + CellAddress.ColumnName(Address.Column) +
        (RowAbsolute ? "$" : string.Empty) + Address.Row.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override string ToString() => ToA1();
}

/// <summary>A reference to one cell, optionally on another sheet.</summary>
public sealed class CellReferenceExpression : Expression
{
    /// <summary>Creates a cell reference.</summary>
    /// <param name="sheetName">The sheet named by a qualifier, or null for the formula's own sheet.</param>
    /// <param name="reference">The location and its absoluteness markers.</param>
    public CellReferenceExpression(string? sheetName, CellReference reference)
    {
        SheetName = sheetName;
        Reference = reference;
    }

    /// <summary>The qualified sheet name, or null when the reference is local.</summary>
    public string? SheetName { get; }

    /// <summary>The referenced location.</summary>
    public CellReference Reference { get; }

    /// <inheritdoc />
    public override TResult Accept<TResult>(IExpressionVisitor<TResult> visitor) => visitor.VisitCellReference(this);
}

/// <summary>A reference to a rectangular range, optionally on another sheet.</summary>
public sealed class RangeReferenceExpression : Expression
{
    /// <summary>Creates a range reference from the two corners <em>as written</em>.</summary>
    public RangeReferenceExpression(string? sheetName, CellReference from, CellReference to)
    {
        SheetName = sheetName;
        From = from;
        To = to;
        Range = new CellRange(from.Address, to.Address);
    }

    /// <summary>The qualified sheet name, or null when the reference is local.</summary>
    public string? SheetName { get; }

    /// <summary>The first corner, exactly as the user wrote it.</summary>
    public CellReference From { get; }

    /// <summary>The second corner, exactly as the user wrote it.</summary>
    public CellReference To { get; }

    /// <summary>The normalised rectangle the two corners denote.</summary>
    public CellRange Range { get; }

    /// <inheritdoc />
    public override TResult Accept<TResult>(IExpressionVisitor<TResult> visitor) => visitor.VisitRangeReference(this);
}
