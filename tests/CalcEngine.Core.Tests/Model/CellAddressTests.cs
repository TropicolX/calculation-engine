using CalcEngine.Core.Model;

namespace CalcEngine.Core.Tests.Model;

/// <summary>
/// Specification tests for the <see cref="CellAddress"/> ADT.
/// Each region corresponds to a clause of the specification in
/// docs/adt-specifications.md §2.
/// </summary>
public class CellAddressTests
{
    [Theory]
    [InlineData("A1", 1, 1)]
    [InlineData("B2", 2, 2)]
    [InlineData("B45", 2, 45)]
    [InlineData("Z1", 26, 1)]
    [InlineData("AA1", 27, 1)]
    [InlineData("AB10", 28, 10)]
    [InlineData("XFD1048576", 16384, 1048576)]
    public void Parse_ReadsColumnLettersAndRowDigits(string a1, int column, int row)
    {
        var address = CellAddress.Parse(a1);

        Assert.Equal(column, address.Column);
        Assert.Equal(row, address.Row);
    }

    [Theory]
    [InlineData("b2")]
    [InlineData("B2")]
    [InlineData("$B$2")]
    [InlineData("$B2")]
    [InlineData("B$2")]
    public void Parse_IgnoresCaseAndDollarSigns(string a1)
    {
        // The '$' markers are part of the *reference*, not part of the address:
        // the address ADT denotes a location, nothing else.
        Assert.Equal(new CellAddress(2, 2), CellAddress.Parse(a1));
    }

    [Theory]
    [InlineData(1, 1, "A1")]
    [InlineData(2, 45, "B45")]
    [InlineData(26, 1, "Z1")]
    [InlineData(27, 1, "AA1")]
    [InlineData(702, 3, "ZZ3")]
    [InlineData(703, 3, "AAA3")]
    [InlineData(16384, 1048576, "XFD1048576")]
    public void ToA1_IsTheInverseOfParse(int column, int row, string expected)
    {
        var address = new CellAddress(column, row);

        Assert.Equal(expected, address.ToA1());
        Assert.Equal(address, CellAddress.Parse(expected));
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("1")]
    [InlineData("A0")]          // rows are 1-based
    [InlineData("1A")]
    [InlineData("A1B")]
    [InlineData("ZZZZ1")]       // beyond XFD
    [InlineData("A1048577")]    // beyond the last row
    [InlineData("A 1")]
    [InlineData(null)]
    public void TryParse_RejectsMalformedOrOutOfRangeInput(string? a1)
    {
        Assert.False(CellAddress.TryParse(a1, out _));
    }

    [Fact]
    public void Constructor_RejectsOutOfRangeCoordinates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CellAddress(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CellAddress(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CellAddress(16385, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CellAddress(1, 1048577));
    }

    [Fact]
    public void Equality_IsStructural()
    {
        Assert.Equal(new CellAddress(3, 7), new CellAddress(3, 7));
        Assert.NotEqual(new CellAddress(3, 7), new CellAddress(7, 3));
        Assert.Equal(new CellAddress(3, 7).GetHashCode(), new CellAddress(3, 7).GetHashCode());
    }

    [Fact]
    public void CompareTo_OrdersInReadingOrder()
    {
        // Row-major: A1 < B1 < A2, which is the order a user scans a grid.
        var sorted = new[]
        {
            CellAddress.Parse("B2"), CellAddress.Parse("A2"),
            CellAddress.Parse("B1"), CellAddress.Parse("A1"),
        };
        Array.Sort(sorted);

        Assert.Equal(new[] { "A1", "B1", "A2", "B2" }, sorted.Select(a => a.ToA1()));
    }

    [Theory]
    [InlineData("B2", 1, 0, "C2")]
    [InlineData("B2", 0, 3, "B5")]
    [InlineData("B2", -1, -1, "A1")]
    public void Offset_TranslatesTheAddress(string from, int dColumn, int dRow, string expected)
    {
        Assert.Equal(expected, CellAddress.Parse(from).Offset(dColumn, dRow).ToA1());
    }

    [Fact]
    public void Offset_OffTheGridIsRejected()
    {
        // Callers that want a #REF! must ask for one; the ADT will not invent
        // an address outside its own representation invariant.
        Assert.Throws<ArgumentOutOfRangeException>(() => CellAddress.Parse("A1").Offset(-1, 0));
        Assert.False(CellAddress.Parse("A1").TryOffset(0, -1, out _));
    }

    [Theory]
    [InlineData(1, "A")]
    [InlineData(26, "Z")]
    [InlineData(27, "AA")]
    [InlineData(52, "AZ")]
    [InlineData(53, "BA")]
    [InlineData(16384, "XFD")]
    public void ColumnName_UsesBijectiveBase26(int column, string expected)
    {
        Assert.Equal(expected, CellAddress.ColumnName(column));
        Assert.True(CellAddress.TryParseColumnName(expected, out var roundTripped));
        Assert.Equal(column, roundTripped);
    }
}
