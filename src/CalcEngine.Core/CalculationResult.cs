using System.Diagnostics;
using CalcEngine.Core.Model;
using CalcEngine.Core.Parsing;

namespace CalcEngine.Core;

/// <summary>One cell whose value changed, with what it was and what it became.</summary>
/// <param name="Address">Which cell.</param>
/// <param name="OldValue">The value before the recalculation.</param>
/// <param name="NewValue">The value after it.</param>
public readonly record struct CellChange(SheetCellAddress Address, CellValue OldValue, CellValue NewValue)
{
    /// <inheritdoc />
    public override string ToString() => $"{Address.ToA1()}: {OldValue} -> {NewValue}";
}

/// <summary>
/// A dependency cycle, reported as the exact path round the ring.
/// </summary>
/// <remarks>
/// <see cref="Path"/> is in <em>reference</em> order: each cell's formula reads
/// the next, and the last reads the first. The path starts at the smallest
/// address in the ring, so the same broken workbook always reports the same
/// sentence no matter which cell the user happened to type last.
/// </remarks>
public sealed class CircularReference
{
    internal CircularReference(IReadOnlyList<SheetCellAddress> path) => Path = path;

    /// <summary>
    /// How many cells a rendered cycle shows in full before it abbreviates.
    /// </summary>
    /// <remarks>
    /// A ring of twenty thousand cells is a real import artefact, and neither a
    /// user nor a log wants two hundred kilobytes of it. <see cref="Path"/>
    /// always holds every cell for programmatic use; only the rendering elides.
    /// </remarks>
    public const int MaximumRenderedCells = 12;

    /// <summary>The cells in the ring, each referring to the next.</summary>
    public IReadOnlyList<SheetCellAddress> Path { get; }

    /// <summary>
    /// True when the ring spans more than one sheet, in which case every cell is
    /// rendered with its sheet name.
    /// </summary>
    public bool SpansSheets =>
        Path.Any(a => !string.Equals(a.Sheet, Path[0].Sheet, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Renders the ring as <c>A1 → B3 → C7 → A1</c>, qualifying with sheet
    /// names only when the ring actually spans sheets.
    /// </summary>
    public override string ToString()
    {
        var qualify = SpansSheets;
        var names = new List<string>(MaximumRenderedCells + 2);

        if (Path.Count <= MaximumRenderedCells)
        {
            names.AddRange(Path.Select(a => Describe(a, qualify)));
        }
        else
        {
            var head = MaximumRenderedCells - 2;
            names.AddRange(Path.Take(head).Select(a => Describe(a, qualify)));
            names.Add($"… ({Path.Count - head - 1:N0} more)");
            names.Add(Describe(Path[^1], qualify));
        }

        names.Add(Describe(Path[0], qualify));
        return string.Join(" → ", names);
    }

    /// <summary>Renders the ring as a sentence: <c>A1 refers to B3, which refers to C7, which refers back to A1</c>.</summary>
    public string ToSentence()
    {
        var qualify = SpansSheets;
        if (Path.Count == 1)
        {
            return $"{Describe(Path[0], qualify)} refers to itself";
        }

        var shown = Math.Min(Path.Count, MaximumRenderedCells);
        var sentence = new System.Text.StringBuilder(Describe(Path[0], qualify));
        for (var i = 1; i < shown; i++)
        {
            sentence.Append(i == 1 ? " refers to " : ", which refers to ").Append(Describe(Path[i], qualify));
        }

        if (shown < Path.Count)
        {
            sentence.Append($", and after {Path.Count - shown:N0} more cells");
        }

        return sentence.Append(", refers back to ").Append(Describe(Path[0], qualify)).ToString();
    }

    private static string Describe(SheetCellAddress address, bool qualify) =>
        qualify ? address.ToA1() : address.Address.ToA1();
}

/// <summary>
/// What one recalculation did: which cells changed, which cycles were found,
/// what the edit failed to parse, and how long it took.
/// </summary>
/// <remarks>
/// Every mutating call on <see cref="Workbook"/> returns one of these. Nothing
/// about a bad edit is communicated by an exception: a malformed formula appears
/// in <see cref="SyntaxErrors"/>, a ring appears in <see cref="Cycles"/>, and
/// the client decides what to show.
/// </remarks>
public sealed class CalculationResult
{
    internal CalculationResult(
        IReadOnlyList<CellChange> changes,
        IReadOnlyList<CircularReference> cycles,
        IReadOnlyList<FormulaSyntaxError> syntaxErrors,
        int cellsEvaluated,
        TimeSpan duration)
    {
        Changes = changes;
        Cycles = cycles;
        SyntaxErrors = syntaxErrors;
        CellsEvaluated = cellsEvaluated;
        Duration = duration;
    }

    /// <summary>A result in which nothing happened.</summary>
    public static CalculationResult Empty { get; } = new([], [], [], 0, TimeSpan.Zero);

    /// <summary>Cells whose value is different from before, in evaluation order.</summary>
    public IReadOnlyList<CellChange> Changes { get; }

    /// <summary>Every distinct dependency cycle discovered.</summary>
    public IReadOnlyList<CircularReference> Cycles { get; }

    /// <summary>Why the edited formula was rejected, if it was.</summary>
    public IReadOnlyList<FormulaSyntaxError> SyntaxErrors { get; }

    /// <summary>How many formula cells were evaluated. The measure of how little work was done.</summary>
    public int CellsEvaluated { get; }

    /// <summary>How long the recalculation took.</summary>
    public TimeSpan Duration { get; }

    /// <summary>True when a cycle was reported.</summary>
    public bool HasCycles => Cycles.Count > 0;

    /// <summary>True when the edited formula did not parse.</summary>
    public bool HasSyntaxErrors => SyntaxErrors.Count > 0;

    /// <inheritdoc />
    public override string ToString() =>
        $"{Changes.Count} change(s), {CellsEvaluated} evaluated, {Cycles.Count} cycle(s) in {Duration.TotalMilliseconds:F2} ms";

    internal static CalculationResult Merge(IReadOnlyList<CalculationResult> results)
    {
        var changes = new List<CellChange>();
        var cycles = new List<CircularReference>();
        var errors = new List<FormulaSyntaxError>();
        var evaluated = 0;
        var duration = TimeSpan.Zero;

        foreach (var result in results)
        {
            changes.AddRange(result.Changes);
            cycles.AddRange(result.Cycles);
            errors.AddRange(result.SyntaxErrors);
            evaluated += result.CellsEvaluated;
            duration += result.Duration;
        }

        return new CalculationResult(changes, cycles, errors, evaluated, duration);
    }
}

/// <summary>
/// An observer of cell changes — the Observer pattern's subscriber side.
/// </summary>
/// <remarks>
/// Implemented alongside the <see cref="Workbook.CellsChanged"/> event rather
/// than instead of it: the event is what a C# client expects, the interface is
/// what a client with several observers of its own (a grid, an audit log, a
/// dirty-document flag) can implement and pass around as a value.
/// </remarks>
public interface ICellChangeObserver
{
    /// <summary>Called once per recalculation, after every value is settled.</summary>
    void OnCellsChanged(CalculationResult result);
}

/// <summary>Carries a <see cref="CalculationResult"/> to event handlers.</summary>
public sealed class CellsChangedEventArgs(CalculationResult result) : EventArgs
{
    /// <summary>What the recalculation did.</summary>
    public CalculationResult Result { get; } = result;
}

/// <summary>Configuration for a <see cref="Workbook"/>.</summary>
public sealed class WorkbookOptions
{
    /// <summary>
    /// When false, an edit stores and evaluates only the edited cell; its
    /// dependents wait for <see cref="Workbook.CalculateNow"/>. This is Excel's
    /// manual calculation mode, and it is what a bulk import should use.
    /// </summary>
    public bool AutomaticCalculation { get; init; } = true;

    /// <summary>How many operations the undo history keeps. The brief requires at least 100.</summary>
    public int HistoryLimit { get; init; } = 100;

    /// <summary>The name of the sheet a new workbook starts with.</summary>
    public string InitialSheetName { get; init; } = "Sheet1";

    /// <summary>The function library; a fresh standard library when null.</summary>
    public Functions.FunctionRegistry? Functions { get; init; }
}

internal static class Stopwatches
{
    internal static TimeSpan Elapsed(long startTimestamp) =>
        Stopwatch.GetElapsedTime(startTimestamp);
}
