using CalcEngine.Core;
using CalcEngine.Core.Model;

namespace CalcEngine.Core.Tests.Workbooks;

/// <summary>
/// Reactive recalculation: when one cell changes, only the cells that depend on
/// it are recomputed, and in an order that never reads a stale value.
/// </summary>
public class RecalculationTests
{
    [Fact]
    public void ADependentUpdatesWhenItsPrecedentChanges()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "10");
        workbook.SetCellContent("Sheet1", "B1", "=A1*2");

        workbook.SetCellContent("Sheet1", "A1", "21");

        Assert.Equal(42, workbook.GetCellValue("Sheet1", "B1").AsNumber);
    }

    [Fact]
    public void AChainRecalculatesInDependencyOrder()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "1");
        for (var row = 2; row <= 50; row++)
        {
            workbook.SetCellContent("Sheet1", $"A{row}", $"=A{row - 1}+1");
        }

        workbook.SetCellContent("Sheet1", "A1", "100");

        Assert.Equal(149, workbook.GetCellValue("Sheet1", "A50").AsNumber);
    }

    [Fact]
    public void OnlyAffectedCellsAreRecomputed()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "1");
        workbook.SetCellContent("Sheet1", "B1", "=A1+1");   // depends on A1
        workbook.SetCellContent("Sheet1", "C1", "=B1+1");   // depends on A1, transitively
        workbook.SetCellContent("Sheet1", "D1", "=99");     // depends on nothing
        workbook.SetCellContent("Sheet1", "E1", "=D1+1");   // unaffected subtree

        var result = workbook.SetCellContent("Sheet1", "A1", "2");

        // A1 itself plus exactly its two dependents; D1 and E1 are never touched.
        Assert.Equal(2, result.CellsEvaluated);
        Assert.Equal(
            new[] { "A1", "B1", "C1" },
            result.Changes.Select(c => c.Address.Address.ToA1()).Order());
    }

    [Fact]
    public void ADiamondRecomputesTheSharedCellOnlyOnce()
    {
        //      A1
        //     /  \
        //    B1  C1
        //     \  /
        //      D1
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "1");
        workbook.SetCellContent("Sheet1", "B1", "=A1*2");
        workbook.SetCellContent("Sheet1", "C1", "=A1*3");
        workbook.SetCellContent("Sheet1", "D1", "=B1+C1");

        var result = workbook.SetCellContent("Sheet1", "A1", "10");

        Assert.Equal(3, result.CellsEvaluated);
        Assert.Equal(50, workbook.GetCellValue("Sheet1", "D1").AsNumber);
    }

    [Fact]
    public void ARangeDependencyIsTriggeredByAnyCellInsideIt()
    {
        var workbook = new Workbook();
        for (var row = 2; row <= 45; row++)
        {
            workbook.SetCellContent("Sheet1", $"B{row}", "1");
        }

        workbook.SetCellContent("Sheet1", "C1", "=SUM(B2:B45)*0.3");
        Assert.Equal(13.2, workbook.GetCellValue("Sheet1", "C1").AsNumber, precision: 10);

        workbook.SetCellContent("Sheet1", "B20", "11");

        Assert.Equal(16.2, workbook.GetCellValue("Sheet1", "C1").AsNumber, precision: 10);
    }

    [Fact]
    public void ACellOutsideTheRangeDoesNotTriggerIt()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "C1", "=SUM(B2:B45)");

        var result = workbook.SetCellContent("Sheet1", "B46", "1000");

        Assert.Equal(0, result.CellsEvaluated);
        Assert.Equal(0, workbook.GetCellValue("Sheet1", "C1").AsNumber);
    }

    [Fact]
    public void WritingIntoAPreviouslyEmptyCellOfARangeStillTriggersIt()
    {
        // The cell had no identity in the graph until now: the range index, not
        // an edge list, is what makes this work.
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "C1", "=SUM(B2:B45)");

        workbook.SetCellContent("Sheet1", "B30", "7");

        Assert.Equal(7, workbook.GetCellValue("Sheet1", "C1").AsNumber);
    }

    [Fact]
    public void EditingAFormulaRemovesTheEdgesItNoLongerNeeds()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "1");
        workbook.SetCellContent("Sheet1", "B1", "2");
        workbook.SetCellContent("Sheet1", "C1", "=A1");

        workbook.SetCellContent("Sheet1", "C1", "=B1");

        // A1 must no longer reach C1.
        var result = workbook.SetCellContent("Sheet1", "A1", "999");

        Assert.Equal(0, result.CellsEvaluated);
        Assert.Equal(2, workbook.GetCellValue("Sheet1", "C1").AsNumber);
    }

    [Fact]
    public void ReplacingAFormulaWithALiteralDropsItsDependencies()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "1");
        workbook.SetCellContent("Sheet1", "B1", "=A1+1");

        workbook.SetCellContent("Sheet1", "B1", "50");
        var result = workbook.SetCellContent("Sheet1", "A1", "2");

        Assert.Equal(0, result.CellsEvaluated);
        Assert.Equal(50, workbook.GetCellValue("Sheet1", "B1").AsNumber);
    }

    [Fact]
    public void RecalculateAllReproducesTheSameValues()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "5");
        workbook.SetCellContent("Sheet1", "A2", "=A1*2");
        workbook.SetCellContent("Sheet1", "A3", "=A2+A1");

        var result = workbook.RecalculateAll();

        Assert.Equal(2, result.CellsEvaluated);   // A1 holds a literal; only A2 and A3 are formulas
        Assert.Empty(result.Changes);             // nothing was stale
        Assert.Equal(15, workbook.GetCellValue("Sheet1", "A3").AsNumber);
    }

    [Fact]
    public void ErrorsFlowDownstream()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "0");
        workbook.SetCellContent("Sheet1", "B1", "=10/A1");
        workbook.SetCellContent("Sheet1", "C1", "=B1+1");

        Assert.Equal(ErrorKind.DivideByZero, workbook.GetCellValue("Sheet1", "C1").AsError.Kind);

        workbook.SetCellContent("Sheet1", "A1", "2");

        Assert.Equal(6, workbook.GetCellValue("Sheet1", "C1").AsNumber);
    }

    [Fact]
    public void ObserversAreNotifiedOfEveryChange()
    {
        var workbook = new Workbook();
        var seen = new List<string>();
        using var subscription = workbook.Subscribe(new RecordingObserver(seen));

        workbook.SetCellContent("Sheet1", "A1", "1");
        workbook.SetCellContent("Sheet1", "B1", "=A1+1");
        seen.Clear();

        workbook.SetCellContent("Sheet1", "A1", "5");

        Assert.Equal(new[] { "Sheet1!A1", "Sheet1!B1" }, seen.Order());
    }

    [Fact]
    public void ObserversStopBeingNotifiedAfterDisposal()
    {
        var workbook = new Workbook();
        var seen = new List<string>();
        var subscription = workbook.Subscribe(new RecordingObserver(seen));
        subscription.Dispose();

        workbook.SetCellContent("Sheet1", "A1", "1");

        Assert.Empty(seen);
    }

    [Fact]
    public void TheEventAndTheObserverInterfaceSeeTheSameChanges()
    {
        var workbook = new Workbook();
        var fromEvent = new List<string>();
        workbook.CellsChanged += (_, e) =>
            fromEvent.AddRange(e.Result.Changes.Select(c => c.Address.ToA1()));

        workbook.SetCellContent("Sheet1", "A1", "1");

        Assert.Equal(new[] { "Sheet1!A1" }, fromEvent);
    }

    [Fact]
    public void ChangesCarryTheOldAndNewValue()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "1");

        var result = workbook.SetCellContent("Sheet1", "A1", "2");
        var change = Assert.Single(result.Changes);

        Assert.Equal(CellValue.Number(1), change.OldValue);
        Assert.Equal(CellValue.Number(2), change.NewValue);
    }

    [Fact]
    public void ManualCalculationModeDefersRecalculation()
    {
        var workbook = new Workbook(new WorkbookOptions { AutomaticCalculation = false });
        workbook.SetCellContent("Sheet1", "A1", "1");
        workbook.SetCellContent("Sheet1", "B1", "=A1+1");

        workbook.SetCellContent("Sheet1", "A1", "10");
        Assert.Equal(2, workbook.GetCellValue("Sheet1", "B1").AsNumber);   // still stale

        var result = workbook.CalculateNow();

        Assert.Equal(11, workbook.GetCellValue("Sheet1", "B1").AsNumber);
        Assert.NotEmpty(result.Changes);
    }

    private sealed class RecordingObserver(List<string> seen) : ICellChangeObserver
    {
        public void OnCellsChanged(CalculationResult result)
        {
            foreach (var change in result.Changes)
            {
                seen.Add(change.Address.ToA1());
            }
        }
    }
}
