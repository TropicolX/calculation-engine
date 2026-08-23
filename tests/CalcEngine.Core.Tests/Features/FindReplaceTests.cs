using CalcEngine.Core;
using CalcEngine.Core.Features.FindReplace;
using CalcEngine.Core.Model;

namespace CalcEngine.Core.Tests.Features;

/// <summary>
/// Group E, assigned feature 1: Find and Replace.
/// </summary>
/// <remarks>
/// The interesting cases are not "does it find the string". They are what
/// happens when the string is inside a formula, inside a computed value, or in
/// a place where replacing it would break the workbook.
/// </remarks>
public class FindReplaceTests
{
    private static Workbook Results()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "Course");
        workbook.SetCellContent("Sheet1", "B1", "Mark");
        workbook.SetCellContent("Sheet1", "C1", "Grade");
        workbook.SetCellContent("Sheet1", "A2", "CSC322");
        workbook.SetCellContent("Sheet1", "B2", "72");
        workbook.SetCellContent("Sheet1", "C2", "=IF(B2>=50,\"PASSED\",\"FAILED\")");
        workbook.SetCellContent("Sheet1", "A3", "csc322");
        workbook.SetCellContent("Sheet1", "B3", "41");
        workbook.SetCellContent("Sheet1", "C3", "=IF(B3>=50,\"PASSED\",\"FAILED\")");
        workbook.SetCellContent("Sheet1", "A4", "MTH201");
        workbook.SetCellContent("Sheet1", "B4", "55");
        workbook.SetCellContent("Sheet1", "C4", "=IF(B4>=50,\"PASSED\",\"FAILED\")");
        return workbook;
    }

    private static FindReplaceService Service(Workbook workbook) => new(workbook);

    // ------------------------------------------------------------------ find

    [Fact]
    public void FindAll_IsCaseInsensitiveByDefault()
    {
        var matches = Service(Results()).FindAll(new FindOptions { SearchText = "CSC322" });

        Assert.Equal(new[] { "A2", "A3" }, matches.Select(m => m.Address.Address.ToA1()));
    }

    [Fact]
    public void FindAll_CanMatchCase()
    {
        var matches = Service(Results()).FindAll(new FindOptions { SearchText = "CSC322", MatchCase = true });

        Assert.Equal(new[] { "A2" }, matches.Select(m => m.Address.Address.ToA1()));
    }

    [Fact]
    public void FindAll_MatchesInsideAValueByDefault()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "Okafor, Ngozi");

        var matches = Service(workbook).FindAll(new FindOptions { SearchText = "Ngozi" });
        var match = Assert.Single(matches);

        Assert.Equal(8, match.Start);
        Assert.Equal("Ngozi", match.MatchedText);
    }

    [Fact]
    public void FindAll_CanRequireTheWholeCellToMatch()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "Okafor, Ngozi");
        workbook.SetCellContent("Sheet1", "A2", "Ngozi");

        var matches = Service(workbook).FindAll(
            new FindOptions { SearchText = "Ngozi", MatchEntireCell = true });

        Assert.Equal(new[] { "A2" }, matches.Select(m => m.Address.Address.ToA1()));
    }

    [Fact]
    public void FindAll_ReportsEveryOccurrenceWithinACell()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "ab-ab-ab");

        var matches = Service(workbook).FindAll(new FindOptions { SearchText = "ab" });

        Assert.Equal(3, matches.Count);
        Assert.Equal(new[] { 0, 3, 6 }, matches.Select(m => m.Start));
    }

    [Fact]
    public void FindAll_SearchesRawContentSoFormulasAreVisible()
    {
        var matches = Service(Results()).FindAll(new FindOptions { SearchText = "PASSED" });

        Assert.Equal(new[] { "C2", "C3", "C4" }, matches.Select(m => m.Address.Address.ToA1()));
    }

    [Fact]
    public void FindAll_CanSearchDisplayedValuesInstead()
    {
        var matches = Service(Results()).FindAll(
            new FindOptions { SearchText = "PASSED", LookIn = SearchIn.DisplayValue });

        // C3 computed FAILED, so only the two that actually show PASSED match.
        Assert.Equal(new[] { "C2", "C4" }, matches.Select(m => m.Address.Address.ToA1()));
    }

    [Fact]
    public void FindAll_HonoursAnExplicitRange()
    {
        var matches = Service(Results()).FindAll(
            new FindOptions { SearchText = "CSC322", Range = CellRange.Parse("A3:C4") });

        Assert.Equal(new[] { "A3" }, matches.Select(m => m.Address.Address.ToA1()));
    }

    [Fact]
    public void FindAll_SpansEverySheetUnlessOneIsNamed()
    {
        var workbook = Results();
        workbook.AddSheet("Marks");
        workbook.SetCellContent("Marks", "A1", "CSC322");

        Assert.Equal(3, Service(workbook).FindAll(new FindOptions { SearchText = "CSC322" }).Count);
        Assert.Single(Service(workbook).FindAll(new FindOptions { SearchText = "CSC322", SheetName = "Marks" }));
    }

    [Fact]
    public void FindAll_OrdersByRowsOrByColumns()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "x");
        workbook.SetCellContent("Sheet1", "B1", "x");
        workbook.SetCellContent("Sheet1", "A2", "x");

        var byRows = Service(workbook).FindAll(new FindOptions { SearchText = "x" });
        var byColumns = Service(workbook).FindAll(
            new FindOptions { SearchText = "x", Order = SearchOrder.ByColumns });

        Assert.Equal(new[] { "A1", "B1", "A2" }, byRows.Select(m => m.Address.Address.ToA1()));
        Assert.Equal(new[] { "A1", "A2", "B1" }, byColumns.Select(m => m.Address.Address.ToA1()));
    }

    [Fact]
    public void FindAll_SupportsRegularExpressions()
    {
        var matches = Service(Results()).FindAll(
            new FindOptions { SearchText = @"^[A-Z]{3}\d{3}$", UseRegex = true, MatchCase = true });

        Assert.Equal(new[] { "A2", "A4" }, matches.Select(m => m.Address.Address.ToA1()));
    }

    [Fact]
    public void FindAll_RejectsAnInvalidRegularExpressionWithoutThrowing()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Service(Results()).FindAll(new FindOptions { SearchText = "([", UseRegex = true }));

        Assert.Contains("regular expression", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindAll_OfAnEmptySearchTextIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Service(Results()).FindAll(new FindOptions { SearchText = "" }));
    }

    [Fact]
    public void FindNext_WalksTheMatchesAndWrapsAround()
    {
        var service = Service(Results());
        var options = new FindOptions { SearchText = "CSC322" };

        var first = service.FindNext(options);
        Assert.Equal("A2", first!.Address.Address.ToA1());

        var second = service.FindNext(options, first.Address);
        Assert.Equal("A3", second!.Address.Address.ToA1());

        var wrapped = service.FindNext(options, second.Address);
        Assert.Equal("A2", wrapped!.Address.Address.ToA1());
    }

    [Fact]
    public void FindNext_ReturnsNullWhenNothingMatches()
    {
        Assert.Null(Service(Results()).FindNext(new FindOptions { SearchText = "PHY101" }));
    }

    [Fact]
    public void Count_AgreesWithFindAll()
    {
        var service = Service(Results());
        var options = new FindOptions { SearchText = "PASSED" };

        Assert.Equal(service.FindAll(options).Count, service.Count(options));
    }

    // --------------------------------------------------------------- replace

    [Fact]
    public void ReplaceAll_RewritesLiteralCells()
    {
        var workbook = Results();

        var result = Service(workbook).ReplaceAll(new FindOptions { SearchText = "CSC322" }, "CSC 322");

        Assert.Equal(2, result.CellsChanged);
        Assert.Equal("CSC 322", workbook.GetCellContent("Sheet1", "A2"));
        Assert.Equal("CSC 322", workbook.GetCellContent("Sheet1", "A3"));
    }

    [Fact]
    public void ReplaceAll_IsASingleUndoableOperation()
    {
        var workbook = Results();
        var before = workbook.History.UndoCount;

        Service(workbook).ReplaceAll(new FindOptions { SearchText = "PASSED" }, "PASS");

        Assert.Equal(before + 1, workbook.History.UndoCount);
        Assert.Equal("replace \"PASSED\" with \"PASS\" in 3 cells", workbook.History.NextUndoDescription);

        workbook.History.Undo();

        Assert.Equal("=IF(B2>=50,\"PASSED\",\"FAILED\")", workbook.GetCellContent("Sheet1", "C2"));
    }

    [Fact]
    public void ReplaceAll_RecalculatesWhatItChanged()
    {
        var workbook = Results();

        Service(workbook).ReplaceAll(new FindOptions { SearchText = "PASSED" }, "CREDIT");

        Assert.Equal("CREDIT", workbook.GetCellValue("Sheet1", "C2").ToDisplayString());
        Assert.Equal("FAILED", workbook.GetCellValue("Sheet1", "C3").ToDisplayString());
    }

    [Fact]
    public void ReplaceAll_CanBeToldToLeaveFormulasAlone()
    {
        var workbook = Results();

        var result = Service(workbook).ReplaceAll(
            new FindOptions { SearchText = "PASSED", Formulas = FormulaHandling.SkipFormulas },
            "PASS");

        Assert.Equal(0, result.CellsChanged);
        Assert.Equal(3, result.Skipped.Count);
        Assert.All(result.Skipped, s => Assert.Equal(ReplaceSkipReason.FormulaSkippedByOption, s.Reason));
    }

    [Fact]
    public void ReplaceAll_InTextLiteralsOnlyLeavesReferencesUntouched()
    {
        // The failure a naive replace produces: turning "B2" into "B3" inside
        // =SUM(B2:B9) silently rewrites the range.  In TextLiteralsOnly mode the
        // AST decides what is text and what is structure.
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "=CONCAT(\"B2 total: \",SUM(B2:B9))");
        workbook.SetCellContent("Sheet1", "B2", "5");

        var result = Service(workbook).ReplaceAll(
            new FindOptions { SearchText = "B2", Formulas = FormulaHandling.TextLiteralsOnly },
            "B3");

        Assert.Equal(1, result.CellsChanged);
        Assert.Equal("=CONCAT(\"B3 total: \",SUM(B2:B9))", workbook.GetCellContent("Sheet1", "A1"));
    }

    [Fact]
    public void ReplaceAll_InWholeContentModeWouldHaveBrokenThatFormula()
    {
        // Same edit, default mode: the range really is rewritten.  The test
        // exists so that the difference between the two modes is documented and
        // cannot regress unnoticed.
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "=CONCAT(\"B2 total: \",SUM(B2:B9))");

        Service(workbook).ReplaceAll(new FindOptions { SearchText = "B2" }, "B3");

        Assert.Equal("=CONCAT(\"B3 total: \",SUM(B3:B9))", workbook.GetCellContent("Sheet1", "A1"));
    }

    [Fact]
    public void ReplaceAll_SkipsAFormulaCellWithNoMatchingTextLiteral()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "=SUM(B2:B9)");

        var result = Service(workbook).ReplaceAll(
            new FindOptions { SearchText = "B2", Formulas = FormulaHandling.TextLiteralsOnly },
            "B3");

        Assert.Equal(0, result.CellsChanged);
        Assert.Equal(ReplaceSkipReason.NoTextLiteralMatch, Assert.Single(result.Skipped).Reason);
        Assert.Equal("=SUM(B2:B9)", workbook.GetCellContent("Sheet1", "A1"));
    }

    [Fact]
    public void ReplaceAll_RefusesAReplacementThatWouldNotParse()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "=SUM(B2:B9)");

        var result = Service(workbook).ReplaceAll(new FindOptions { SearchText = "B9" }, "B9)");

        Assert.Equal(0, result.CellsChanged);
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal(ReplaceSkipReason.WouldBreakFormula, skipped.Reason);
        Assert.Contains("no opening bracket", skipped.Explanation, StringComparison.Ordinal);
        Assert.Equal("=SUM(B2:B9)", workbook.GetCellContent("Sheet1", "A1"));
    }

    [Fact]
    public void ReplaceAll_CanBeToldToAcceptABrokenFormulaAnyway()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "=SUM(B2:B9)");

        var result = Service(workbook).ReplaceAll(
            new FindOptions { SearchText = "B9", ValidateFormulas = false },
            "B9)");

        Assert.Equal(1, result.CellsChanged);
        Assert.Equal(ErrorKind.Parse, workbook.GetCellValue("Sheet1", "A1").AsError.Kind);
    }

    [Fact]
    public void ReplaceAll_CannotChangeAComputedValue()
    {
        var workbook = Results();

        var result = Service(workbook).ReplaceAll(
            new FindOptions { SearchText = "PASSED", LookIn = SearchIn.DisplayValue },
            "PASS");

        Assert.Equal(0, result.CellsChanged);
        Assert.Equal(2, result.Skipped.Count);
        Assert.All(result.Skipped, s => Assert.Equal(ReplaceSkipReason.ComputedValue, s.Reason));
    }

    [Fact]
    public void ReplaceAll_CountsOccurrencesAsWellAsCells()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "ab-ab-ab");
        workbook.SetCellContent("Sheet1", "A2", "ab");

        var result = Service(workbook).ReplaceAll(new FindOptions { SearchText = "ab" }, "xy");

        Assert.Equal(2, result.CellsChanged);
        Assert.Equal(4, result.OccurrencesReplaced);
        Assert.Equal("xy-xy-xy", workbook.GetCellContent("Sheet1", "A1"));
    }

    [Fact]
    public void ReplaceAll_WithRegexSupportsGroupReferences()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "Okafor, Ngozi");
        workbook.SetCellContent("Sheet1", "A2", "Adeyemi, Bola");

        Service(workbook).ReplaceAll(
            new FindOptions { SearchText = @"(\w+), (\w+)", UseRegex = true },
            "$2 $1");

        Assert.Equal("Ngozi Okafor", workbook.GetCellContent("Sheet1", "A1"));
        Assert.Equal("Bola Adeyemi", workbook.GetCellContent("Sheet1", "A2"));
    }

    [Fact]
    public void ReplaceAll_WithNothingToDoChangesNothing()
    {
        var workbook = Results();
        var before = workbook.History.UndoCount;

        var result = Service(workbook).ReplaceAll(new FindOptions { SearchText = "PHY101" }, "x");

        Assert.Equal(0, result.CellsChanged);
        Assert.Equal(before, workbook.History.UndoCount);
    }

    [Fact]
    public void ReplaceAll_EmptyReplacementDeletesTheText()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "CSC-322");

        Service(workbook).ReplaceAll(new FindOptions { SearchText = "-" }, "");

        Assert.Equal("CSC322", workbook.GetCellContent("Sheet1", "A1"));
    }

    [Fact]
    public void Replace_ChangesOneMatchOnly()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "ab-ab");
        var service = Service(workbook);
        var options = new FindOptions { SearchText = "ab" };

        var match = service.FindNext(options)!;
        service.Replace(match, "xy");

        Assert.Equal("xy-ab", workbook.GetCellContent("Sheet1", "A1"));
    }

    [Fact]
    public void ReplaceAll_TurningTextIntoANumberReclassifiesTheCell()
    {
        // Replacement rewrites content, so the classifier runs again: this is
        // how "n/a" scattered through a marks column becomes 0.
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "n/a");
        workbook.SetCellContent("Sheet1", "A2", "=A1+1");

        Service(workbook).ReplaceAll(
            new FindOptions { SearchText = "n/a", MatchEntireCell = true },
            "0");

        Assert.True(workbook.GetCellValue("Sheet1", "A1").IsNumber);
        Assert.Equal(1, workbook.GetCellValue("Sheet1", "A2").AsNumber);
    }
}
