using System.Globalization;

namespace CalcEngine.Core.Model;

/// <summary>
/// The location of one cell inside one sheet, in A1 notation.
/// </summary>
/// <remarks>
/// <para><b>Abstraction function.</b>
/// AF(c) = the cell in row <c>c.Row</c>, column <c>c.Column</c> of a sheet,
/// where both coordinates are 1-based and the sheet itself is *not* part of the
/// abstract value. A <see cref="CellAddress"/> is a coordinate pair; pairing it
/// with a sheet is the job of <see cref="SheetCellAddress"/>.</para>
///
/// <para><b>Representation invariant.</b>
/// <c>1 &lt;= Column &lt;= MaxColumn &amp;&amp; 1 &lt;= Row &lt;= MaxRow</c>.
/// The invariant is established by the constructor, which is the only way to
/// build a value other than <c>default</c>; <c>default(CellAddress)</c> is
/// deliberately equal to <c>A1</c> (see <see cref="Column"/>) so that the
/// invariant holds for every reachable value, including the zero value that
/// C# hands to an uninitialised array element.</para>
///
/// <para>The type is a <c>readonly struct</c> and therefore immutable and
/// copyable; two addresses that denote the same cell are equal, so an address
/// may be used as a dictionary key.</para>
/// </remarks>
public readonly struct CellAddress : IEquatable<CellAddress>, IComparable<CellAddress>
{
    /// <summary>The last addressable column, <c>XFD</c> (16,384), as in Excel.</summary>
    public const int MaxColumn = 16_384;

    /// <summary>The last addressable row, 1,048,576, as in Excel.</summary>
    public const int MaxRow = 1_048_576;

    private const int MaxColumnLetters = 3;

    // Stored zero-based so that default(CellAddress) is A1 rather than an
    // invariant-violating (0, 0).
    private readonly int _columnIndex;
    private readonly int _rowIndex;

    /// <summary>Creates an address from 1-based coordinates.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The coordinates are off the grid.</exception>
    public CellAddress(int column, int row)
    {
        if (column is < 1 or > MaxColumn)
        {
            throw new ArgumentOutOfRangeException(
                nameof(column), column, $"Column must be between 1 and {MaxColumn} (XFD).");
        }

        if (row is < 1 or > MaxRow)
        {
            throw new ArgumentOutOfRangeException(
                nameof(row), row, $"Row must be between 1 and {MaxRow}.");
        }

        _columnIndex = column - 1;
        _rowIndex = row - 1;
    }

    /// <summary>The 1-based column number; column <c>A</c> is 1.</summary>
    public int Column => _columnIndex + 1;

    /// <summary>The 1-based row number.</summary>
    public int Row => _rowIndex + 1;

    /// <summary>The top-left cell, <c>A1</c>.</summary>
    public static CellAddress A1 => default;

    /// <summary>Parses an A1-style reference such as <c>B45</c> or <c>$B$45</c>.</summary>
    /// <exception cref="FormatException">The text is not a well-formed, in-range address.</exception>
    public static CellAddress Parse(string text)
    {
        if (!TryParse(text, out var address))
        {
            throw new FormatException($"'{text}' is not a valid cell address.");
        }

        return address;
    }

    /// <summary>
    /// Parses an A1-style reference. Dollar signs are accepted and ignored:
    /// absoluteness belongs to a <em>reference</em>, not to a location.
    /// </summary>
    /// <returns><see langword="true"/> if <paramref name="text"/> is well formed and in range.</returns>
    public static bool TryParse(string? text, out CellAddress address)
    {
        address = default;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var i = 0;
        if (text[i] == '$')
        {
            i++;
        }

        var letterStart = i;
        while (i < text.Length && char.IsAsciiLetter(text[i]))
        {
            i++;
        }

        var letterCount = i - letterStart;
        if (letterCount is 0 or > MaxColumnLetters)
        {
            return false;
        }

        if (i < text.Length && text[i] == '$')
        {
            i++;
        }

        var digitStart = i;
        while (i < text.Length && char.IsAsciiDigit(text[i]))
        {
            i++;
        }

        if (i != text.Length || i == digitStart)
        {
            return false;
        }

        if (!TryParseColumnName(text.AsSpan(letterStart, letterCount), out var column))
        {
            return false;
        }

        if (!int.TryParse(
                text.AsSpan(digitStart),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var row)
            || row is < 1 or > MaxRow)
        {
            return false;
        }

        address = new CellAddress(column, row);
        return true;
    }

    /// <summary>Renders the address in A1 notation, for example <c>B45</c>.</summary>
    public string ToA1() => string.Concat(ColumnName(Column), Row.ToString(CultureInfo.InvariantCulture));

    /// <summary>Renders the column number as bijective base-26 letters: 1 → <c>A</c>, 27 → <c>AA</c>.</summary>
    public static string ColumnName(int column)
    {
        if (column is < 1 or > MaxColumn)
        {
            throw new ArgumentOutOfRangeException(nameof(column), column, "Column is off the grid.");
        }

        Span<char> buffer = stackalloc char[MaxColumnLetters];
        var next = MaxColumnLetters;
        var remaining = column;
        while (remaining > 0)
        {
            remaining--;
            buffer[--next] = (char)('A' + (remaining % 26));
            remaining /= 26;
        }

        return new string(buffer[next..]);
    }

    /// <summary>Reads bijective base-26 column letters; the inverse of <see cref="ColumnName"/>.</summary>
    public static bool TryParseColumnName(ReadOnlySpan<char> letters, out int column)
    {
        column = 0;
        if (letters.IsEmpty || letters.Length > MaxColumnLetters)
        {
            return false;
        }

        foreach (var c in letters)
        {
            if (!char.IsAsciiLetter(c))
            {
                column = 0;
                return false;
            }

            column = (column * 26) + (char.ToUpperInvariant(c) - 'A' + 1);
        }

        if (column > MaxColumn)
        {
            column = 0;
            return false;
        }

        return true;
    }

    /// <inheritdoc cref="TryParseColumnName(ReadOnlySpan{char}, out int)"/>
    public static bool TryParseColumnName(string? letters, out int column)
    {
        if (letters is null)
        {
            column = 0;
            return false;
        }

        return TryParseColumnName(letters.AsSpan(), out column);
    }

    /// <summary>Translates the address by a column and row delta.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The result would be off the grid.</exception>
    public CellAddress Offset(int columnDelta, int rowDelta) =>
        new(checked(Column + columnDelta), checked(Row + rowDelta));

    /// <summary>
    /// Translates the address by a delta, reporting rather than throwing when the
    /// result leaves the grid. Callers that want a <c>#REF!</c> error use this.
    /// </summary>
    public bool TryOffset(int columnDelta, int rowDelta, out CellAddress result)
    {
        var column = (long)Column + columnDelta;
        var row = (long)Row + rowDelta;
        if (column is < 1 or > MaxColumn || row is < 1 or > MaxRow)
        {
            result = default;
            return false;
        }

        result = new CellAddress((int)column, (int)row);
        return true;
    }

    /// <inheritdoc />
    public bool Equals(CellAddress other) => _columnIndex == other._columnIndex && _rowIndex == other._rowIndex;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CellAddress other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_columnIndex, _rowIndex);

    /// <summary>Orders addresses in reading order: row-major, then by column.</summary>
    public int CompareTo(CellAddress other)
    {
        var byRow = _rowIndex.CompareTo(other._rowIndex);
        return byRow != 0 ? byRow : _columnIndex.CompareTo(other._columnIndex);
    }

    /// <inheritdoc />
    public override string ToString() => ToA1();

    public static bool operator ==(CellAddress left, CellAddress right) => left.Equals(right);

    public static bool operator !=(CellAddress left, CellAddress right) => !left.Equals(right);

    public static bool operator <(CellAddress left, CellAddress right) => left.CompareTo(right) < 0;

    public static bool operator >(CellAddress left, CellAddress right) => left.CompareTo(right) > 0;

    public static bool operator <=(CellAddress left, CellAddress right) => left.CompareTo(right) <= 0;

    public static bool operator >=(CellAddress left, CellAddress right) => left.CompareTo(right) >= 0;
}
