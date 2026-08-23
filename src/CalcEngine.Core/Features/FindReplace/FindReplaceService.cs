using System.Text;
using CalcEngine.Core.Expressions;
using CalcEngine.Core.Model;
using CalcEngine.Core.Parsing;

namespace CalcEngine.Core.Features.FindReplace;

/// <summary>
/// Find and Replace across a workbook — Group E, assigned feature 1.
/// </summary>
/// <remarks>
/// <para>Searching a spreadsheet is easy. Replacing in one is not, because three
/// of the places a match can be found are places a naive replace corrupts:</para>
///
/// <list type="number">
///   <item><description><b>Inside a formula's structure.</b> Replacing "B2"
///   with "B3" turns <c>=SUM(B2:B9)</c> into <c>=SUM(B3:B9)</c> and changes
///   every total on the sheet, silently.
///   <see cref="FormulaHandling.TextLiteralsOnly"/> uses the parsed tree and the
///   source spans of its text literals to rewrite only what is genuinely
///   text.</description></item>
///
///   <item><description><b>Inside a computed value.</b> A formula's result
///   cannot be written back, and a literal's displayed form need not equal its
///   content ("72.50" displays as 72.5). Such cells are reported in
///   <see cref="ReplaceResult.Skipped"/> with
///   <see cref="ReplaceSkipReason.ComputedValue"/> rather than being mangled or
///   ignored.</description></item>
///
///   <item><description><b>Where the result would not parse.</b> By default the
///   rewritten formula is re-parsed before it is written, and a replacement that
///   would break it is refused with the parser's own message.</description></item>
/// </list>
///
/// <para>Every replacement pass is one <see cref="Commands.SetCellsCommand"/>:
/// one recalculation, one notification, one press of Ctrl+Z.</para>
/// </remarks>
public sealed class FindReplaceService
{
    private readonly Workbook _workbook;

    /// <summary>Creates a service over a workbook.</summary>
    public FindReplaceService(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        _workbook = workbook;
    }

    /// <summary>Every occurrence, in the order given by <see cref="FindOptions.Order"/>.</summary>
    public IReadOnlyList<FindMatch> FindAll(FindOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var matcher = new TextMatcher(options);

        var matches = new List<FindMatch>();
        foreach (var address in CellsToSearch(options))
        {
            var text = TextOf(address, options.LookIn);
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            foreach (var (start, length) in matcher.Matches(text))
            {
                matches.Add(new FindMatch(address, text, start, length, options.LookIn));
            }
        }

        return matches;
    }

    /// <summary>How many occurrences there are.</summary>
    public int Count(FindOptions options) => FindAll(options).Count;

    /// <summary>
    /// The first occurrence after <paramref name="after"/>, wrapping round to
    /// the start of the workbook. Null when nothing matches at all.
    /// </summary>
    public FindMatch? FindNext(FindOptions options, SheetCellAddress? after = null)
    {
        var matches = FindAll(options);
        if (matches.Count == 0)
        {
            return null;
        }

        if (after is not { } previous)
        {
            return matches[0];
        }

        var key = SortKey(previous, options.Order);
        foreach (var match in matches)
        {
            if (SortKey(match.Address, options.Order).CompareTo(key) > 0)
            {
                return match;
            }
        }

        return matches[0];
    }

    /// <summary>The last occurrence before <paramref name="before"/>, wrapping round to the end.</summary>
    public FindMatch? FindPrevious(FindOptions options, SheetCellAddress? before = null)
    {
        var matches = FindAll(options);
        if (matches.Count == 0)
        {
            return null;
        }

        if (before is not { } next)
        {
            return matches[^1];
        }

        var key = SortKey(next, options.Order);
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            if (SortKey(matches[i].Address, options.Order).CompareTo(key) < 0)
            {
                return matches[i];
            }
        }

        return matches[^1];
    }

    /// <summary>Replaces every occurrence, as one undoable operation.</summary>
    public ReplaceResult ReplaceAll(FindOptions options, string replacement)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(replacement);

        var matcher = new TextMatcher(options);
        var edits = new List<CellEdit>();
        var changed = new List<SheetCellAddress>();
        var skipped = new List<SkippedReplacement>();
        var occurrences = 0;

        foreach (var address in MatchedCells(options, matcher))
        {
            var outcome = Rewrite(address, options, matcher, replacement);
            if (outcome.Skip is { } reason)
            {
                skipped.Add(reason);
                continue;
            }

            if (outcome.NewContent is null)
            {
                continue;
            }

            edits.Add(new CellEdit(address, outcome.NewContent));
            changed.Add(address);
            occurrences += outcome.Occurrences;
        }

        if (edits.Count == 0)
        {
            return new ReplaceResult(0, changed, skipped, CalculationResult.Empty);
        }

        var description =
            $"replace \"{options.SearchText}\" with \"{replacement}\" in {edits.Count} cell{(edits.Count == 1 ? "" : "s")}";
        var calculation = _workbook.SetCellContents(edits, description);
        return new ReplaceResult(occurrences, changed, skipped, calculation);
    }

    /// <summary>Replaces a single occurrence found earlier by <see cref="FindNext"/>.</summary>
    public ReplaceResult Replace(FindMatch match, string replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        var content = _workbook.GetCellContent(match.Address);
        if (content is null || match.Target == SearchIn.DisplayValue)
        {
            return new ReplaceResult(
                0,
                [],
                [
                    new SkippedReplacement(
                        match.Address,
                        ReplaceSkipReason.ComputedValue,
                        "a displayed value cannot be written back into the cell"),
                ],
                CalculationResult.Empty);
        }

        // A regular expression can match the empty string, and a matcher cannot
        // be built for an empty search text.  Splice directly: the match already
        // says exactly which characters to replace.
        var newContent = match.Length == 0
            ? string.Concat(content.AsSpan(0, match.Start), replacement, content.AsSpan(match.Start))
            : new TextMatcher(new FindOptions { SearchText = match.MatchedText })
                .ReplaceOne(content, match.Start, match.Length, replacement);
        if (string.Equals(newContent, content, StringComparison.Ordinal))
        {
            return new ReplaceResult(0, [], [], CalculationResult.Empty);
        }

        var calculation = _workbook.SetCellContents(
            [new CellEdit(match.Address, newContent)],
            $"replace \"{match.MatchedText}\" in {match.Address.ToA1()}");

        return new ReplaceResult(1, [match.Address], [], calculation);
    }

    // ------------------------------------------------------------- internals

    private readonly record struct RewriteOutcome(string? NewContent, int Occurrences, SkippedReplacement? Skip);

    private RewriteOutcome Rewrite(
        SheetCellAddress address,
        FindOptions options,
        TextMatcher matcher,
        string replacement)
    {
        var content = _workbook.GetCellContent(address);
        if (content is null)
        {
            return new RewriteOutcome(null, 0, null);
        }

        var isFormula = CellContentClassifier.Classify(content) == CellContentKind.Formula;

        if (options.LookIn == SearchIn.DisplayValue)
        {
            // The match was in a computed result.  Only a literal whose content
            // also matches can be rewritten; anything else is reported, not
            // guessed at.
            if (isFormula)
            {
                return Skip(address, ReplaceSkipReason.ComputedValue,
                    "this cell shows the result of a formula, which cannot be written back");
            }

            var rewritten = matcher.ReplaceAll(content, replacement, out var literalCount);
            if (literalCount == 0)
            {
                return Skip(address, ReplaceSkipReason.ComputedValue,
                    "the text was found in the displayed value but not in the cell's content");
            }

            return new RewriteOutcome(rewritten, literalCount, null);
        }

        if (!isFormula)
        {
            var rewritten = matcher.ReplaceAll(content, replacement, out var count);
            return count == 0
                ? new RewriteOutcome(null, 0, null)
                : new RewriteOutcome(rewritten, count, null);
        }

        return options.Formulas switch
        {
            FormulaHandling.SkipFormulas => Skip(address, ReplaceSkipReason.FormulaSkippedByOption,
                "formula cells were excluded from this replacement"),
            FormulaHandling.TextLiteralsOnly => RewriteTextLiterals(address, content, matcher, replacement, options),
            _ => RewriteWholeFormula(address, content, matcher, replacement, options),
        };
    }

    private RewriteOutcome RewriteWholeFormula(
        SheetCellAddress address,
        string content,
        TextMatcher matcher,
        string replacement,
        FindOptions options)
    {
        var rewritten = matcher.ReplaceAll(content, replacement, out var count);
        if (count == 0)
        {
            return new RewriteOutcome(null, 0, null);
        }

        return Validate(address, rewritten, count, options);
    }

    /// <summary>
    /// Rewrites only the quoted text inside a formula, using the parsed tree.
    /// </summary>
    /// <remarks>
    /// Matching happens against the literal's <em>value</em> — the text after
    /// doubled quotes have been collapsed — because that is what the user sees
    /// and searches for. The result is re-quoted and spliced back over the
    /// literal's source span, so every other character of the formula, including
    /// the user's spacing, survives untouched.
    /// </remarks>
    private RewriteOutcome RewriteTextLiterals(
        SheetCellAddress address,
        string content,
        TextMatcher matcher,
        string replacement,
        FindOptions options)
    {
        var formula = _workbook.GetCellFormula(address);
        if (formula is null)
        {
            return Skip(address, ReplaceSkipReason.WouldBreakFormula,
                "this formula does not parse, so its text literals cannot be located");
        }

        var literals = TextLiteralFinder.FindAll(formula);
        if (literals.Count == 0)
        {
            return Skip(address, ReplaceSkipReason.NoTextLiteralMatch,
                "the match is in the formula's structure, not in any of its text");
        }

        var builder = new StringBuilder(content.Length);
        var copiedTo = 0;
        var replaced = 0;

        foreach (var literal in literals)
        {
            var rewrittenValue = matcher.ReplaceAll(literal.Value, replacement, out var count);
            if (count == 0)
            {
                continue;
            }

            replaced += count;
            builder.Append(content, copiedTo, literal.Span.Start - copiedTo);
            builder.Append('"').Append(rewrittenValue.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
            copiedTo = literal.Span.End;
        }

        if (replaced == 0)
        {
            return Skip(address, ReplaceSkipReason.NoTextLiteralMatch,
                "the match is in the formula's structure, not in any of its text");
        }

        builder.Append(content, copiedTo, content.Length - copiedTo);
        return Validate(address, builder.ToString(), replaced, options);
    }

    private static RewriteOutcome Validate(
        SheetCellAddress address,
        string rewritten,
        int occurrences,
        FindOptions options)
    {
        if (!options.ValidateFormulas || rewritten.Length == 0 || rewritten[0] != '=')
        {
            return new RewriteOutcome(rewritten, occurrences, null);
        }

        var parsed = FormulaParser.Parse(rewritten);
        if (parsed.Success)
        {
            return new RewriteOutcome(rewritten, occurrences, null);
        }

        return Skip(address, ReplaceSkipReason.WouldBreakFormula,
            $"the replacement would leave '{rewritten}' unparsable — {parsed.Errors[0].Message}");
    }

    private static RewriteOutcome Skip(SheetCellAddress address, ReplaceSkipReason reason, string explanation) =>
        new(null, 0, new SkippedReplacement(address, reason, explanation));

    /// <summary>The distinct cells holding at least one match, in visit order.</summary>
    private IEnumerable<SheetCellAddress> MatchedCells(FindOptions options, TextMatcher matcher)
    {
        foreach (var address in CellsToSearch(options))
        {
            var text = TextOf(address, options.LookIn);
            if (!string.IsNullOrEmpty(text) && matcher.Matches(text).Any())
            {
                yield return address;
            }
        }
    }

    private string? TextOf(SheetCellAddress address, SearchIn lookIn) =>
        lookIn == SearchIn.Content
            ? _workbook.GetCellContent(address)
            : _workbook.GetCellValue(address).ToDisplayString();

    /// <summary>
    /// The populated cells in scope, ordered so that "next" is well defined.
    /// </summary>
    /// <remarks>
    /// Only populated cells are visited. A search that walked the addressable
    /// grid would visit seventeen billion cells to find nothing.
    /// </remarks>
    private IEnumerable<SheetCellAddress> CellsToSearch(FindOptions options)
    {
        foreach (var sheet in _workbook.Sheets)
        {
            if (options.SheetName is not null &&
                !string.Equals(sheet.Name, options.SheetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var addresses = sheet.PopulatedCells().ToList();
            if (options.Range is { } range)
            {
                addresses.RemoveAll(a => !range.Contains(a));
            }

            addresses.Sort(options.Order == SearchOrder.ByRows
                ? static (a, b) => a.CompareTo(b)
                : static (a, b) => a.Column != b.Column ? a.Column.CompareTo(b.Column) : a.Row.CompareTo(b.Row));

            foreach (var address in addresses)
            {
                yield return new SheetCellAddress(sheet.Name, address);
            }
        }
    }

    private (int Sheet, int First, int Second) SortKey(SheetCellAddress address, SearchOrder order)
    {
        _workbook.TryGetSheetIndexPublic(address.Sheet, out var sheetIndex);
        return order == SearchOrder.ByRows
            ? (sheetIndex, address.Address.Row, address.Address.Column)
            : (sheetIndex, address.Address.Column, address.Address.Row);
    }
}
