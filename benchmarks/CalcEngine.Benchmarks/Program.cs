using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CalcEngine.Benchmarks;

// ---------------------------------------------------------------------------
// CalcEngine performance harness.
//
//   dotnet run -c Release --project benchmarks/CalcEngine.Benchmarks
//   dotnet run -c Release --project benchmarks/CalcEngine.Benchmarks -- --json results.json
//
// Release matters: a Debug build measures the absence of the optimiser.
// ---------------------------------------------------------------------------

var jsonPath = ArgumentAfter(args, "--json");
var quick = args.Contains("--quick", StringComparer.OrdinalIgnoreCase);

Console.WriteLine("CalcEngine benchmark harness");
Console.WriteLine($"  runtime      : .NET {Environment.Version}");
Console.WriteLine($"  processors   : {Environment.ProcessorCount}");
Console.WriteLine($"  configuration: {(IsDebug() ? "DEBUG (measurements are not meaningful)" : "RELEASE")}");
Console.WriteLine();

var results = new List<BenchmarkResult>();
var harness = new WorkbookBenchmarks(quick);

results.Add(harness.MeasureLoad());
results.Add(harness.MeasureFullRecalculation());
results.Add(harness.MeasureSingleEditThroughFiveHundredDependents());
results.Add(harness.MeasureEditWithNoDependents());
results.Add(harness.MeasureRangeAggregateEdit());
results.Add(harness.MeasureFindAll());
results.Add(harness.MeasureReplaceAll());
results.Add(harness.MeasureDuplicateDetection());
results.Add(harness.MeasureUndoRedo());

Console.WriteLine();
Console.WriteLine(BenchmarkResult.MarkdownTable(results));

var failures = results.Count(r => !r.Passed);
Console.WriteLine();
Console.WriteLine(failures == 0
    ? "All targets met."
    : $"{failures} target(s) MISSED.");

if (jsonPath is not null)
{
    File.WriteAllText(
        jsonPath,
        JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Wrote {jsonPath}");
}

return failures == 0 ? 0 : 1;

static string? ArgumentAfter(string[] args, string flag)
{
    var at = Array.FindIndex(args, a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
    return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
}

static bool IsDebug()
{
#if DEBUG
    return true;
#else
    return false;
#endif
}

namespace CalcEngine.Benchmarks
{
    /// <summary>One measured operation and whether it met its published target.</summary>
    public sealed record BenchmarkResult(
        string Name,
        string Target,
        int Iterations,
        double MinMs,
        double MedianMs,
        double P95Ms,
        double MaxMs,
        double? BudgetMs)
    {
        /// <summary>
        /// A target is met when the <em>worst</em> observed run is inside the
        /// budget. Reporting a median that fits while the tail does not would be
        /// a way of not answering the question.
        /// </summary>
        public bool Passed => BudgetMs is null || MaxMs <= BudgetMs;

        public static BenchmarkResult From(string name, string target, double? budgetMs, IReadOnlyList<double> samples)
        {
            var sorted = samples.Order().ToList();
            return new BenchmarkResult(
                name,
                target,
                sorted.Count,
                sorted[0],
                sorted[sorted.Count / 2],
                sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.95))],
                sorted[^1],
                budgetMs);
        }

        public static string MarkdownTable(IReadOnlyList<BenchmarkResult> results)
        {
            var table = new StringBuilder();
            table.AppendLine("| Benchmark | Target | Runs | Min | Median | p95 | Max | Verdict |");
            table.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | :---: |");
            foreach (var r in results)
            {
                table.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "| {0} | {1} | {2} | {3:F2} ms | {4:F2} ms | {5:F2} ms | {6:F2} ms | {7} |",
                    r.Name, r.Target, r.Iterations, r.MinMs, r.MedianMs, r.P95Ms, r.MaxMs,
                    r.BudgetMs is null ? "—" : r.Passed ? "PASS" : "**FAIL**"));
            }

            return table.ToString();
        }
    }

    internal static class Timing
    {
        internal static IReadOnlyList<double> Measure(int warmups, int iterations, Action<int> action)
        {
            for (var i = 0; i < warmups; i++)
            {
                action(i);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var samples = new List<double>(iterations);
            for (var i = 0; i < iterations; i++)
            {
                var start = Stopwatch.GetTimestamp();
                action(i);
                samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }

            return samples;
        }

        internal static double Once(Action action)
        {
            var start = Stopwatch.GetTimestamp();
            action();
            return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
    }
}
