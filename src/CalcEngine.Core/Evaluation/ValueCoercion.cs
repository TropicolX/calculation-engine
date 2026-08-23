using System.Globalization;
using CalcEngine.Core.Model;

namespace CalcEngine.Core.Evaluation;

/// <summary>
/// The conversions and the ordering that make a spreadsheet a spreadsheet.
/// </summary>
/// <remarks>
/// <para>Every method here returns a <see cref="CellValue"/> rather than
/// throwing: a coercion that cannot be made is <c>#VALUE!</c>, which is a
/// result the user can see in a cell, not an exception that escapes to the
/// client.</para>
///
/// <para>Two rules are worth stating because they surprise programmers.
/// Text comparison is <b>case-insensitive</b> — <c>="a"="A"</c> is TRUE, and
/// <c>EXACT</c> is the case-sensitive alternative. And values of different
/// types never interleave: every number sorts before every text, which sorts
/// before every boolean, so <c>=1&lt;"a"</c> is TRUE regardless of the number.
/// Both are Excel's rules, and departmental workbooks depend on them.</para>
/// </remarks>
public static class ValueCoercion
{
    /// <summary>Coerces to a number, or returns the blocking error.</summary>
    public static CellValue ToNumber(CellValue value) => value.Kind switch
    {
        CellValueKind.Number => value,
        CellValueKind.Blank => CellValue.Zero,
        CellValueKind.Boolean => CellValue.Number(value.AsBoolean ? 1 : 0),
        CellValueKind.Error => value,
        CellValueKind.Text => TryParseNumber(value.AsText, out var parsed)
            ? CellValue.Number(parsed)
            : CellValue.FromError(ErrorValue.Value.WithDetail($"'{value.AsText}' is not a number")),
        _ => CellValue.FromError(ErrorValue.Value),
    };

    /// <summary>Coerces to text, or returns the blocking error.</summary>
    public static CellValue ToText(CellValue value) => value.Kind switch
    {
        CellValueKind.Text => value,
        CellValueKind.Error => value,
        _ => CellValue.Text(value.ToDisplayString()),
    };

    /// <summary>Coerces to a truth value, or returns the blocking error.</summary>
    public static CellValue ToBoolean(CellValue value) => value.Kind switch
    {
        CellValueKind.Boolean => value,
        CellValueKind.Blank => CellValue.False,
        CellValueKind.Number => CellValue.Boolean(value.AsNumber != 0),
        CellValueKind.Error => value,
        CellValueKind.Text => ParseBoolean(value.AsText),
        _ => CellValue.FromError(ErrorValue.Value),
    };

    /// <summary>Reads a number in the invariant culture; the only numeric syntax the engine accepts.</summary>
    public static bool TryParseNumber(string text, out double number)
    {
        const NumberStyles Styles = NumberStyles.Float;
        return double.TryParse(text.Trim(), Styles, CultureInfo.InvariantCulture, out number);
    }

    /// <summary>
    /// Wraps a computed double, mapping the two results a spreadsheet has no
    /// number for onto <c>#NUM!</c>. Nothing else in the engine builds a numeric
    /// <see cref="CellValue"/> directly, which is what keeps the representation
    /// invariant "a number is finite" true.
    /// </summary>
    public static CellValue FromDouble(double result) =>
        double.IsFinite(result)
            ? CellValue.Number(result)
            : CellValue.FromError(ErrorValue.Number.WithDetail(
                double.IsNaN(result) ? "the result is not a number" : "the result is too large to represent"));

    /// <summary>
    /// The spreadsheet ordering: numbers, then text, then booleans; text
    /// compared case-insensitively; blank equal to 0, to "" and to FALSE.
    /// </summary>
    /// <returns>Negative, zero or positive, as for <see cref="IComparable{T}"/>.</returns>
    public static int Compare(CellValue left, CellValue right)
    {
        // A blank takes the type of whatever it is compared against, so that
        // both =A1=0 and =A1="" are TRUE for an empty A1.
        if (left.IsBlank && !right.IsBlank)
        {
            left = Blanken(right);
        }
        else if (right.IsBlank && !left.IsBlank)
        {
            right = Blanken(left);
        }
        else if (left.IsBlank && right.IsBlank)
        {
            return 0;
        }

        var leftRank = TypeRank(left);
        var rightRank = TypeRank(right);
        if (leftRank != rightRank)
        {
            return leftRank.CompareTo(rightRank);
        }

        return leftRank switch
        {
            0 => left.AsNumber.CompareTo(right.AsNumber),
            1 => string.Compare(left.AsText, right.AsText, StringComparison.OrdinalIgnoreCase),
            _ => left.AsBoolean.CompareTo(right.AsBoolean),
        };
    }

    /// <summary>Case-sensitive equality, as used by <c>EXACT</c>.</summary>
    public static bool ExactEquals(CellValue left, CellValue right)
    {
        if (left.IsText && right.IsText)
        {
            return string.Equals(left.AsText, right.AsText, StringComparison.Ordinal);
        }

        return Compare(left, right) == 0;
    }

    private static CellValue Blanken(CellValue other) => other.Kind switch
    {
        CellValueKind.Text => CellValue.Text(string.Empty),
        CellValueKind.Boolean => CellValue.False,
        _ => CellValue.Zero,
    };

    private static int TypeRank(CellValue value) => value.Kind switch
    {
        CellValueKind.Number => 0,
        CellValueKind.Text => 1,
        CellValueKind.Boolean => 2,
        _ => 0,
    };

    private static CellValue ParseBoolean(string text)
    {
        if (string.Equals(text, "TRUE", StringComparison.OrdinalIgnoreCase))
        {
            return CellValue.True;
        }

        if (string.Equals(text, "FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return CellValue.False;
        }

        return TryParseNumber(text, out var number)
            ? CellValue.Boolean(number != 0)
            : CellValue.FromError(ErrorValue.Value.WithDetail($"'{text}' is not TRUE or FALSE"));
    }
}
