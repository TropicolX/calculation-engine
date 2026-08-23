using CalcEngine.Core.Model;

namespace CalcEngine.Core.Features.Duplicates;

/// <summary>One appearance of a record.</summary>
/// <param name="Cells">The cells the record occupies.</param>
/// <param name="Row">Its row; for a column record, the top row of the range.</param>
/// <param name="Column">Its column; for a row record, the leftmost column of the range.</param>
/// <param name="KeyValues">The values that were compared.</param>
public sealed record DuplicateOccurrence(
    CellRange Cells,
    int Row,
    int Column,
    IReadOnlyList<CellValue> KeyValues)
{
    /// <inheritdoc />
    public override string ToString() => Cells.ToA1();
}

/// <summary>A set of records that compared equal, in the order they were found.</summary>
public sealed record DuplicateGroup(string Key, IReadOnlyList<DuplicateOccurrence> Occurrences)
{
    /// <summary>The occurrence that would be kept: the first in reading order.</summary>
    public DuplicateOccurrence First => Occurrences[0];

    /// <summary>The occurrences that would be removed.</summary>
    public IEnumerable<DuplicateOccurrence> Repeats => Occurrences.Skip(1);

    /// <summary>How many times the record appears.</summary>
    public int Count => Occurrences.Count;

    /// <inheritdoc />
    public override string ToString() => $"{Count} occurrences: {string.Join(", ", Occurrences)}";
}

/// <summary>
/// What a duplicate scan found: the groups, the totals, and a fast test a grid
/// can call once per visible cell while it paints.
/// </summary>
public sealed class DuplicateReport
{
    private readonly HashSet<SheetCellAddress> _duplicateCells;

    internal DuplicateReport(
        string sheetName,
        DuplicateScope scope,
        IReadOnlyList<DuplicateGroup> groups,
        int distinctCount)
    {
        SheetName = sheetName;
        Scope = scope;
        Groups = groups;
        DistinctCount = distinctCount;

        _duplicateCells = [];
        foreach (var group in groups)
        {
            foreach (var repeat in group.Repeats)
            {
                foreach (var address in repeat.Cells.Cells())
                {
                    _duplicateCells.Add(new SheetCellAddress(sheetName, address));
                }
            }
        }
    }

    /// <summary>The sheet that was scanned.</summary>
    public string SheetName { get; }

    /// <summary>What was treated as a record.</summary>
    public DuplicateScope Scope { get; }

    /// <summary>Groups of two or more equal records, in first-appearance order.</summary>
    public IReadOnlyList<DuplicateGroup> Groups { get; }

    /// <summary>How many distinct records were seen.</summary>
    public int DistinctCount { get; }

    /// <summary>How many records are repeats — that is, how many removal would take out.</summary>
    public int DuplicateCount => Groups.Sum(g => g.Count - 1);

    /// <summary>Every cell belonging to a repeat: what a grid shades.</summary>
    public IReadOnlyCollection<SheetCellAddress> DuplicateCells => _duplicateCells;

    /// <summary>True when the cell belongs to a repeated record — not to the first occurrence.</summary>
    public bool IsDuplicate(SheetCellAddress address) => _duplicateCells.Contains(address);

    /// <inheritdoc />
    public override string ToString() =>
        $"{DuplicateCount} duplicate record(s) in {Groups.Count} group(s); {DistinctCount} distinct";
}

/// <summary>The outcome of a removal, including the case where it declined to act.</summary>
public sealed class RemoveDuplicatesResult
{
    internal RemoveDuplicatesResult(
        DuplicateReport report,
        int recordsRemoved,
        int recordsRemaining,
        IReadOnlyList<SheetCellAddress> formulasThatWouldMove,
        string? refusalReason,
        CalculationResult calculation)
    {
        Report = report;
        RecordsRemoved = recordsRemoved;
        RecordsRemaining = recordsRemaining;
        FormulasThatWouldMove = formulasThatWouldMove;
        RefusalReason = refusalReason;
        Calculation = calculation;
    }

    /// <summary>What the scan found.</summary>
    public DuplicateReport Report { get; }

    /// <summary>How many records were removed.</summary>
    public int RecordsRemoved { get; }

    /// <summary>How many distinct records are left.</summary>
    public int RecordsRemaining { get; }

    /// <summary>
    /// Formula cells that a shift would relocate. Reported whether or not the
    /// shift went ahead, so a client can warn either way.
    /// </summary>
    public IReadOnlyList<SheetCellAddress> FormulasThatWouldMove { get; }

    /// <summary>Why nothing was done, or null if something was.</summary>
    public string? RefusalReason { get; }

    /// <summary>True when the operation declined to act.</summary>
    public bool Refused => RefusalReason is not null;

    /// <summary>The recalculation the removal caused.</summary>
    public CalculationResult Calculation { get; }

    /// <inheritdoc />
    public override string ToString() =>
        Refused ? $"refused: {RefusalReason}" : $"{RecordsRemoved} record(s) removed, {RecordsRemaining} remaining";
}
