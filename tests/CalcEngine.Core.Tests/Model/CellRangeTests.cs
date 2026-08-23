using CalcEngine.Core.Model;

namespace CalcEngine.Core.Tests.Model;

/// <summary>
/// Specification tests for the <see cref="CellRange"/> ADT
/// (docs/adt-specifications.md §3).
/// </summary>
public class CellRangeTests
{
    [Fact]
    public void Parse_ReadsBothCorners()
    {
        var range = CellRange.Parse("B2:B45");

        Assert.Equal(CellAddress.Parse("B2"), range.TopLeft);
        Assert.Equal(CellAddress.Parse("B45"), range.BottomRight);
        Assert.Equal(44, range.CellCount);
    }

    [Fact]
    public void Constructor_NormalisesTheCorners()
    {
        // A user may drag a selection upwards; the ADT stores it normalised so
        // that the representation invariant "TopLeft <= BottomRight" holds.
        var dragged = new CellRange(CellAddress.Parse("D9"), CellAddress.Parse("B2"));

        Assert.Equal("B2", dragged.TopLeft.ToA1());
        Assert.Equal("D9", dragged.BottomRight.ToA1());
        Assert.Equal("B2:D9", dragged.ToA1());
    }

    [Fact]
    public void SingleCellRange_HasOneCell()
    {
        var range = CellRange.Single(CellAddress.Parse("C3"));

        Assert.Equal(1, range.CellCount);
        Assert.Equal("C3", range.ToA1());
        Assert.Equal(new[] { "C3" }, range.Cells().Select(c => c.ToA1()));
    }

    [Fact]
    public void Cells_EnumeratesInRowMajorOrder()
    {
        var range = CellRange.Parse("A1:B3");

        Assert.Equal(
            new[] { "A1", "B1", "A2", "B2", "A3", "B3" },
            range.Cells().Select(c => c.ToA1()));
    }

    [Theory]
    [InlineData("B2:D9", "C5", true)]
    [InlineData("B2:D9", "B2", true)]
    [InlineData("B2:D9", "D9", true)]
    [InlineData("B2:D9", "A5", false)]
    [InlineData("B2:D9", "E5", false)]
    [InlineData("B2:D9", "C1", false)]
    [InlineData("B2:D9", "C10", false)]
    public void Contains_TestsTheClosedRectangle(string range, string cell, bool expected)
    {
        Assert.Equal(expected, CellRange.Parse(range).Contains(CellAddress.Parse(cell)));
    }

    [Theory]
    [InlineData("B2:D9", "C5:E12", true)]
    [InlineData("B2:D9", "D9:D9", true)]
    [InlineData("B2:D9", "E2:F9", false)]
    [InlineData("B2:D9", "B10:D12", false)]
    public void Intersects_IsSymmetric(string left, string right, bool expected)
    {
        var a = CellRange.Parse(left);
        var b = CellRange.Parse(right);

        Assert.Equal(expected, a.Intersects(b));
        Assert.Equal(expected, b.Intersects(a));
    }

    [Fact]
    public void Dimensions_AreInclusiveOfBothCorners()
    {
        var range = CellRange.Parse("B2:D9");

        Assert.Equal(3, range.ColumnCount);
        Assert.Equal(8, range.RowCount);
        Assert.Equal(24, range.CellCount);
    }

    [Fact]
    public void CellCount_DoesNotOverflowOnAWholeGrid()
    {
        var whole = new CellRange(CellAddress.Parse("A1"), CellAddress.Parse("XFD1048576"));

        Assert.Equal(16384L * 1048576L, whole.CellCount);
    }

    [Theory]
    [InlineData("B2")]
    [InlineData("B2:")]
    [InlineData(":B2")]
    [InlineData("B2:C3:D4")]
    [InlineData("B2:ZZZZ9")]
    public void TryParse_RejectsMalformedRanges(string text)
    {
        Assert.False(CellRange.TryParse(text, out _));
    }
}
