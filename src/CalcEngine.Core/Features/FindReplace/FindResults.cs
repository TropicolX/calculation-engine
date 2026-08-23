using CalcEngine.Core.Model;

namespace CalcEngine.Core.Features.FindReplace;

/// <summary>One occurrence of the search text in one cell.</summary>
/// <param name="Address">The cell it was found in.</param>
/// <param name="SearchedText">The text that was searched — content or displayed value.</param>
/// <param name="Start">Zero-based offset of the occurrence within that text.</param>
/// <param name="Length">Length of the occurrence.</param>
/// <param name="Target">Which text was searched.</param>
/// <remarks>
/// A reference type, so that "no match" is a null reference rather than a
/// nullable value: <c>FindNext(...)?.Address</c> is the shape every caller
/// wants, and it keeps a match cheap to pass around a user interface.
/// </remarks>
public sealed record FindMatch(
    SheetCellAddress Address,
    string SearchedText,
    int Start,
    int Length,
    SearchIn Target)
{
    /// <summary>The matched substring.</summary>
    public string MatchedText => SearchedText.Substring(Start, Length);

    /// <inheritdoc />
    public override string ToString() => $"{Address.ToA1()} [{Start}..{Start + Length}) '{MatchedText}'";
}

/// <summary>Why a cell that matched was not changed.</summary>
public enum ReplaceSkipReason
{
    /// <summary>
    /// The match was in a computed value. A formula's result is not writable,
    /// and a literal's displayed form may differ from its content.
    /// </summary>
    ComputedValue,

    /// <summary>The caller asked for formula cells to be left alone.</summary>
    FormulaSkippedByOption,

    /// <summary>In text-literals-only mode, the match was in the formula's structure, not its text.</summary>
    NoTextLiteralMatch,

    /// <summary>The replacement would have left the formula unparsable.</summary>
    WouldBreakFormula,
}

/// <summary>A cell that matched but was deliberately not written.</summary>
/// <param name="Address">The cell.</param>
/// <param name="Reason">Why it was left alone.</param>
/// <param name="Explanation">A sentence for the user.</param>
public readonly record struct SkippedReplacement(
    SheetCellAddress Address,
    ReplaceSkipReason Reason,
    string Explanation)
{
    /// <inheritdoc />
    public override string ToString() => $"{Address.ToA1()}: {Explanation}";
}

/// <summary>What a replace operation did — and, as importantly, what it declined to do.</summary>
public sealed class ReplaceResult
{
    internal ReplaceResult(
        int occurrencesReplaced,
        IReadOnlyList<SheetCellAddress> changedCells,
        IReadOnlyList<SkippedReplacement> skipped,
        CalculationResult calculation)
    {
        OccurrencesReplaced = occurrencesReplaced;
        ChangedCells = changedCells;
        Skipped = skipped;
        Calculation = calculation;
    }

    /// <summary>How many individual occurrences were rewritten.</summary>
    public int OccurrencesReplaced { get; }

    /// <summary>How many cells were rewritten.</summary>
    public int CellsChanged => ChangedCells.Count;

    /// <summary>The cells that were rewritten, in visit order.</summary>
    public IReadOnlyList<SheetCellAddress> ChangedCells { get; }

    /// <summary>Cells that matched but were not written, each with a reason.</summary>
    public IReadOnlyList<SkippedReplacement> Skipped { get; }

    /// <summary>The recalculation the replacement caused.</summary>
    public CalculationResult Calculation { get; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{OccurrencesReplaced} occurrence(s) in {CellsChanged} cell(s); {Skipped.Count} skipped";
}
