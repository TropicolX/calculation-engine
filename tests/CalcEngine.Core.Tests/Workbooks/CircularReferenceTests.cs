using CalcEngine.Core;
using CalcEngine.Core.Model;

namespace CalcEngine.Core.Tests.Workbooks;

/// <summary>
/// "Circular references must be detected and reported to the client with the
/// exact cycle, never with a crash or an infinite loop."
/// </summary>
public class CircularReferenceTests
{
    [Fact]
    public void ACellThatRefersToItselfIsACycleOfOne()
    {
        var workbook = new Workbook();

        var result = workbook.SetCellContent("Sheet1", "A1", "=A1+1");

        var cycle = Assert.Single(result.Cycles);
        Assert.Equal("A1 → A1", cycle.ToString());
        Assert.Equal(ErrorKind.Circular, workbook.GetCellValue("Sheet1", "A1").AsError.Kind);
    }

    [Fact]
    public void TheExactCycleIsReportedInReferenceOrder()
    {
        // A1 refers to B3, B3 refers to C7, C7 refers back to A1.
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "=B3+1");
        workbook.SetCellContent("Sheet1", "B3", "=C7+1");
        var result = workbook.SetCellContent("Sheet1", "C7", "=A1+1");

        var cycle = Assert.Single(result.Cycles);

        Assert.Equal("A1 → B3 → C7 → A1", cycle.ToString());
        Assert.Equal(new[] { "A1", "B3", "C7" }, cycle.Path.Select(a => a.Address.ToA1()));
    }

    [Fact]
    public void EveryCellInTheCycleIsFlagged()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "=B3+1");
        workbook.SetCellContent("Sheet1", "B3", "=C7+1");
        workbook.SetCellContent("Sheet1", "C7", "=A1+1");

        foreach (var address in new[] { "A1", "B3", "C7" })
        {
            var value = workbook.GetCellValue("Sheet1", address);
            Assert.Equal(ErrorKind.Circular, value.AsError.Kind);
            Assert.Contains("A1 → B3 → C7 → A1", value.AsError.Detail!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheCycleIsReportedTheSameWayWhicheverCellClosedIt()
    {
        var first = new Workbook();
        first.SetCellContent("Sheet1", "A1", "=B3+1");
        first.SetCellContent("Sheet1", "B3", "=C7+1");
        var closedByC7 = first.SetCellContent("Sheet1", "C7", "=A1+1");

        var second = new Workbook();
        second.SetCellContent("Sheet1", "C7", "=A1+1");
        second.SetCellContent("Sheet1", "B3", "=C7+1");
        var closedByA1 = second.SetCellContent("Sheet1", "A1", "=B3+1");

        Assert.Equal(closedByC7.Cycles[0].ToString(), closedByA1.Cycles[0].ToString());
    }

    [Fact]
    public void BreakingTheCycleRestoresEveryCell()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "=B3+1");
        workbook.SetCellContent("Sheet1", "B3", "=C7+1");
        workbook.SetCellContent("Sheet1", "C7", "=A1+1");

        var result = workbook.SetCellContent("Sheet1", "C7", "10");

        Assert.Empty(result.Cycles);
        Assert.Equal(12, workbook.GetCellValue("Sheet1", "A1").AsNumber);
        Assert.Equal(11, workbook.GetCellValue("Sheet1", "B3").AsNumber);
    }

    [Fact]
    public void ACycleThroughARangeIsDetected()
    {
        var workbook = new Workbook();

        var result = workbook.SetCellContent("Sheet1", "B5", "=SUM(B1:B10)");

        Assert.Single(result.Cycles);
        Assert.Equal(ErrorKind.Circular, workbook.GetCellValue("Sheet1", "B5").AsError.Kind);
    }

    [Fact]
    public void ACycleAcrossSheetsNamesBothSheets()
    {
        var workbook = new Workbook();
        workbook.AddSheet("Marks");
        workbook.SetCellContent("Sheet1", "A1", "=Marks!A1+1");
        var result = workbook.SetCellContent("Marks", "A1", "=Sheet1!A1+1");

        var cycle = Assert.Single(result.Cycles);

        Assert.Contains("Marks!A1", cycle.ToString(), StringComparison.Ordinal);
        Assert.Contains("Sheet1!A1", cycle.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CellsDownstreamOfACycleSeeAnError()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "=B1");
        workbook.SetCellContent("Sheet1", "B1", "=A1");
        workbook.SetCellContent("Sheet1", "C1", "=A1+1");

        Assert.True(workbook.GetCellValue("Sheet1", "C1").IsError);
    }

    [Fact]
    public void TwoIndependentCyclesAreBothReported()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "=A2");
        workbook.SetCellContent("Sheet1", "A2", "=A1");
        workbook.SetCellContent("Sheet1", "D1", "=D2");
        workbook.SetCellContent("Sheet1", "D2", "=D1");

        var result = workbook.RecalculateAll();

        Assert.Equal(2, result.Cycles.Count);
    }

    [Fact]
    public void TheCycleReadsAsASentenceForAClientWithoutADiagram()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "=B3+1");
        workbook.SetCellContent("Sheet1", "B3", "=C7+1");
        var result = workbook.SetCellContent("Sheet1", "C7", "=A1+1");

        Assert.Equal(
            "A1 refers to B3, which refers to C7, which refers back to A1",
            result.Cycles[0].ToSentence());
    }

    [Fact]
    public void ASelfReferenceReadsAsSuch()
    {
        var workbook = new Workbook();
        var result = workbook.SetCellContent("Sheet1", "A1", "=A1+1");

        Assert.Equal("A1 refers to itself", result.Cycles[0].ToSentence());
    }

    [Fact]
    public void AVeryLongCycleAbbreviatesWhenRenderedButKeepsEveryCellInItsPath()
    {
        var workbook = new Workbook();
        const int Length = 40;
        for (var row = 1; row <= Length; row++)
        {
            var previous = row == 1 ? Length : row - 1;
            workbook.SetCellContent("Sheet1", $"A{row}", $"=A{previous}+1");
        }

        var cycle = workbook.RecalculateAll().Cycles[0];

        Assert.Equal(Length, cycle.Path.Count);
        Assert.Contains("more)", cycle.ToString(), StringComparison.Ordinal);
        Assert.True(cycle.ToString().Length < 200, "a rendered cycle must stay readable");
    }

    [Fact]
    public void ALongCycleDoesNotOverflowTheStack()
    {
        // 20,000 cells in one ring: a recursive detector would die here.
        var workbook = new Workbook();
        const int Length = 20_000;
        for (var row = 1; row <= Length; row++)
        {
            var previous = row == 1 ? Length : row - 1;
            workbook.SetCellContent("Sheet1", $"A{row}", $"=A{previous}+1");
        }

        var result = workbook.RecalculateAll();

        Assert.Single(result.Cycles);
        Assert.Equal(Length, result.Cycles[0].Path.Count);
    }

    [Fact]
    public void ADeepChainDoesNotOverflowTheStack()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "1");
        for (var row = 2; row <= 20_000; row++)
        {
            workbook.SetCellContent("Sheet1", $"A{row}", $"=A{row - 1}+1");
        }

        workbook.SetCellContent("Sheet1", "A1", "0");

        Assert.Equal(19_999, workbook.GetCellValue("Sheet1", "A20000").AsNumber);
    }
}
