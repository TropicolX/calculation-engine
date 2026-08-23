namespace CalcEngine.Core.Model;

/// <summary>
/// A rectangular block of cells within one sheet, closed on both corners.
/// </summary>
/// <remarks>
/// <para><b>Abstraction function.</b>
/// AF(r) = { (col, row) | r.TopLeft.Column &lt;= col &lt;= r.BottomRight.Column
/// and r.TopLeft.Row &lt;= row &lt;= r.BottomRight.Row } — the set of cells in
/// the closed rectangle. A single cell is the range whose corners coincide.</para>
///
/// <para><b>Representation invariant.</b>
/// <c>TopLeft.Column &lt;= BottomRight.Column &amp;&amp; TopLeft.Row &lt;= BottomRight.Row</c>.
/// The constructor restores the invariant by normalising the two corners it is
/// given, so a selection dragged upwards or leftwards yields the same range as
/// the same selection dragged the other way.</para>
///
/// <para>The type never materialises its cells: <see cref="Cells"/> is lazy and
/// <see cref="CellCount"/> is arithmetic, so a range covering the whole grid
/// (17 billion cells) is a cheap value.</para>
/// </remarks>
public readonly struct CellRange : IEquatable<CellRange>
{
    /// <summary>Creates the smallest range containing both corners.</summary>
    public CellRange(CellAddress first, CellAddress second)
    {
        var left = Math.Min(first.Column, second.Column);
        var right = Math.Max(first.Column, second.Column);
        var top = Math.Min(first.Row, second.Row);
        var bottom = Math.Max(first.Row, second.Row);

        TopLeft = new CellAddress(left, top);
        BottomRight = new CellAddress(right, bottom);
    }

    /// <summary>The top-left corner, inclusive.</summary>
    public CellAddress TopLeft { get; }

    /// <summary>The bottom-right corner, inclusive.</summary>
    public CellAddress BottomRight { get; }

    /// <summary>Number of columns spanned, at least 1.</summary>
    public int ColumnCount => BottomRight.Column - TopLeft.Column + 1;

    /// <summary>Number of rows spanned, at least 1.</summary>
    public int RowCount => BottomRight.Row - TopLeft.Row + 1;

    /// <summary>Number of cells covered. <see cref="long"/> because a whole grid overflows <see cref="int"/>.</summary>
    public long CellCount => (long)ColumnCount * RowCount;

    /// <summary>True when the range covers exactly one cell.</summary>
    public bool IsSingleCell => TopLeft == BottomRight;

    /// <summary>The one-cell range at <paramref name="address"/>.</summary>
    public static CellRange Single(CellAddress address) => new(address, address);

    /// <summary>Parses <c>B2:B45</c>, or the degenerate one-cell form is rejected — use <see cref="Single"/>.</summary>
    /// <exception cref="FormatException">The text is not a well-formed range.</exception>
    public static CellRange Parse(string text)
    {
        if (!TryParse(text, out var range))
        {
            throw new FormatException($"'{text}' is not a valid cell range.");
        }

        return range;
    }

    /// <summary>Parses <c>B2:B45</c>. A bare address is <em>not</em> a range; use <see cref="Single"/>.</summary>
    public static bool TryParse(string? text, out CellRange range)
    {
        range = default;
        if (text is null)
        {
            return false;
        }

        var colon = text.IndexOf(':');
        if (colon <= 0 || colon == text.Length - 1)
        {
            return false;
        }

        if (text.IndexOf(':', colon + 1) >= 0)
        {
            return false;
        }

        if (!CellAddress.TryParse(text[..colon], out var first)
            || !CellAddress.TryParse(text[(colon + 1)..], out var second))
        {
            return false;
        }

        range = new CellRange(first, second);
        return true;
    }

    /// <summary>True when <paramref name="address"/> lies inside the closed rectangle.</summary>
    public bool Contains(CellAddress address) =>
        address.Column >= TopLeft.Column && address.Column <= BottomRight.Column &&
        address.Row >= TopLeft.Row && address.Row <= BottomRight.Row;

    /// <summary>True when this range wholly contains <paramref name="other"/>.</summary>
    public bool Contains(CellRange other) =>
        Contains(other.TopLeft) && Contains(other.BottomRight);

    /// <summary>True when the two rectangles share at least one cell.</summary>
    public bool Intersects(CellRange other) =>
        TopLeft.Column <= other.BottomRight.Column &&
        other.TopLeft.Column <= BottomRight.Column &&
        TopLeft.Row <= other.BottomRight.Row &&
        other.TopLeft.Row <= BottomRight.Row;

    /// <summary>Lazily enumerates the covered cells in row-major (reading) order.</summary>
    public IEnumerable<CellAddress> Cells()
    {
        var topLeft = TopLeft;
        var bottomRight = BottomRight;
        for (var row = topLeft.Row; row <= bottomRight.Row; row++)
        {
            for (var column = topLeft.Column; column <= bottomRight.Column; column++)
            {
                yield return new CellAddress(column, row);
            }
        }
    }

    /// <summary>Renders the range as <c>B2:B45</c>, or as a bare address when it is a single cell.</summary>
    public string ToA1() => IsSingleCell ? TopLeft.ToA1() : $"{TopLeft.ToA1()}:{BottomRight.ToA1()}";

    /// <inheritdoc />
    public bool Equals(CellRange other) => TopLeft == other.TopLeft && BottomRight == other.BottomRight;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CellRange other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(TopLeft, BottomRight);

    /// <inheritdoc />
    public override string ToString() => ToA1();

    public static bool operator ==(CellRange left, CellRange right) => left.Equals(right);

    public static bool operator !=(CellRange left, CellRange right) => !left.Equals(right);
}
