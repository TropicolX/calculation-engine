using CalcEngine.Core.Evaluation;
using CalcEngine.Core.Model;

namespace CalcEngine.Core;

/// <summary>What a piece of cell content turned out to be.</summary>
public enum CellContentKind
{
    /// <summary>No content at all.</summary>
    Blank,

    /// <summary>A formula, beginning with <c>=</c>.</summary>
    Formula,

    /// <summary>A number written in the invariant culture.</summary>
    Number,

    /// <summary>TRUE or FALSE.</summary>
    Boolean,

    /// <summary>One of the error spellings, typed literally.</summary>
    Error,

    /// <summary>Anything else.</summary>
    Text,
}

/// <summary>
/// Decides what a string typed into a cell means, before any parsing happens.
/// </summary>
/// <remarks>
/// This is the reason <see cref="Parsing.FormulaParser"/> throws when handed a
/// student's name: classification is a separate responsibility, and mixing the
/// two would make "Ngozi Okafor" a syntax error. Only content beginning with
/// <c>=</c> is ever offered to the parser.
/// </remarks>
public static class CellContentClassifier
{
    /// <summary>Classifies cell content.</summary>
    public static CellContentKind Classify(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return CellContentKind.Blank;
        }

        if (content[0] == '=')
        {
            return CellContentKind.Formula;
        }

        if (ValueCoercion.TryParseNumber(content, out _))
        {
            return CellContentKind.Number;
        }

        var trimmed = content.Trim();
        if (string.Equals(trimmed, "TRUE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return CellContentKind.Boolean;
        }

        if (ErrorValue.TryParse(trimmed, out _))
        {
            return CellContentKind.Error;
        }

        return CellContentKind.Text;
    }

    /// <summary>
    /// The value of non-formula content. Formula content must go to the parser
    /// instead, so passing it here is a programming error.
    /// </summary>
    public static CellValue LiteralValue(string? content)
    {
        switch (Classify(content))
        {
            case CellContentKind.Blank:
                return CellValue.Blank;

            case CellContentKind.Number:
                ValueCoercion.TryParseNumber(content!, out var number);
                return CellValue.Number(number);

            case CellContentKind.Boolean:
                return CellValue.Boolean(
                    string.Equals(content!.Trim(), "TRUE", StringComparison.OrdinalIgnoreCase));

            case CellContentKind.Error:
                ErrorValue.TryParse(content!.Trim(), out var error);
                return CellValue.FromError(error);

            case CellContentKind.Formula:
                throw new ArgumentException("Formula content must be parsed, not classified as a literal.", nameof(content));

            default:
                return CellValue.Text(content!);
        }
    }
}
