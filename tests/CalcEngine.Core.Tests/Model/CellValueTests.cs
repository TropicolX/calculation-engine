using CalcEngine.Core.Model;

namespace CalcEngine.Core.Tests.Model;

/// <summary>
/// Specification tests for the <see cref="CellValue"/> tagged-union ADT
/// (docs/adt-specifications.md §4).
/// </summary>
public class CellValueTests
{
    [Fact]
    public void Blank_IsTheDefaultValue()
    {
        Assert.Equal(CellValueKind.Blank, default(CellValue).Kind);
        Assert.Equal(CellValueKind.Blank, CellValue.Blank.Kind);
        Assert.True(CellValue.Blank.IsBlank);
    }

    [Fact]
    public void EachFactoryProducesItsOwnKind()
    {
        Assert.Equal(CellValueKind.Number, CellValue.Number(1).Kind);
        Assert.Equal(CellValueKind.Text, CellValue.Text("x").Kind);
        Assert.Equal(CellValueKind.Boolean, CellValue.Boolean(true).Kind);
        Assert.Equal(CellValueKind.Error, CellValue.FromError(ErrorValue.DivideByZero).Kind);
    }

    [Fact]
    public void AccessingTheWrongVariantThrows()
    {
        // The union is *tagged*: reading a payload that is not there is a
        // programming error in the caller, not a spreadsheet error value.
        Assert.Throws<InvalidOperationException>(() => CellValue.Text("x").AsNumber);
        Assert.Throws<InvalidOperationException>(() => CellValue.Number(1).AsText);
        Assert.Throws<InvalidOperationException>(() => CellValue.Number(1).AsError);
    }

    [Fact]
    public void Text_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => CellValue.Text(null!));
    }

    [Fact]
    public void Equality_IsStructuralWithinAKind()
    {
        Assert.Equal(CellValue.Number(2.5), CellValue.Number(2.5));
        Assert.Equal(CellValue.Text("Ngozi"), CellValue.Text("Ngozi"));
        Assert.NotEqual(CellValue.Text("Ngozi"), CellValue.Text("ngozi"));
        Assert.NotEqual(CellValue.Number(1), CellValue.Boolean(true));
        Assert.Equal(
            CellValue.FromError(ErrorValue.DivideByZero),
            CellValue.FromError(ErrorValue.DivideByZero));
    }

    [Fact]
    public void ErrorDetailDoesNotAffectEquality()
    {
        // Two #REF! cells are equal as *values*; the detail is diagnostic text
        // for the client, not part of the abstract value.
        var bare = CellValue.FromError(ErrorValue.Reference);
        var detailed = CellValue.FromError(ErrorValue.Reference.WithDetail("sheet 'Marks' was deleted"));

        Assert.Equal(bare, detailed);
        Assert.Equal("sheet 'Marks' was deleted", detailed.AsError.Detail);
    }

    [Theory]
    [InlineData(1.0, "1")]
    [InlineData(2.5, "2.5")]
    [InlineData(-0.75, "-0.75")]
    [InlineData(1e21, "1E+21")]
    public void ToDisplayString_FormatsNumbersInvariantly(double number, string expected)
    {
        Assert.Equal(expected, CellValue.Number(number).ToDisplayString());
    }

    [Fact]
    public void ToDisplayString_UsesSpreadsheetSpellings()
    {
        Assert.Equal(string.Empty, CellValue.Blank.ToDisplayString());
        Assert.Equal("TRUE", CellValue.Boolean(true).ToDisplayString());
        Assert.Equal("FALSE", CellValue.Boolean(false).ToDisplayString());
        Assert.Equal("Ngozi", CellValue.Text("Ngozi").ToDisplayString());
        Assert.Equal("#DIV/0!", CellValue.FromError(ErrorValue.DivideByZero).ToDisplayString());
    }

    [Fact]
    public void EveryErrorCodeIsRecognisedByItsSpelling()
    {
        foreach (var error in ErrorValue.All)
        {
            Assert.True(ErrorValue.TryParse(error.Code, out var parsed));
            Assert.Equal(error.Kind, parsed.Kind);
        }

        Assert.False(ErrorValue.TryParse("#NOPE!", out _));
    }

    [Fact]
    public void IsError_IsTrueOnlyForTheErrorKind()
    {
        Assert.True(CellValue.FromError(ErrorValue.Value).IsError);
        Assert.False(CellValue.Number(0).IsError);
        Assert.False(CellValue.Blank.IsError);
    }
}
