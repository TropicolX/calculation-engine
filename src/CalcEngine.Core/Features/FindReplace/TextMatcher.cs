using System.Text;
using System.Text.RegularExpressions;

namespace CalcEngine.Core.Features.FindReplace;

/// <summary>
/// The matching engine behind Find and Replace: plain text or regular
/// expression, case-sensitive or not, whole-cell or substring.
/// </summary>
/// <remarks>
/// Isolated from the search itself so that "which cells do we look at" and "what
/// counts as a match" can be tested and changed independently — and so that the
/// regular-expression timeout, which is a denial-of-service guard rather than a
/// matching rule, lives in exactly one place.
/// </remarks>
internal sealed class TextMatcher
{
    private readonly string _search;
    private readonly StringComparison _comparison;
    private readonly bool _entireCell;
    private readonly Regex? _regex;

    internal TextMatcher(FindOptions options)
    {
        if (string.IsNullOrEmpty(options.SearchText))
        {
            throw new ArgumentException("There is nothing to search for: SearchText is empty.", nameof(options));
        }

        _search = options.SearchText;
        _entireCell = options.MatchEntireCell;
        _comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        if (!options.UseRegex)
        {
            return;
        }

        var regexOptions = RegexOptions.CultureInvariant;
        if (!options.MatchCase)
        {
            regexOptions |= RegexOptions.IgnoreCase;
        }

        try
        {
            // A timeout, not a hope: a user-supplied pattern such as (a+)+$ can
            // take exponential time, and a spreadsheet must not hang because
            // someone typed one into a search box.
            _regex = new Regex(options.SearchText, regexOptions, options.RegexTimeout);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(
                $"'{options.SearchText}' is not a valid regular expression: {exception.Message}",
                nameof(options),
                exception);
        }
    }

    /// <summary>Every non-overlapping occurrence in <paramref name="text"/>, left to right.</summary>
    internal IEnumerable<(int Start, int Length)> Matches(string text)
    {
        if (text.Length == 0)
        {
            yield break;
        }

        if (_regex is not null)
        {
            foreach (Match match in _regex.Matches(text))
            {
                if (!_entireCell || (match.Index == 0 && match.Length == text.Length))
                {
                    yield return (match.Index, match.Length);
                }
            }

            yield break;
        }

        if (_entireCell)
        {
            if (text.Equals(_search, _comparison))
            {
                yield return (0, text.Length);
            }

            yield break;
        }

        var from = 0;
        while (from <= text.Length - _search.Length)
        {
            var at = text.IndexOf(_search, from, _comparison);
            if (at < 0)
            {
                yield break;
            }

            yield return (at, _search.Length);
            from = at + Math.Max(1, _search.Length);
        }
    }

    /// <summary>Replaces every occurrence, reporting how many there were.</summary>
    internal string ReplaceAll(string text, string replacement, out int count)
    {
        var occurrences = Matches(text).ToList();
        count = occurrences.Count;
        if (count == 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var copiedTo = 0;
        foreach (var (start, length) in occurrences)
        {
            builder.Append(text, copiedTo, start - copiedTo);
            builder.Append(Expand(text, start, length, replacement));
            copiedTo = start + length;
        }

        builder.Append(text, copiedTo, text.Length - copiedTo);
        return builder.ToString();
    }

    /// <summary>Replaces the single occurrence at <paramref name="start"/>.</summary>
    internal string ReplaceOne(string text, int start, int length, string replacement) =>
        string.Concat(
            text.AsSpan(0, start),
            Expand(text, start, length, replacement),
            text.AsSpan(start + length));

    /// <summary>
    /// Applies <c>$1</c>-style group references, but only in regular-expression
    /// mode. In plain-text mode a <c>$</c> in the replacement is a dollar sign,
    /// which is what someone replacing "USD" with "$" expects.
    /// </summary>
    private string Expand(string text, int start, int length, string replacement)
    {
        if (_regex is null)
        {
            return replacement;
        }

        var match = _regex.Match(text, start, length);
        return match.Success ? match.Result(replacement) : replacement;
    }
}
