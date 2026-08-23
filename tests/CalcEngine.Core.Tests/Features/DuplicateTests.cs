using CalcEngine.Core;
using CalcEngine.Core.Features.Duplicates;
using CalcEngine.Core.Model;

namespace CalcEngine.Core.Tests.Features;

/// <summary>
/// Group E, assigned feature 2: duplicate detection and removal.
/// </summary>
public class DuplicateTests
{
    /// <summary>
    /// A registration sheet with the three kinds of repetition that occur in
    /// real result processing: an exact duplicated row, the same student
    /// entered twice with different marks, and a mark that merely happens to
    /// repeat.
    /// </summary>
    private static Workbook Registrations()
    {
        var workbook = new Workbook();
        var rows = new[]
        {
            ("MatNo", "Name", "Mark"),
            ("CSC/17/001", "Ngozi", "72"),
            ("CSC/17/002", "Bola", "55"),
            ("CSC/17/001", "Ngozi", "72"),
            ("CSC/17/003", "Emeka", "41"),
            ("CSC/17/002", "Bola", "60"),
        };

        for (var i = 0; i < rows.Length; i++)
        {
            workbook.SetCellContent("Sheet1", $"A{i + 1}", rows[i].Item1);
            workbook.SetCellContent("Sheet1", $"B{i + 1}", rows[i].Item2);
            workbook.SetCellContent("Sheet1", $"C{i + 1}", rows[i].Item3);
        }

        return workbook;
    }

    private static DuplicateOptions AllRows(bool header = true) => new()
    {
        Range = CellRange.Parse("A1:C6"),
        HasHeaderRow = header,
    };

    // ----------------------------------------------------------- detection

    [Fact]
    public void WholeRowDuplicatesAreFound()
    {
        var report = new DuplicateService(Registrations()).Find(AllRows());

        var group = Assert.Single(report.Groups);
        Assert.Equal(2, group.Count);
        Assert.Equal(new[] { 2, 4 }, group.Occurrences.Select(o => o.Row));
        Assert.Equal(1, report.DuplicateCount);
        Assert.Equal(4, report.DistinctCount);
    }

    [Fact]
    public void TheFirstOccurrenceIsTheOneThatIsKept()
    {
        var report = new DuplicateService(Registrations()).Find(AllRows());
        var group = Assert.Single(report.Groups);

        Assert.Equal(2, group.First.Row);
        Assert.Equal(new[] { 4 }, group.Repeats.Select(o => o.Row));
    }

    [Fact]
    public void KeyColumnsNarrowWhatCountsAsTheSameRecord()
    {
        // On matriculation number alone, both students appear twice.
        var report = new DuplicateService(Registrations()).Find(new DuplicateOptions
        {
            Range = CellRange.Parse("A1:C6"),
            HasHeaderRow = true,
            KeyColumnOffsets = [0],
        });

        Assert.Equal(2, report.Groups.Count);
        Assert.Equal(2, report.DuplicateCount);
    }

    [Fact]
    public void AHeaderRowIsExcludedFromComparison()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "Mark");
        workbook.SetCellContent("Sheet1", "A2", "Mark");
        workbook.SetCellContent("Sheet1", "A3", "Mark");

        var withHeader = new DuplicateService(workbook).Find(new DuplicateOptions
        {
            Range = CellRange.Parse("A1:A3"),
            HasHeaderRow = true,
        });
        var withoutHeader = new DuplicateService(workbook).Find(new DuplicateOptions
        {
            Range = CellRange.Parse("A1:A3"),
        });

        Assert.Equal(1, withHeader.DuplicateCount);
        Assert.Equal(2, withoutHeader.DuplicateCount);
    }

    [Fact]
    public void ComparisonIsCaseInsensitiveByDefault()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "csc322");
        workbook.SetCellContent("Sheet1", "A2", "CSC322");

        var options = new DuplicateOptions { Range = CellRange.Parse("A1:A2") };

        Assert.Equal(1, new DuplicateService(workbook).Find(options).DuplicateCount);
        Assert.Equal(0, new DuplicateService(workbook)
            .Find(new DuplicateOptions { Range = CellRange.Parse("A1:A2"), MatchCase = true })
            .DuplicateCount);
    }

    [Fact]
    public void SurroundingWhitespaceIsIgnoredByDefault()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "Ngozi ");
        workbook.SetCellContent("Sheet1", "A2", " Ngozi");

        Assert.Equal(1, new DuplicateService(workbook)
            .Find(new DuplicateOptions { Range = CellRange.Parse("A1:A2") })
            .DuplicateCount);

        Assert.Equal(0, new DuplicateService(workbook)
            .Find(new DuplicateOptions { Range = CellRange.Parse("A1:A2"), TrimWhitespace = false })
            .DuplicateCount);
    }

    [Fact]
    public void ANumberIsNotTheSameAsTheTextThatLooksLikeIt()
    {
        // The mistake that makes a duplicate finder untrustworthy: 5 and "5"
        // must not collide, or an imported column of text marks appears to
        // duplicate a typed one.
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "5");
        workbook.SetCellContent("Sheet1", "A2", "=\"5\"");

        Assert.Equal(0, new DuplicateService(workbook)
            .Find(new DuplicateOptions { Range = CellRange.Parse("A1:A2") })
            .DuplicateCount);
    }

    [Fact]
    public void FormulasAreComparedByTheirValueNotTheirText()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "10");
        workbook.SetCellContent("Sheet1", "A2", "=5+5");

        Assert.Equal(1, new DuplicateService(workbook)
            .Find(new DuplicateOptions { Range = CellRange.Parse("A1:A2") })
            .DuplicateCount);
    }

    [Fact]
    public void EmptyRecordsAreIgnoredUnlessAskedFor()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "x");
        workbook.SetCellContent("Sheet1", "A5", "x");

        var ignored = new DuplicateService(workbook).Find(new DuplicateOptions
        {
            Range = CellRange.Parse("A1:A6"),
        });
        var included = new DuplicateService(workbook).Find(new DuplicateOptions
        {
            Range = CellRange.Parse("A1:A6"),
            IncludeBlankRecords = true,
        });

        Assert.Equal(1, ignored.DuplicateCount);              // just the two x's
        Assert.Equal(4, included.DuplicateCount);             // and the four blanks collapse too
    }

    [Fact]
    public void CellScopeFindsRepeatedValuesAnywhereInTheRange()
    {
        var workbook = Registrations();

        var report = new DuplicateService(workbook).Find(new DuplicateOptions
        {
            Range = CellRange.Parse("A2:C6"),
            Scope = DuplicateScope.Cells,
        });

        // CSC/17/001, CSC/17/002, Ngozi, Bola and 72 each appear twice.
        Assert.Equal(5, report.Groups.Count);
        Assert.Equal(5, report.DuplicateCount);
    }

    [Fact]
    public void ColumnScopeComparesWholeColumns()
    {
        var workbook = new Workbook();
        foreach (var (column, values) in new (string, int[])[]
                 {
                     ("A", [1, 2, 3]), ("B", [9, 9, 9]), ("C", [1, 2, 3]),
                 })
        {
            for (var row = 1; row <= 3; row++)
            {
                workbook.SetCellContent("Sheet1", $"{column}{row}", values[row - 1].ToString());
            }
        }

        var report = new DuplicateService(workbook).Find(new DuplicateOptions
        {
            Range = CellRange.Parse("A1:C3"),
            Scope = DuplicateScope.Columns,
        });

        var group = Assert.Single(report.Groups);
        Assert.Equal(new[] { 1, 3 }, group.Occurrences.Select(o => o.Column));
    }

    [Fact]
    public void TheReportCanAnswerWhetherOneCellIsADuplicate()
    {
        // What the grid needs in order to shade the offending rows.
        var report = new DuplicateService(Registrations()).Find(AllRows());

        Assert.True(report.IsDuplicate(new SheetCellAddress("Sheet1", CellAddress.Parse("A4"))));
        Assert.True(report.IsDuplicate(new SheetCellAddress("Sheet1", CellAddress.Parse("C4"))));
        Assert.False(report.IsDuplicate(new SheetCellAddress("Sheet1", CellAddress.Parse("A2"))));
        Assert.False(report.IsDuplicate(new SheetCellAddress("Sheet1", CellAddress.Parse("A5"))));
    }

    [Fact]
    public void ARangeWithNoRepetitionReportsNothing()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "1");
        workbook.SetCellContent("Sheet1", "A2", "2");

        var report = new DuplicateService(workbook).Find(new DuplicateOptions
        {
            Range = CellRange.Parse("A1:A2"),
        });

        Assert.Empty(report.Groups);
        Assert.Equal(0, report.DuplicateCount);
        Assert.Equal(2, report.DistinctCount);
    }

    [Fact]
    public void AKeyColumnOutsideTheRangeIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DuplicateService(Registrations()).Find(
            new DuplicateOptions { Range = CellRange.Parse("A1:C6"), KeyColumnOffsets = [7] }));
    }

    // ------------------------------------------------------------- removal

    [Fact]
    public void RemoveClearsTheDuplicateRowsByDefault()
    {
        var workbook = Registrations();

        var result = new DuplicateService(workbook).Remove(AllRows());

        Assert.Equal(1, result.RecordsRemoved);
        Assert.Null(workbook.GetCellContent("Sheet1", "A4"));
        Assert.Null(workbook.GetCellContent("Sheet1", "C4"));
        Assert.Equal("CSC/17/001", workbook.GetCellContent("Sheet1", "A2"));
        Assert.Equal("CSC/17/003", workbook.GetCellContent("Sheet1", "A5"));
    }

    [Fact]
    public void ShiftUpCompactsTheSurvivingRecords()
    {
        var workbook = Registrations();

        var result = new DuplicateService(workbook).Remove(AllRows(), DuplicateRemoval.ShiftUp);

        Assert.Equal(1, result.RecordsRemoved);
        Assert.Equal(new[] { "CSC/17/001", "CSC/17/002", "CSC/17/003", "CSC/17/002" },
            new[] { "A2", "A3", "A4", "A5" }.Select(a => workbook.GetCellContent("Sheet1", a)));
        Assert.Null(workbook.GetCellContent("Sheet1", "A6"));
    }

    [Fact]
    public void MarkOnlyChangesNothing()
    {
        var workbook = Registrations();

        var result = new DuplicateService(workbook).Remove(AllRows(), DuplicateRemoval.MarkOnly);

        Assert.Equal(0, result.RecordsRemoved);
        Assert.Equal(1, result.Report.DuplicateCount);
        Assert.Equal("CSC/17/001", workbook.GetCellContent("Sheet1", "A4"));
    }

    [Fact]
    public void RemovalIsASingleUndoableOperation()
    {
        var workbook = Registrations();
        var before = workbook.History.UndoCount;

        new DuplicateService(workbook).Remove(AllRows());

        Assert.Equal(before + 1, workbook.History.UndoCount);
        Assert.Contains("duplicate", workbook.History.NextUndoDescription!, StringComparison.OrdinalIgnoreCase);

        workbook.History.Undo();

        Assert.Equal("CSC/17/001", workbook.GetCellContent("Sheet1", "A4"));
        Assert.Equal("72", workbook.GetCellContent("Sheet1", "C4"));
    }

    [Fact]
    public void RemovalRecalculatesFormulasThatReadTheRange()
    {
        var workbook = Registrations();
        workbook.SetCellContent("Sheet1", "E1", "=COUNTA(A2:A6)");
        Assert.Equal(5, workbook.GetCellValue("Sheet1", "E1").AsNumber);

        new DuplicateService(workbook).Remove(AllRows());

        Assert.Equal(4, workbook.GetCellValue("Sheet1", "E1").AsNumber);
    }

    [Fact]
    public void ShiftUpRefusesToMoveFormulasUnlessTheCallerInsists()
    {
        var workbook = Registrations();
        workbook.SetCellContent("Sheet1", "C5", "=B5&\" mark\"");

        var refused = new DuplicateService(workbook).Remove(AllRows(), DuplicateRemoval.ShiftUp);

        Assert.True(refused.Refused);
        Assert.Equal(0, refused.RecordsRemoved);
        Assert.Single(refused.FormulasThatWouldMove);
        Assert.Equal("=B5&\" mark\"", workbook.GetCellContent("Sheet1", "C5"));
        Assert.Contains("relative references", refused.RefusalReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShiftUpMovesFormulasVerbatimWhenAllowed()
    {
        var workbook = Registrations();
        workbook.SetCellContent("Sheet1", "C5", "=B5&\" mark\"");

        var result = new DuplicateService(workbook).Remove(
            new DuplicateOptions
            {
                Range = CellRange.Parse("A1:C6"),
                HasHeaderRow = true,
                AllowMovingFormulas = true,
            },
            DuplicateRemoval.ShiftUp);

        Assert.False(result.Refused);
        // The formula moved from C5 to C4 but still reads B5: content moves,
        // references do not.  Documented, and reported in FormulasThatWouldMove.
        Assert.Equal("=B5&\" mark\"", workbook.GetCellContent("Sheet1", "C4"));
        Assert.Single(result.FormulasThatWouldMove);
    }

    [Fact]
    public void ClearContentsNeverRefusesBecauseNothingMoves()
    {
        var workbook = Registrations();
        workbook.SetCellContent("Sheet1", "C5", "=B5&\" mark\"");

        var result = new DuplicateService(workbook).Remove(AllRows());

        Assert.False(result.Refused);
        Assert.Equal("=B5&\" mark\"", workbook.GetCellContent("Sheet1", "C5"));
    }

    [Fact]
    public void RemovingNothingIsNotAnOperation()
    {
        var workbook = new Workbook();
        workbook.SetCellContent("Sheet1", "A1", "1");
        workbook.SetCellContent("Sheet1", "A2", "2");
        var before = workbook.History.UndoCount;

        var result = new DuplicateService(workbook).Remove(new DuplicateOptions
        {
            Range = CellRange.Parse("A1:A2"),
        });

        Assert.Equal(0, result.RecordsRemoved);
        Assert.Equal(before, workbook.History.UndoCount);
    }

    [Fact]
    public void RemovalKeepsOneOfEachDistinctRecord()
    {
        var workbook = new Workbook();
        for (var row = 1; row <= 9; row++)
        {
            workbook.SetCellContent("Sheet1", $"A{row}", (row % 3).ToString());
        }

        var result = new DuplicateService(workbook).Remove(
            new DuplicateOptions { Range = CellRange.Parse("A1:A9") },
            DuplicateRemoval.ShiftUp);

        Assert.Equal(6, result.RecordsRemoved);
        Assert.Equal(3, result.RecordsRemaining);
        Assert.Equal(new[] { "1", "2", "0" }, new[] { "A1", "A2", "A3" }.Select(a => workbook.GetCellContent("Sheet1", a)));
        Assert.Null(workbook.GetCellContent("Sheet1", "A4"));
    }
}
