using CalcEngine.Core.Model;

namespace CalcEngine.Core.Features.FindReplace;

/// <summary>Which text of a cell is searched.</summary>
public enum SearchIn
{
    /// <summary>
    /// The raw content — what the formula bar shows. A formula cell is searched
    /// as its formula text. This is Excel's "Look in: Formulas".
    /// </summary>
    Content,

    /// <summary>
    /// The displayed result. This is Excel's "Look in: Values"; it finds
    /// computed results, and consequently most of what it finds cannot be
    /// replaced.
    /// </summary>
    DisplayValue,
}

/// <summary>The order in which cells are visited, which decides what "next" means.</summary>
public enum SearchOrder
{
    /// <summary>Left to right, then down.</summary>
    ByRows,

    /// <summary>Top to bottom, then across.</summary>
    ByColumns,
}

/// <summary>What replacement does when the cell holds a formula.</summary>
public enum FormulaHandling
{
    /// <summary>
    /// Treat the formula as ordinary text. This is Excel's behaviour and it is
    /// the default, because it is what someone renaming a sheet or a course code
    /// usually wants — but it is also how <c>=SUM(B2:B9)</c> silently becomes
    /// <c>=SUM(B3:B9)</c> when replacing "B2" with "B3".
    /// </summary>
    WholeContent,

    /// <summary>
    /// Replace only inside quoted text within the formula, using the parsed
    /// tree to decide what is text and what is structure. References, function
    /// names and operators cannot be touched.
    /// </summary>
    TextLiteralsOnly,

    /// <summary>Leave formula cells alone entirely.</summary>
    SkipFormulas,
}

/// <summary>
/// What to look for, where, and how strictly.
/// </summary>
/// <remarks>
/// An options object rather than a dozen parameters: the same object describes a
/// Find, a Find Next and a Replace All, so a user interface can bind one form to
/// all three and cannot let them drift apart.
/// </remarks>
public sealed class FindOptions
{
    /// <summary>The text (or regular expression) to look for. Must not be empty.</summary>
    public required string SearchText { get; init; }

    /// <summary>When false (the default) the search is case-insensitive, as in Excel.</summary>
    public bool MatchCase { get; init; }

    /// <summary>When true, only a cell whose whole text equals the search text matches.</summary>
    public bool MatchEntireCell { get; init; }

    /// <summary>When true, <see cref="SearchText"/> is a .NET regular expression.</summary>
    public bool UseRegex { get; init; }

    /// <summary>Which text of the cell to search.</summary>
    public SearchIn LookIn { get; init; } = SearchIn.Content;

    /// <summary>The order cells are visited in.</summary>
    public SearchOrder Order { get; init; } = SearchOrder.ByRows;

    /// <summary>Restrict to one sheet; null searches them all.</summary>
    public string? SheetName { get; init; }

    /// <summary>Restrict to a rectangle; null searches every populated cell.</summary>
    public CellRange? Range { get; init; }

    /// <summary>How replacement treats formula cells.</summary>
    public FormulaHandling Formulas { get; init; } = FormulaHandling.WholeContent;

    /// <summary>
    /// When true (the default), a replacement that would leave a formula
    /// unparsable is refused and reported instead of being written.
    /// </summary>
    public bool ValidateFormulas { get; init; } = true;

    /// <summary>How long a single regular-expression match may take before it is abandoned.</summary>
    public TimeSpan RegexTimeout { get; init; } = TimeSpan.FromSeconds(1);
}
