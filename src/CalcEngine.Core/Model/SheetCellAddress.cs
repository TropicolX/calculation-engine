namespace CalcEngine.Core.Model;

/// <summary>
/// A cell identified across a whole workbook: a sheet name plus an address.
/// </summary>
/// <remarks>
/// <para><b>Abstraction function.</b> AF(s) = the cell AF(s.Address) of the
/// sheet named s.Sheet.</para>
///
/// <para><b>Representation invariant.</b> <c>Sheet</c> is non-null and
/// non-empty. Sheet names are compared case-insensitively, as in Excel, so
/// <c>Marks!B2</c> and <c>marks!B2</c> denote the same cell.</para>
/// </remarks>
public readonly struct SheetCellAddress : IEquatable<SheetCellAddress>
{
    /// <summary>Creates a workbook-wide cell identity.</summary>
    /// <exception cref="ArgumentException"><paramref name="sheet"/> is null or blank.</exception>
    public SheetCellAddress(string sheet, CellAddress address)
    {
        if (string.IsNullOrWhiteSpace(sheet))
        {
            throw new ArgumentException("A sheet name is required.", nameof(sheet));
        }

        Sheet = sheet;
        Address = address;
    }

    /// <summary>The name of the sheet the cell lives on.</summary>
    public string Sheet { get; }

    /// <summary>The cell's location within that sheet.</summary>
    public CellAddress Address { get; }

    /// <summary>Renders the reference as <c>Marks!B2</c>, quoting the sheet name if it needs it.</summary>
    public string ToA1() => $"{QuoteSheetName(Sheet)}!{Address.ToA1()}";

    /// <summary>
    /// Quotes a sheet name for use in a formula, doubling embedded apostrophes.
    /// A name that is a valid unquoted identifier is returned unchanged.
    /// </summary>
    public static string QuoteSheetName(string sheet)
    {
        if (NeedsQuoting(sheet))
        {
            return $"'{sheet.Replace("'", "''", StringComparison.Ordinal)}'";
        }

        return sheet;
    }

    private static bool NeedsQuoting(string sheet)
    {
        if (sheet.Length == 0 || !(char.IsAsciiLetter(sheet[0]) || sheet[0] == '_'))
        {
            return true;
        }

        foreach (var c in sheet)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_' && c != '.')
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool Equals(SheetCellAddress other) =>
        Address == other.Address &&
        string.Equals(Sheet, other.Sheet, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SheetCellAddress other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Sheet ?? string.Empty), Address);

    /// <inheritdoc />
    public override string ToString() => ToA1();

    public static bool operator ==(SheetCellAddress left, SheetCellAddress right) => left.Equals(right);

    public static bool operator !=(SheetCellAddress left, SheetCellAddress right) => !left.Equals(right);
}
