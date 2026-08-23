using System.Diagnostics;
using CalcEngine.Core;
using CalcEngine.Core.Model;

namespace CalcEngine.Core.Tests.Performance;

/// <summary>
/// The two published targets, as tests.
/// </summary>
/// <remarks>
/// <para>The benchmark harness under <c>benchmarks/</c> is the instrument; these
/// are the alarm. They run in the ordinary test configuration, which is slower
/// than the Release build the harness measures, and they still pass with two
/// orders of magnitude to spare — so a failure here means something regressed
/// structurally, not that the machine was busy.</para>
///
/// <para>Filter them out with
/// <c>dotnet test --filter "Category!=Performance"</c>.</para>
/// </remarks>
[Trait("Category", "Performance")]
public class PerformanceTargetTests
{
    private const int DataRows = 10_000;
    private const int DataColumns = 8;
    private const int CalcRows = 9_750;
    private const int ChainLength = 500;

    [Fact]
    public void ASingleEditPropagatesThroughFiveHundredDependentsWithinFiftyMilliseconds()
    {
        var workbook = BuildHundredThousandCellWorkbook();

        // Warm up: the first edit pays for JIT, not for the graph.
        for (var i = 0; i < 3; i++)
        {
            workbook.SetCellContent("Data", "A1", (5_000 + i).ToString());
        }

        var worst = 0.0;
        var evaluated = 0;
        for (var i = 0; i < 10; i++)
        {
            var start = Stopwatch.GetTimestamp();
            var result = workbook.SetCellContent("Data", "A1", (1_000 + i).ToString());
            worst = Math.Max(worst, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            evaluated = result.CellsEvaluated;
        }

        Assert.True(
            evaluated >= ChainLength,
            $"expected the {ChainLength}-cell chain to recompute, but only {evaluated} cells were evaluated");
        Assert.True(worst < 50, $"slowest propagation was {worst:F2} ms; the target is 50 ms");
    }

    [Fact]
    public void AFullRecalculationOfOneHundredThousandCellsCompletesWithinTwoSeconds()
    {
        var workbook = BuildHundredThousandCellWorkbook();
        workbook.RecalculateAll();   // warm up

        var start = Stopwatch.GetTimestamp();
        var result = workbook.RecalculateAll();
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        Assert.Equal(CalcRows * 2 + ChainLength, result.CellsEvaluated);
        Assert.True(elapsed < 2_000, $"full recalculation took {elapsed:F0} ms; the target is 2,000 ms");
    }

    [Fact]
    public void AnEditThatNothingDependsOnDoesNotWalkTheWorkbook()
    {
        // The structural claim behind the timing: cost follows the affected
        // subgraph, not the size of the workbook.
        var workbook = BuildHundredThousandCellWorkbook();

        // Row 100 is inside the Calc range: exactly its two formulas recompute,
        // and none of the other 99,998 cells is looked at.
        Assert.Equal(2, workbook.SetCellContent("Data", "H100", "12345").CellsEvaluated);

        // Rows beyond 9,750 have no formula reading them at all: nothing runs.
        Assert.Equal(0, workbook.SetCellContent("Data", $"H{DataRows}", "12345").CellsEvaluated);
    }

    /// <summary>
    /// Exactly 100,000 cells: 80,000 literals, 19,500 formulas and a 500-cell
    /// dependency chain rooted at <c>Data!A1</c>. The same shape the benchmark
    /// harness builds.
    /// </summary>
    private static Workbook BuildHundredThousandCellWorkbook()
    {
        var workbook = new Workbook(new WorkbookOptions
        {
            InitialSheetName = "Data",
            AutomaticCalculation = false,
        });
        workbook.AddSheet("Calc");
        workbook.AddSheet("Chain");

        var edits = new List<CellEdit>(100_000);

        for (var row = 1; row <= DataRows; row++)
        {
            for (var column = 1; column <= DataColumns; column++)
            {
                edits.Add(new CellEdit(
                    new SheetCellAddress("Data", new CellAddress(column, row)),
                    ((row * column) % 100).ToString()));
            }
        }

        for (var row = 1; row <= CalcRows; row++)
        {
            edits.Add(new CellEdit(
                new SheetCellAddress("Calc", new CellAddress(1, row)),
                $"=SUM(Data!A{row}:H{row})"));
            edits.Add(new CellEdit(
                new SheetCellAddress("Calc", new CellAddress(2, row)),
                $"=ROUND(A{row}*0.3,2)"));
        }

        edits.Add(new CellEdit(new SheetCellAddress("Chain", CellAddress.Parse("A1")), "=Data!A1*2"));
        for (var row = 2; row <= ChainLength; row++)
        {
            edits.Add(new CellEdit(new SheetCellAddress("Chain", new CellAddress(1, row)), $"=A{row - 1}+1"));
        }

        using (workbook.History.Suspend())
        {
            workbook.SetCellContents(edits, "load");
        }

        workbook.RecalculateAll();
        workbook.AutomaticCalculation = true;
        Assert.Equal(100_000, workbook.TrackedCellCount);
        return workbook;
    }
}
