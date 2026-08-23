using System.Globalization;
using System.Text;
using CalcEngine.Core.Model;

namespace CalcEngine.Core.Features.Duplicates;

/// <summary>
/// Duplicate detection and removal — Group E, assigned feature 2.
/// </summary>
/// <remarks>
/// <para><b>Detection is one linear pass.</b> Each record is reduced to a
/// canonical key string and grouped in a dictionary, so a 50,000-row range costs
/// 50,000 key builds and 50,000 hash lookups rather than the 1.25 billion
/// comparisons of the pairwise approach.</para>
///
/// <para><b>The key is typed.</b> A number and the text that prints like it
/// produce different keys, so a column of imported text marks does not appear to
/// duplicate a typed one; each field is length-prefixed so that no combination
/// of contents can make two different records collide. Formulas contribute
/// their <em>value</em>, because two rows computing the same grade by different
/// routes are still the same record.</para>
///
/// <para><b>Removal is conservative by default.</b>
/// <see cref="DuplicateRemoval.ClearContents"/> blanks repeats where they stand;
/// nothing moves, so nothing that refers into the range can be silently
/// redirected. <see cref="DuplicateRemoval.ShiftUp"/> compacts the survivors,
/// and refuses outright if that would relocate a formula, because content moves
/// verbatim and its references do not follow it. That limitation is deliberate
/// and is discussed in docs/design-portfolio.md §7.2.</para>
/// </remarks>
public sealed class DuplicateService
{
    /// <summary>ASCII unit separator; ends every field of a record key.</summary>
    private const char FieldSeparator = '\u001F';

    private readonly Workbook _workbook;

    /// <summary>Creates a service over a workbook.</summary>
    public DuplicateService(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        _workbook = workbook;
    }

    /// <summary>Scans a range and reports what repeats.</summary>
    public DuplicateReport Find(DuplicateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var sheetName = options.SheetName ?? _workbook.Sheets[0].Name;
        if (!_workbook.TryGetSheet(sheetName, out _))
        {
            throw new KeyNotFoundException($"There is no sheet called '{sheetName}'.");
        }

        var groups = new List<List<DuplicateOccurrence>>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var keys = new List<string>();

        foreach (var (key, occurrence) in EnumerateRecords(sheetName, options))
        {
            if (!index.TryGetValue(key, out var at))
            {
                index[key] = groups.Count;
                keys.Add(key);
                groups.Add([occurrence]);
                continue;
            }

            groups[at].Add(occurrence);
        }

        var duplicated = new List<DuplicateGroup>();
        for (var i = 0; i < groups.Count; i++)
        {
            if (groups[i].Count > 1)
            {
                duplicated.Add(new DuplicateGroup(keys[i], groups[i]));
            }
        }

        return new DuplicateReport(sheetName, options.Scope, duplicated, groups.Count);
    }

    /// <summary>Scans and then removes, as one undoable operation.</summary>
    public RemoveDuplicatesResult Remove(
        DuplicateOptions options,
        DuplicateRemoval mode = DuplicateRemoval.ClearContents)
    {
        ArgumentNullException.ThrowIfNull(options);

        var report = Find(options);
        var remaining = report.DistinctCount;

        if (mode == DuplicateRemoval.MarkOnly || report.DuplicateCount == 0)
        {
            return new RemoveDuplicatesResult(report, 0, remaining, [], null, CalculationResult.Empty);
        }

        if (mode == DuplicateRemoval.ClearContents)
        {
            var edits = new List<CellEdit>();
            foreach (var group in report.Groups)
            {
                foreach (var repeat in group.Repeats)
                {
                    foreach (var address in repeat.Cells.Cells())
                    {
                        edits.Add(new CellEdit(new SheetCellAddress(report.SheetName, address), null));
                    }
                }
            }

            var cleared = _workbook.SetCellContents(
                edits,
                $"remove {report.DuplicateCount} duplicate record(s)");
            return new RemoveDuplicatesResult(report, report.DuplicateCount, remaining, [], null, cleared);
        }

        return ShiftUp(options, report);
    }

    // ------------------------------------------------------------- internals

    private RemoveDuplicatesResult ShiftUp(DuplicateOptions options, DuplicateReport report)
    {
        if (options.Scope == DuplicateScope.Cells)
        {
            throw new ArgumentException(
                "ShiftUp is not defined for cell-level duplicates: there is no record to compact. " +
                "Use ClearContents or MarkOnly.",
                nameof(options));
        }

        var byRows = options.Scope == DuplicateScope.Rows;
        var removed = new HashSet<int>();
        foreach (var group in report.Groups)
        {
            foreach (var repeat in group.Repeats)
            {
                removed.Add(byRows ? repeat.Row : repeat.Column);
            }
        }

        var (firstLine, lastLine) = RecordLines(options);
        var survivors = new List<int>();
        for (var line = firstLine; line <= lastLine; line++)
        {
            if (!removed.Contains(line))
            {
                survivors.Add(line);
            }
        }

        // Which formulas a compaction would relocate.  Reported either way.
        var moving = new List<SheetCellAddress>();
        for (var i = 0; i < survivors.Count; i++)
        {
            if (survivors[i] == firstLine + i)
            {
                continue;
            }

            foreach (var address in RecordCells(options, survivors[i]))
            {
                var content = _workbook.GetCellContent(report.SheetName, address);
                if (content is not null && CellContentClassifier.Classify(content) == CellContentKind.Formula)
                {
                    moving.Add(new SheetCellAddress(report.SheetName, address));
                }
            }
        }

        if (moving.Count > 0 && !options.AllowMovingFormulas)
        {
            var named = string.Join(", ", moving.Take(5).Select(a => a.ToA1()));
            var ellipsis = moving.Count > 5 ? ", and more" : string.Empty;
            return new RemoveDuplicatesResult(
                report,
                0,
                report.DistinctCount,
                moving,
                $"compacting would move {moving.Count} formula cell(s) ({named}{ellipsis}); " +
                "their relative references would not follow them. " +
                "Use ClearContents, or set AllowMovingFormulas to accept it.",
                CalculationResult.Empty);
        }

        var edits = new List<CellEdit>();
        for (var i = 0; i < survivors.Count; i++)
        {
            var from = survivors[i];
            var to = firstLine + i;
            if (from == to)
            {
                continue;
            }

            var source = RecordCells(options, from).ToList();
            var destination = RecordCells(options, to).ToList();
            for (var c = 0; c < source.Count; c++)
            {
                edits.Add(new CellEdit(
                    new SheetCellAddress(report.SheetName, destination[c]),
                    _workbook.GetCellContent(report.SheetName, source[c])));
            }
        }

        for (var line = firstLine + survivors.Count; line <= lastLine; line++)
        {
            foreach (var address in RecordCells(options, line))
            {
                edits.Add(new CellEdit(new SheetCellAddress(report.SheetName, address), null));
            }
        }

        var calculation = _workbook.SetCellContents(
            edits,
            $"remove {report.DuplicateCount} duplicate record(s) and compact");

        return new RemoveDuplicatesResult(report, report.DuplicateCount, survivors.Count, moving, null, calculation);
    }

    /// <summary>The first and last record line (row or column) inside the range.</summary>
    private static (int First, int Last) RecordLines(DuplicateOptions options)
    {
        var range = options.Range;
        return options.Scope == DuplicateScope.Columns
            ? (range.TopLeft.Column + (options.HasHeaderRow ? 1 : 0), range.BottomRight.Column)
            : (range.TopLeft.Row + (options.HasHeaderRow ? 1 : 0), range.BottomRight.Row);
    }

    /// <summary>The cells of one record, in order.</summary>
    private static IEnumerable<CellAddress> RecordCells(DuplicateOptions options, int line)
    {
        var range = options.Range;
        if (options.Scope == DuplicateScope.Columns)
        {
            for (var row = range.TopLeft.Row; row <= range.BottomRight.Row; row++)
            {
                yield return new CellAddress(line, row);
            }

            yield break;
        }

        for (var column = range.TopLeft.Column; column <= range.BottomRight.Column; column++)
        {
            yield return new CellAddress(column, line);
        }
    }

    private IEnumerable<(string Key, DuplicateOccurrence Occurrence)> EnumerateRecords(
        string sheetName,
        DuplicateOptions options) =>
        options.Scope == DuplicateScope.Cells
            ? EnumerateCells(sheetName, options)
            : EnumerateLines(sheetName, options);

    private IEnumerable<(string Key, DuplicateOccurrence Occurrence)> EnumerateCells(
        string sheetName,
        DuplicateOptions options)
    {
        var builder = new StringBuilder();
        foreach (var address in options.Range.Cells())
        {
            var value = _workbook.GetCellValue(sheetName, address);
            if (value.IsBlank && !options.IncludeBlankRecords)
            {
                continue;
            }

            builder.Clear();
            AppendKey(builder, value, options);

            yield return (
                builder.ToString(),
                new DuplicateOccurrence(CellRange.Single(address), address.Row, address.Column, [value]));
        }
    }

    private IEnumerable<(string Key, DuplicateOccurrence Occurrence)> EnumerateLines(
        string sheetName,
        DuplicateOptions options)
    {
        var range = options.Range;
        var recordLength = options.Scope == DuplicateScope.Columns ? range.RowCount : range.ColumnCount;
        var keyOffsets = ResolveKeyOffsets(options, recordLength);
        var (firstLine, lastLine) = RecordLines(options);

        var builder = new StringBuilder();
        for (var line = firstLine; line <= lastLine; line++)
        {
            builder.Clear();
            var values = new CellValue[keyOffsets.Count];
            var allBlank = true;

            for (var i = 0; i < keyOffsets.Count; i++)
            {
                var address = options.Scope == DuplicateScope.Columns
                    ? new CellAddress(line, range.TopLeft.Row + keyOffsets[i])
                    : new CellAddress(range.TopLeft.Column + keyOffsets[i], line);

                var value = _workbook.GetCellValue(sheetName, address);
                values[i] = value;
                allBlank &= value.IsBlank;
                AppendKey(builder, value, options);
            }

            if (allBlank && !options.IncludeBlankRecords)
            {
                continue;
            }

            var cells = options.Scope == DuplicateScope.Columns
                ? new CellRange(new CellAddress(line, range.TopLeft.Row), new CellAddress(line, range.BottomRight.Row))
                : new CellRange(
                    new CellAddress(range.TopLeft.Column, line),
                    new CellAddress(range.BottomRight.Column, line));

            var occurrence = options.Scope == DuplicateScope.Columns
                ? new DuplicateOccurrence(cells, range.TopLeft.Row, line, values)
                : new DuplicateOccurrence(cells, line, range.TopLeft.Column, values);

            yield return (builder.ToString(), occurrence);
        }
    }

    private static IReadOnlyList<int> ResolveKeyOffsets(DuplicateOptions options, int recordLength)
    {
        if (options.KeyColumnOffsets is not { } offsets)
        {
            return Enumerable.Range(0, recordLength).ToList();
        }

        foreach (var offset in offsets)
        {
            if (offset < 0 || offset >= recordLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    offset,
                    $"Key offset {offset} is outside the range, which is {recordLength} wide.");
            }
        }

        return offsets;
    }

    /// <summary>
    /// Appends one field to a record key.
    /// </summary>
    /// <remarks>
    /// A one-character type tag keeps 5 and "5" apart, and a length prefix keeps
    /// { "a", "b" } apart from { "ab", "" } — two records that a naive
    /// concatenation would declare identical.
    /// </remarks>
    private static void AppendKey(StringBuilder builder, CellValue value, DuplicateOptions options)
    {
        switch (value.Kind)
        {
            case CellValueKind.Blank:
                builder.Append('_');
                break;

            case CellValueKind.Number:
                AppendField(builder, 'n', value.AsNumber.ToString("R", CultureInfo.InvariantCulture));
                break;

            case CellValueKind.Boolean:
                AppendField(builder, 'b', value.AsBoolean ? "1" : "0");
                break;

            case CellValueKind.Error:
                AppendField(builder, 'e', value.AsError.Code);
                break;

            default:
                var text = value.AsText;
                if (options.TrimWhitespace)
                {
                    text = text.Trim();
                }

                if (!options.MatchCase)
                {
                    text = text.ToUpperInvariant();
                }

                AppendField(builder, 't', text);
                break;
        }

        builder.Append(FieldSeparator);
    }

    private static void AppendField(StringBuilder builder, char tag, string payload) =>
        builder.Append(tag)
            .Append(payload.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(payload);
}
