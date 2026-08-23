using CalcEngine.Core;
using CalcEngine.Core.Commands;

namespace CalcEngine.Core.Tests.Commands;

/// <summary>
/// "Undo and redo of at least the last 100 editing operations."
/// </summary>
public class UndoRedoTests
{
    [Fact]
    public void AFreshWorkbookHasNothingToUndo()
    {
        var workbook = new Workbook();

        Assert.False(workbook.History.CanUndo);
        Assert.False(workbook.History.CanRedo);
        Assert.Equal(0, workbook.History.UndoCount);
    }

    [Fact]
    public void UndoRestoresThePreviousContent()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "10");
        workbook.SetCellContent("Sheet1", "A1", "20");

        workbook.History.Undo();

        Assert.Equal("10", workbook.GetCellContent("Sheet1", "A1"));
        Assert.Equal(10, workbook.GetCellValue("Sheet1", "A1").AsNumber);
    }

    [Fact]
    public void UndoOfTheFirstEditLeavesTheCellEmpty()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "10");

        workbook.History.Undo();

        Assert.Null(workbook.GetCellContent("Sheet1", "A1"));
        Assert.True(workbook.GetCellValue("Sheet1", "A1").IsBlank);
    }

    [Fact]
    public void RedoReappliesTheUndoneEdit()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "10");
        workbook.SetCellContent("Sheet1", "A1", "20");
        workbook.History.Undo();

        workbook.History.Redo();

        Assert.Equal("20", workbook.GetCellContent("Sheet1", "A1"));
    }

    [Fact]
    public void UndoRecalculatesDependents()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "10");
        workbook.SetCellContent("Sheet1", "B1", "=A1*2");
        workbook.SetCellContent("Sheet1", "A1", "50");

        var result = workbook.History.Undo();

        Assert.Equal(20, workbook.GetCellValue("Sheet1", "B1").AsNumber);
        Assert.Contains(result.Changes, c => c.Address.Address.ToA1() == "B1");
    }

    [Fact]
    public void UndoOfAFormulaEditRestoresItsDependencies()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "1");
        workbook.SetCellContent("Sheet1", "B1", "2");
        workbook.SetCellContent("Sheet1", "C1", "=A1");
        workbook.SetCellContent("Sheet1", "C1", "=B1");

        workbook.History.Undo();

        // C1 must depend on A1 again, not merely display A1's old value.
        workbook.SetCellContent("Sheet1", "A1", "99");

        Assert.Equal(99, workbook.GetCellValue("Sheet1", "C1").AsNumber);
    }

    [Fact]
    public void UndoOfACycleClearsTheErrorEverywhere()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "=B1+1");
        workbook.SetCellContent("Sheet1", "B1", "5");
        workbook.SetCellContent("Sheet1", "B1", "=A1+1");

        Assert.True(workbook.GetCellValue("Sheet1", "A1").IsError);

        workbook.History.Undo();

        Assert.Equal(6, workbook.GetCellValue("Sheet1", "A1").AsNumber);
        Assert.Equal(5, workbook.GetCellValue("Sheet1", "B1").AsNumber);
    }

    [Fact]
    public void AtLeastOneHundredOperationsCanBeUndone()
    {
        var workbook = new Workbook();
        for (var i = 1; i <= 100; i++)
        {
            workbook.SetCellContent("Sheet1", "A1", i.ToString());
        }

        Assert.Equal(100, workbook.History.UndoCount);

        for (var i = 0; i < 100; i++)
        {
            workbook.History.Undo();
        }

        Assert.False(workbook.History.CanUndo);
        Assert.Null(workbook.GetCellContent("Sheet1", "A1"));
    }

    [Fact]
    public void TheOldestOperationIsDiscardedOnceTheLimitIsReached()
    {
        var workbook = new Workbook(new WorkbookOptions { HistoryLimit = 100 });
        for (var i = 1; i <= 150; i++)
        {
            workbook.SetCellContent("Sheet1", "A1", i.ToString());
        }

        Assert.Equal(100, workbook.History.UndoCount);

        while (workbook.History.CanUndo)
        {
            workbook.History.Undo();
        }

        // The first 50 edits fell off the back, so A1 rewinds to edit 50, not to empty.
        Assert.Equal("50", workbook.GetCellContent("Sheet1", "A1"));
    }

    [Fact]
    public void ANewEditDiscardsTheRedoStack()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "1");
        workbook.SetCellContent("Sheet1", "A1", "2");
        workbook.History.Undo();
        Assert.True(workbook.History.CanRedo);

        workbook.SetCellContent("Sheet1", "A1", "3");

        Assert.False(workbook.History.CanRedo);
    }

    [Fact]
    public void UndoAndRedoAreNoOpsWhenThereIsNothingToDo()
    {
        var workbook = new Workbook();

        Assert.Empty(workbook.History.Undo().Changes);
        Assert.Empty(workbook.History.Redo().Changes);
    }

    [Fact]
    public void ABatchOfEditsIsOneUndoableOperation()
    {
        var workbook = new Workbook();
        workbook.SetCellContents(
            [
                new CellEdit(new Model.SheetCellAddress("Sheet1", Model.CellAddress.Parse("A1")), "1"),
                new CellEdit(new Model.SheetCellAddress("Sheet1", Model.CellAddress.Parse("A2")), "2"),
                new CellEdit(new Model.SheetCellAddress("Sheet1", Model.CellAddress.Parse("A3")), "3"),
            ],
            "fill A1:A3");

        Assert.Equal(1, workbook.History.UndoCount);
        Assert.Equal("fill A1:A3", workbook.History.NextUndoDescription);

        workbook.History.Undo();

        Assert.Null(workbook.GetCellContent("Sheet1", "A1"));
        Assert.Null(workbook.GetCellContent("Sheet1", "A3"));
    }

    [Fact]
    public void OperationsCarryADescriptionForTheUserInterface()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "B2", "=SUM(A1:A9)");

        Assert.Equal("edit Sheet1!B2", workbook.History.NextUndoDescription);

        workbook.History.Undo();

        Assert.Equal("edit Sheet1!B2", workbook.History.NextRedoDescription);
        Assert.Null(workbook.History.NextUndoDescription);
    }

    [Fact]
    public void ACompositeCommandUndoesAsAWhole()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "1");
        workbook.History.Clear();

        var composite = new CompositeCommand(
            "tidy up",
            [
                SetCellsCommand.ForSingleCell(new Model.SheetCellAddress("Sheet1", Model.CellAddress.Parse("A1")), "2"),
                SetCellsCommand.ForSingleCell(new Model.SheetCellAddress("Sheet1", Model.CellAddress.Parse("B1")), "3"),
            ]);

        workbook.History.Execute(composite);

        Assert.Equal(1, workbook.History.UndoCount);
        Assert.Equal("2", workbook.GetCellContent("Sheet1", "A1"));

        workbook.History.Undo();

        Assert.Equal("1", workbook.GetCellContent("Sheet1", "A1"));
        Assert.Null(workbook.GetCellContent("Sheet1", "B1"));
    }

    [Fact]
    public void ClearEmptiesBothStacks()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "1");
        workbook.History.Undo();

        workbook.History.Clear();

        Assert.False(workbook.History.CanUndo);
        Assert.False(workbook.History.CanRedo);
    }

    [Fact]
    public void AnEditThatChangesNothingIsNotRecorded()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "1");
        var before = workbook.History.UndoCount;

        workbook.SetCellContent("Sheet1", "A1", "1");

        Assert.Equal(before, workbook.History.UndoCount);
    }

    [Fact]
    public void HistoryCanBeSuspendedForBulkLoading()
    {
        var workbook = new Workbook();

        using (workbook.History.Suspend())
        {
            for (var row = 1; row <= 500; row++)
            {
                workbook.SetCellContent("Sheet1", $"A{row}", row.ToString());
            }
        }

        Assert.False(workbook.History.CanUndo);

        workbook.SetCellContent("Sheet1", "B1", "x");

        Assert.Equal(1, workbook.History.UndoCount);
    }
}
