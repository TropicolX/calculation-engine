using CalcEngine.Core.Model;

namespace CalcEngine.Core.Features.Duplicates;

/// <summary>What counts as a repeatable unit.</summary>
public enum DuplicateScope
{
    /// <summary>
    /// Each row of the range is a record, compared on its key columns. This is
    /// the one that matters for result processing: a student registered twice.
    /// </summary>
    Rows,

    /// <summary>Each column of the range is a record, compared on its key rows.</summary>
    Columns,

    /// <summary>Each individual cell value is compared with every other.</summary>
    Cells,
}

/// <summary>What removal does with the repeats it finds.</summary>
public enum DuplicateRemoval
{
    /// <summary>
    /// Blank the repeated records where they stand. The default, because
    /// nothing moves and therefore nothing that refers to the range can be
    /// silently redirected.
    /// </summary>
    ClearContents,

    /// <summary>
    /// Move the surviving records to the top (or left) of the range and clear
    /// what is left over — Excel's Remove Duplicates.
    /// </summary>
    ShiftUp,

    /// <summary>Change nothing; produce the report only.</summary>
    MarkOnly,
}

/// <summary>Where to look for duplicates and what makes two records the same.</summary>
public sealed class DuplicateOptions
{
    /// <summary>The rectangle to examine.</summary>
    public required CellRange Range { get; init; }

    /// <summary>Which sheet; null means the workbook's first sheet.</summary>
    public string? SheetName { get; init; }

    /// <summary>Rows, columns or individual cells.</summary>
    public DuplicateScope Scope { get; init; } = DuplicateScope.Rows;

    /// <summary>When true, the first row (or column) of the range is a header and is never compared.</summary>
    public bool HasHeaderRow { get; init; }

    /// <summary>
    /// Zero-based offsets <em>within a record</em> that form the key: column
    /// offsets in <see cref="DuplicateScope.Rows"/>, row offsets in
    /// <see cref="DuplicateScope.Columns"/>. Null compares the whole record,
    /// which is what Excel does when every column is ticked.
    /// </summary>
    public IReadOnlyList<int>? KeyColumnOffsets { get; init; }

    /// <summary>When false (the default), text is compared case-insensitively, as elsewhere in the engine.</summary>
    public bool MatchCase { get; init; }

    /// <summary>When true (the default), leading and trailing spaces are ignored — imported data is full of them.</summary>
    public bool TrimWhitespace { get; init; } = true;

    /// <summary>
    /// When false (the default), records whose key is entirely blank are not
    /// compared. Otherwise every empty row in a selection collapses into one
    /// enormous "duplicate" group, which is never what was meant.
    /// </summary>
    public bool IncludeBlankRecords { get; init; }

    /// <summary>
    /// Permits <see cref="DuplicateRemoval.ShiftUp"/> to move formula cells.
    /// Off by default: content moves verbatim and its references do not follow,
    /// so moving a formula silently is the kind of corruption this engine
    /// exists to prevent. The refusal names the cells.
    /// </summary>
    public bool AllowMovingFormulas { get; init; }
}
