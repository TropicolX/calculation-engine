using CalcEngine.Core;
using CalcEngine.Core.Model;

namespace CalcEngine.Core.Tests.Workbooks;

/// <summary>Content classification, storage and the public reading surface.</summary>
public class WorkbookTests
{
    [Fact]
    public void ANewWorkbookHasOneSheet()
    {
        var workbook = new Workbook();

        Assert.Single(workbook.Sheets);
        Assert.Equal("Sheet1", workbook.Sheets[0].Name);
    }

    [Theory]
    [InlineData("72.5", CellValueKind.Number)]
    [InlineData("-3", CellValueKind.Number)]
    [InlineData("1.2e3", CellValueKind.Number)]
    [InlineData("TRUE", CellValueKind.Boolean)]
    [InlineData("false", CellValueKind.Boolean)]
    [InlineData("#N/A", CellValueKind.Error)]
    [InlineData("Ngozi Okafor", CellValueKind.Text)]
    [InlineData("=1+1", CellValueKind.Number)]
    [InlineData("", CellValueKind.Blank)]
    [InlineData("   ", CellValueKind.Text)]
    public void ContentIsClassifiedBeforeItIsParsed(string content, CellValueKind expected)
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", content);

        Assert.Equal(expected, workbook.GetCellValue("Sheet1", "A1").Kind);
    }

    [Fact]
    public void TheRawContentIsKeptExactlyAsTyped()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "=  SUM( B1 : B9 )  ");

        Assert.Equal("=  SUM( B1 : B9 )  ", workbook.GetCellContent("Sheet1", "A1"));
    }

    [Fact]
    public void AMalformedFormulaIsStoredAndFlaggedRatherThanRejected()
    {
        var workbook = new Workbook();
        var result = workbook.SetCellContent("Sheet1", "A1", "=SUM(B1");

        Assert.Single(result.SyntaxErrors);
        Assert.Equal(ErrorKind.Parse, workbook.GetCellValue("Sheet1", "A1").AsError.Kind);
        Assert.Equal("=SUM(B1", workbook.GetCellContent("Sheet1", "A1"));
        Assert.Contains("never closed", workbook.GetCellValue("Sheet1", "A1").AsError.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearingACellMakesItBlankAndUpdatesItsDependents()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "10");
        workbook.SetCellContent("Sheet1", "B1", "=A1*2");

        workbook.SetCellContent("Sheet1", "A1", null);

        Assert.True(workbook.GetCellValue("Sheet1", "A1").IsBlank);
        Assert.Equal(0, workbook.GetCellValue("Sheet1", "B1").AsNumber);
    }

    [Fact]
    public void SheetsAreNamedCaseInsensitivelyAndCanBeAdded()
    {
        var workbook = new Workbook();
        workbook.AddSheet("Marks");

        Assert.Equal(2, workbook.Sheets.Count);
        Assert.True(workbook.TryGetSheet("marks", out var found));
        Assert.Equal("Marks", found.Name);
        Assert.Throws<InvalidOperationException>(() => workbook.AddSheet("MARKS"));
    }

    [Fact]
    public void FormulasReachAcrossSheets()
    {
        var workbook = new Workbook();
        workbook.AddSheet("Marks");
        workbook.SetCellContent("Marks", "B2", "70");
        workbook.SetCellContent("Sheet1", "A1", "=Marks!B2*0.3");

        Assert.Equal(21, workbook.GetCellValue("Sheet1", "A1").AsNumber);

        workbook.SetCellContent("Marks", "B2", "80");

        Assert.Equal(24, workbook.GetCellValue("Sheet1", "A1").AsNumber);
    }

    [Fact]
    public void AReferenceToAMissingSheetIsAReferenceError()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "=Nowhere!B2");

        Assert.Equal(ErrorKind.Reference, workbook.GetCellValue("Sheet1", "A1").AsError.Kind);
    }

    [Fact]
    public void EnumerationYieldsOnlyPopulatedCells()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "1");
        workbook.SetCellContent("Sheet1", "Z100", "2");
        workbook.SetCellContent("Sheet1", "B1", "=A1");

        var populated = workbook.Sheets[0].PopulatedCells().Select(c => c.ToA1()).Order().ToList();

        Assert.Equal(new[] { "A1", "B1", "Z100" }, populated);
    }

    [Fact]
    public void SettingTheSameContentTwiceIsNotAChange()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "10");

        var result = workbook.SetCellContent("Sheet1", "A1", "10");

        Assert.Empty(result.Changes);
    }

    [Fact]
    public void UsedRangeDescribesTheOccupiedRectangle()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "B2", "1");
        workbook.SetCellContent("Sheet1", "D7", "2");

        Assert.Equal(CellRange.Parse("B2:D7"), workbook.Sheets[0].UsedRange);
    }
}
