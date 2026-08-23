using CalcEngine.Core;
using CalcEngine.Core.Parsing;

namespace CalcEngine.Core.Tests;

/// <summary>
/// The example in README.md, executed.
/// </summary>
/// <remarks>
/// Documentation that is not executed is documentation that is wrong by the end
/// of the term. Every value quoted in the README's "Using the API" section is
/// asserted here, so the first person to change the API breaks this test rather
/// than misleading the next reader.
/// </remarks>
public class ReadmeExampleTests
{
    [Fact]
    public void TheReadmeExampleBehavesAsDocumented()
    {
        var workbook = new Workbook();
        workbook.AddSheet("Marks");

        workbook.SetCellContent("Marks", "B2", "72");
        workbook.SetCellContent("Marks", "C2", "=ROUND(B2*0.3,1)");

        var result = workbook.SetCellContent("Marks", "B2", "85");

        Assert.Equal(1, result.CellsEvaluated);      // only C2 needed recomputing
        Assert.Equal(2, result.Changes.Count);       // B2 itself, then C2

        // Changes are in evaluation order, so the edited cell comes first and
        // its dependents follow: a client repainting in this order never shows
        // a dependent before the cell it was computed from.
        Assert.Equal("Marks!B2", result.Changes[0].Address.ToA1());
        Assert.Equal("Marks!C2", result.Changes[1].Address.ToA1());
        Assert.Equal(25.5, result.Changes[1].NewValue.AsNumber, precision: 10);
        Assert.Equal(25.5, workbook.GetCellValue("Marks", "C2").AsNumber, precision: 10);

        // Errors are values, never exceptions.
        workbook.SetCellContent("Marks", "D2", "=B2/0");
        Assert.Equal("#DIV/0!", workbook.GetCellValue("Marks", "D2").AsError.Code);

        // A malformed formula is reported, and the text the user typed is kept.
        var bad = workbook.SetCellContent("Marks", "E2", "=SUM(B2:B45");
        Assert.Equal(
            "Column 12: the bracket opened at column 5 was never closed.",
            bad.SyntaxErrors[0].ToString());
        Assert.Equal("=SUM(B2:B45", workbook.GetCellContent("Marks", "E2"));

        // A cycle is reported with its exact path, never as a crash or a hang.
        workbook.SetCellContent("Marks", "F2", "=G2+1");
        var cycle = workbook.SetCellContent("Marks", "G2", "=F2+1");
        Assert.Equal("F2 → G2 → F2", cycle.Cycles[0].ToString());
        Assert.Equal("F2 refers to G2, which refers back to F2", cycle.Cycles[0].ToSentence());

        // One undo per operation.
        workbook.History.Undo();
        Assert.Null(workbook.GetCellContent("Marks", "G2"));
        Assert.Empty(workbook.RecalculateAll().Cycles);
    }

    [Fact]
    public void TheGrammarDocumentsErrorCatalogueIsAccurate()
    {
        // docs/grammar.md §6 lists these verbatim; a drifting message is a
        // message nobody trusts.
        var expected = new (string Formula, string Message)[]
        {
            ("=SUM(B2:B45", "Column 12: the bracket opened at column 5 was never closed."),
            ("=2 +", "Column 5: a value, cell reference or function call was expected after '+'."),
            ("=SUM(,1)", "Column 6: a value, cell reference or function call was expected, but ',' was found."),
            ("=A1 $ B2", "Column 5: '$' is not a valid character in a formula."),
            ("=\"unterminated", "Column 2: this text literal has no closing quotation mark."),
            ("=ZZZZ9+1", "Column 2: 'ZZZZ' is beyond the last column, XFD."),
        };

        foreach (var (formula, message) in expected)
        {
            var parsed = FormulaParser.Parse(formula);
            Assert.False(parsed.Success, formula);
            Assert.Equal(message, parsed.Errors[0].ToString());
        }
    }

    [Fact]
    public void TheDesignPortfolioWorkedExampleIsAccurate()
    {
        // design-portfolio.md and grammar.md §7 both use =SUM(B2:B45)*0.3.
        var workbook = new Workbook();
        for (var row = 2; row <= 45; row++)
        {
            workbook.SetCellContent("Sheet1", $"B{row}", "10");
        }

        workbook.SetCellContent("Sheet1", "C1", "=SUM(B2:B45)*0.3");

        Assert.Equal(132, workbook.GetCellValue("Sheet1", "C1").AsNumber, precision: 10);
    }
}
