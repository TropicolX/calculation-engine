using CalcEngine.Core;
using CalcEngine.Core.Features.Duplicates;
using CalcEngine.Core.Features.FindReplace;
using CalcEngine.Core.Model;

namespace CalcEngine.Benchmarks;

/// <summary>
/// The benchmark workbook and the measurements taken over it.
/// </summary>
/// <remarks>
/// <para>The brief fixes the shape: <em>"a single cell edit in a workbook of
/// 100,000 cells with a chain of 500 dependent cells must propagate within 50
/// milliseconds; a full recalculation of the same workbook must complete within
/// 2 seconds."</em> The workbook below is exactly 100,000 cells and contains
/// exactly such a chain, so the numbers answer the question that was asked
/// rather than an easier one.</para>
///
/// <list type="bullet">
///   <item><description><b>Data</b> — 10,000 rows x 8 columns of numeric
///   literals: 80,000 cells.</description></item>
///   <item><description><b>Calc</b> — 9,750 rows x 2 formula columns: a row
///   total <c>=SUM(Data!A_r:H_r)</c> and a weighting
///   <c>=ROUND(A_r*0.3,2)</c>: 19,500 cells.</description></item>
///   <item><description><b>Chain</b> — 500 cells, each reading the one above
///   it, rooted at <c>Data!A1</c>: 500 cells.</description></item>
/// </list>
///
/// <para>Editing <c>Data!A1</c> therefore touches the 500-cell chain plus the
/// two Calc cells for row 1, and nothing else — which is the property the
/// dependency graph exists to provide.</para>
/// </remarks>
internal sealed class WorkbookBenchmarks
{
    private const int DataRows = 10_000;
    private const int DataColumns = 8;
    private const int CalcRows = 9_750;
    private const int ChainLength = 500;

    private readonly int _iterations;
    private readonly Workbook _workbook;
    private double _loadMilliseconds;

    internal WorkbookBenchmarks(bool quick)
    {
        _iterations = quick ? 5 : 30;
        _workbook = Build(out _loadMilliseconds);

        var cells = _workbook.TrackedCellCount;
        Console.WriteLine($"  workbook     : {cells:N0} tracked cells, chain of {ChainLength}");
    }

    internal BenchmarkResult MeasureLoad() =>
        BenchmarkResult.From(
            $"Load {Total():N0} cells (parse + first evaluation)",
            "no published target",
            null,
            [_loadMilliseconds]);

    internal BenchmarkResult MeasureFullRecalculation()
    {
        var samples = Timing.Measure(2, Math.Max(3, _iterations / 6), _ => _workbook.RecalculateAll());
        return BenchmarkResult.From(
            $"Full recalculation of {Total():N0} cells",
            "< 2,000 ms",
            2_000,
            samples);
    }

    internal BenchmarkResult MeasureSingleEditThroughFiveHundredDependents()
    {
        var samples = Timing.Measure(5, _iterations, i =>
        {
            // Offset well clear of the seeded values: writing the content a cell
            // already holds is correctly a no-op, and would measure nothing.
            var result = _workbook.SetCellContent("Data", "A1", (1_000 + (i % 977)).ToString());
            if (result.CellsEvaluated < ChainLength)
            {
                throw new InvalidOperationException(
                    $"expected at least {ChainLength} dependents to recompute, got {result.CellsEvaluated}");
            }
        });

        return BenchmarkResult.From(
            $"Single edit propagating through {ChainLength} dependents",
            "< 50 ms",
            50,
            samples);
    }

    internal BenchmarkResult MeasureEditWithNoDependents()
    {
        // The case the graph should make almost free: a cell nobody reads.
        var samples = Timing.Measure(5, _iterations, i =>
            _workbook.SetCellContent("Data", $"H{DataRows}", (1_000 + (i % 977)).ToString()));

        return BenchmarkResult.From(
            "Single edit with no dependents",
            "no published target",
            null,
            samples);
    }

    internal BenchmarkResult MeasureRangeAggregateEdit()
    {
        // Writing into a cell covered by a range formula: the path through the
        // range index rather than through an adjacency list.
        var samples = Timing.Measure(5, _iterations, i =>
        {
            var result = _workbook.SetCellContent("Data", "C500", (1_000 + (i % 97)).ToString());
            if (result.CellsEvaluated == 0)
            {
                throw new InvalidOperationException("the range formula did not recompute");
            }
        });

        return BenchmarkResult.From(
            "Edit inside a SUM range (range index lookup)",
            "no published target",
            null,
            samples);
    }

    internal BenchmarkResult MeasureFindAll()
    {
        var service = new FindReplaceService(_workbook);
        var options = new FindOptions { SearchText = "ROUND", SheetName = "Calc" };
        var samples = Timing.Measure(2, Math.Max(3, _iterations / 3), _ =>
        {
            if (service.FindAll(options).Count != CalcRows)
            {
                throw new InvalidOperationException("unexpected match count");
            }
        });

        return BenchmarkResult.From(
            $"Find across {CalcRows * 2:N0} formula cells",
            "no published target",
            null,
            samples);
    }

    internal BenchmarkResult MeasureReplaceAll()
    {
        var service = new FindReplaceService(_workbook);
        var forward = new FindOptions { SearchText = "ROUND(", SheetName = "Calc" };
        var back = new FindOptions { SearchText = "ROUNDDOWN(", SheetName = "Calc" };

        var samples = Timing.Measure(1, Math.Max(3, _iterations / 6), _ =>
        {
            service.ReplaceAll(forward, "ROUNDDOWN(");
            service.ReplaceAll(back, "ROUND(");
        });

        return BenchmarkResult.From(
            $"Replace across {CalcRows:N0} formulas, twice, with re-parse validation",
            "no published target",
            null,
            samples);
    }

    internal BenchmarkResult MeasureDuplicateDetection()
    {
        var service = new DuplicateService(_workbook);
        var options = new DuplicateOptions
        {
            SheetName = "Data",
            Range = new CellRange(CellAddress.Parse("A1"), new CellAddress(DataColumns, DataRows)),
        };

        var samples = Timing.Measure(2, Math.Max(3, _iterations / 3), _ => service.Find(options));

        return BenchmarkResult.From(
            $"Duplicate scan of {DataRows:N0} rows x {DataColumns} columns",
            "no published target",
            null,
            samples);
    }

    internal BenchmarkResult MeasureUndoRedo()
    {
        var samples = Timing.Measure(5, _iterations, i =>
        {
            _workbook.SetCellContent("Data", "A1", (2_000 + (i % 977)).ToString());
            _workbook.History.Undo();
            _workbook.History.Redo();
        });

        return BenchmarkResult.From(
            "Edit, undo and redo through the 500-cell chain",
            "no published target",
            null,
            samples);
    }

    private static long Total() => ((long)DataRows * DataColumns) + (CalcRows * 2L) + ChainLength;

    /// <summary>
    /// Builds the benchmark workbook in one batch with calculation switched off,
    /// which is how a bulk import should drive the engine, then settles it with
    /// a single full recalculation.
    /// </summary>
    private static Workbook Build(out double loadMilliseconds)
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
            edits.Add(new CellEdit(
                new SheetCellAddress("Chain", new CellAddress(1, row)),
                $"=A{row - 1}+1"));
        }

        var elapsed = Timing.Once(() =>
        {
            using (workbook.History.Suspend())
            {
                workbook.SetCellContents(edits, "load benchmark workbook");
            }

            workbook.RecalculateAll();
        });

        workbook.AutomaticCalculation = true;
        loadMilliseconds = elapsed;
        return workbook;
    }
}
