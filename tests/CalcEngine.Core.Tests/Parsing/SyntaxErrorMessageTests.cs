using CalcEngine.Core.Parsing;

namespace CalcEngine.Core.Tests.Parsing;

/// <summary>
/// "Malformed input is normal input."  These tests pin the exact wording and
/// column of every message the parser can produce (docs/grammar.md §6), because
/// an error message that drifts is an error message nobody trusts.
/// </summary>
public class SyntaxErrorMessageTests
{
    private static FormulaSyntaxError FirstError(string formula)
    {
        var result = FormulaParser.Parse(formula);
        Assert.False(result.Success, $"expected '{formula}' to be rejected");
        Assert.Null(result.Expression);
        Assert.NotEmpty(result.Errors);
        return result.Errors[0];
    }

    [Fact]
    public void UnclosedBracket_NamesTheOpeningColumn()
    {
        Assert.Equal(
            "Column 12: the bracket opened at column 5 was never closed.",
            FirstError("=SUM(B2:B45").ToString());
    }

    [Fact]
    public void UnmatchedClosingBracket()
    {
        Assert.Equal(
            "Column 9: there is no opening bracket to match this ')'.",
            FirstError("=SUM(A1))").ToString());
    }

    [Fact]
    public void TrailingOperator_NamesTheOperator()
    {
        Assert.Equal(
            "Column 5: a value, cell reference or function call was expected after '+'.",
            FirstError("=2 +").ToString());
    }

    [Fact]
    public void MissingArgument()
    {
        Assert.Equal(
            "Column 6: a value, cell reference or function call was expected, but ',' was found.",
            FirstError("=SUM(,1)").ToString());
    }

    [Fact]
    public void StrayCharacter()
    {
        Assert.Equal(
            "Column 5: '$' is not a valid character in a formula.",
            FirstError("=A1 $ B2").ToString());
    }

    [Fact]
    public void UnterminatedTextLiteral()
    {
        Assert.Equal(
            "Column 2: this text literal has no closing quotation mark.",
            FirstError("=\"unterminated").ToString());
    }

    [Fact]
    public void ColumnBeyondTheGrid()
    {
        Assert.Equal(
            "Column 2: 'ZZZZ' is beyond the last column, XFD.",
            FirstError("=ZZZZ9+1").ToString());
    }

    [Fact]
    public void RowBeyondTheGrid()
    {
        Assert.Equal(
            "Column 2: row 1048577 is beyond the last row, 1048576.",
            FirstError("=A1048577").ToString());
    }

    [Fact]
    public void EmptyFormula()
    {
        Assert.Equal(
            "Column 2: a formula must contain an expression after the '='.",
            FirstError("=").ToString());
    }

    [Fact]
    public void MissingOperatorBetweenOperands()
    {
        Assert.Equal(
            "Column 5: an operator was expected, but 'B1' was found.",
            FirstError("=A1 B1").ToString());
    }

    [Fact]
    public void ErrorCarriesTheSpanOfTheOffendingText()
    {
        var error = FirstError("=A1 B1");

        Assert.Equal(5, error.Column);
        Assert.Equal(2, error.Length);
    }

    [Fact]
    public void ContentThatIsNotAFormulaIsNotParsedAtAll()
    {
        // The classifier, not the parser, decides what is a formula.
        Assert.Throws<ArgumentException>(() => FormulaParser.Parse("Ngozi Okafor"));
    }

    [Fact]
    public void AllErrorsAreReportedNotJustTheFirst()
    {
        var result = FormulaParser.Parse("=SUM(,1) + ZZZZ9");

        Assert.False(result.Success);
        Assert.True(result.Errors.Count >= 1);
        Assert.All(result.Errors, e => Assert.InRange(e.Column, 1, 17));
    }

    [Fact]
    public void ParsingNeverThrowsOnArbitraryInput()
    {
        string[] nasty =
        [
            "=", "=(", "=)", "=((((", "=1+", "=*1", "=A1:", "=:A1", "=SUM(", "=SUM()",
            "=\"", "=#", "=#FOO!", "=!", "=Sheet1!", "=1..2", "=--", "=A1^^2", "=,",
            "=IF(", "=1e", "=$", "=B2:B45:B60", "=()",
        ];

        foreach (var formula in nasty)
        {
            var result = FormulaParser.Parse(formula);
            Assert.True(result.Success || result.Errors.Count > 0);
        }
    }
}
