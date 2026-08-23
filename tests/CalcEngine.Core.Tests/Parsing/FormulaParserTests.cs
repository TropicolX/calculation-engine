using CalcEngine.Core.Expressions;
using CalcEngine.Core.Model;
using CalcEngine.Core.Parsing;

namespace CalcEngine.Core.Tests.Parsing;

/// <summary>
/// Tests that the parser builds the expression tree the grammar promises
/// (docs/grammar.md §2 and §7).
/// </summary>
public class FormulaParserTests
{
    private static Expression Parse(string formula)
    {
        var result = FormulaParser.Parse(formula);
        Assert.True(result.Success, $"expected '{formula}' to parse, but: {string.Join("; ", result.Errors)}");
        return result.Expression!;
    }

    /// <summary>Fully parenthesised rendering makes tree shape visible in one string.</summary>
    private static string Shape(string formula) => FormulaPrinter.PrintFullyParenthesised(Parse(formula));

    [Theory]
    [InlineData("=1", 1)]
    [InlineData("=42", 42)]
    [InlineData("=2.5", 2.5)]
    [InlineData("=.5", 0.5)]
    [InlineData("=1e3", 1000)]
    [InlineData("=1.5E-2", 0.015)]
    public void NumberLiterals(string formula, double expected)
    {
        var literal = Assert.IsType<NumberLiteralExpression>(Parse(formula));
        Assert.Equal(expected, literal.Value);
    }

    [Theory]
    [InlineData("=\"Ngozi\"", "Ngozi")]
    [InlineData("=\"\"", "")]
    [InlineData("=\"she said \"\"yes\"\"\"", "she said \"yes\"")]
    public void TextLiterals_UnescapeDoubledQuotes(string formula, string expected)
    {
        var literal = Assert.IsType<TextLiteralExpression>(Parse(formula));
        Assert.Equal(expected, literal.Value);
    }

    [Theory]
    [InlineData("=TRUE", true)]
    [InlineData("=false", false)]
    [InlineData("=True", true)]
    public void BooleanLiterals_AreCaseInsensitive(string formula, bool expected)
    {
        Assert.Equal(expected, Assert.IsType<BooleanLiteralExpression>(Parse(formula)).Value);
    }

    [Fact]
    public void ErrorLiterals_ReproduceTheError()
    {
        Assert.Equal(ErrorValue.NotAvailable, Assert.IsType<ErrorLiteralExpression>(Parse("=#N/A")).Value);
        Assert.Equal(ErrorValue.DivideByZero, Assert.IsType<ErrorLiteralExpression>(Parse("=#DIV/0!")).Value);
    }

    [Fact]
    public void CellReference_KeepsAbsolutenessAndCase()
    {
        var reference = Assert.IsType<CellReferenceExpression>(Parse("=$b$2"));

        Assert.Equal(CellAddress.Parse("B2"), reference.Reference.Address);
        Assert.True(reference.Reference.ColumnAbsolute);
        Assert.True(reference.Reference.RowAbsolute);
        Assert.Null(reference.SheetName);
    }

    [Fact]
    public void CellReference_MixedAbsoluteness()
    {
        var reference = Assert.IsType<CellReferenceExpression>(Parse("=B$2")).Reference;

        Assert.False(reference.ColumnAbsolute);
        Assert.True(reference.RowAbsolute);
    }

    [Fact]
    public void RangeReference_NormalisesCornersButKeepsWhatWasWritten()
    {
        var range = Assert.IsType<RangeReferenceExpression>(Parse("=D9:B2"));

        Assert.Equal(CellRange.Parse("B2:D9"), range.Range);
        Assert.Equal("D9", range.From.Address.ToA1());
        Assert.Equal("B2", range.To.Address.ToA1());
    }

    [Theory]
    [InlineData("=Marks!B2", "Marks")]
    [InlineData("='CSC 322'!B2", "CSC 322")]
    [InlineData("='Bola''s sheet'!B2", "Bola's sheet")]
    public void SheetQualifiedReference_UnquotesTheSheetName(string formula, string expected)
    {
        Assert.Equal(expected, Assert.IsType<CellReferenceExpression>(Parse(formula)).SheetName);
    }

    [Fact]
    public void SheetQualifiedRange()
    {
        var range = Assert.IsType<RangeReferenceExpression>(Parse("=Marks!B2:B45"));

        Assert.Equal("Marks", range.SheetName);
        Assert.Equal(CellRange.Parse("B2:B45"), range.Range);
    }

    [Fact]
    public void FunctionCall_WithArguments()
    {
        var call = Assert.IsType<FunctionCallExpression>(Parse("=IF(A1>50,\"PASS\",\"FAIL\")"));

        Assert.Equal("IF", call.Name);
        Assert.Equal(3, call.Arguments.Count);
    }

    [Fact]
    public void FunctionCall_WithNoArguments()
    {
        Assert.Empty(Assert.IsType<FunctionCallExpression>(Parse("=RAND()")).Arguments);
    }

    [Fact]
    public void FunctionCall_NameIsUpperCasedForLookup()
    {
        Assert.Equal("SUM", Assert.IsType<FunctionCallExpression>(Parse("=sum(A1)")).Name);
    }

    [Fact]
    public void FunctionCall_NameThatLooksLikeACellReference()
    {
        // LOG10 lexes as CELL_REF; the '(' is what makes it a call. grammar.md §5.
        Assert.Equal("LOG10", Assert.IsType<FunctionCallExpression>(Parse("=LOG10(100)")).Name);
    }

    [Fact]
    public void FunctionCall_Nested()
    {
        Assert.Equal(
            "=ROUND((SUM(B2:B45) / COUNT(B2:B45)), 2)",
            Shape("=ROUND(SUM(B2:B45)/COUNT(B2:B45),2)"));
    }

    [Fact]
    public void TheWorkedExampleFromTheGrammarDocument()
    {
        Assert.Equal("=(SUM(B2:B45) * 0.3)", Shape("=SUM(B2:B45)*0.3"));
    }

    [Theory]
    [InlineData("=1=2", "=(1 = 2)")]
    [InlineData("=1<>2", "=(1 <> 2)")]
    [InlineData("=1<=2", "=(1 <= 2)")]
    [InlineData("=1>=2", "=(1 >= 2)")]
    [InlineData("=\"a\"&\"b\"", "=(\"a\" & \"b\")")]
    [InlineData("=50%", "=(50%)")]
    [InlineData("=-A1", "=(-A1)")]
    [InlineData("=+A1", "=(+A1)")]
    public void OperatorsAreRecognised(string formula, string expected)
    {
        Assert.Equal(expected, Shape(formula));
    }

    [Fact]
    public void WhitespaceIsInsignificant()
    {
        Assert.Equal(Shape("=SUM(B2:B45)"), Shape("=  SUM ( B2 : B45 )  "));
    }
}
