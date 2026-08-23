using System.Text;
using CalcEngine.Core.Evaluation;
using CalcEngine.Core.Model;

namespace CalcEngine.Core.Functions;

/// <summary>
/// The functions <see cref="FunctionRegistry.CreateDefault"/> installs.
/// </summary>
/// <remarks>
/// The eight named in the project brief — SUM, AVERAGE, MIN, MAX, COUNT, IF,
/// ROUND, LOOKUP — plus the supporting set a results sheet actually needs:
/// COUNTIF and COUNTA for flagging repeated entries, the text functions for
/// tidying imported names, and the IS* predicates for guarding against missing
/// marks.
/// </remarks>
internal static class StandardLibrary
{
    internal static IEnumerable<IFunction> All()
    {
        // --- required by the specification ---------------------------------
        yield return new SumFunction();
        yield return new AverageFunction();
        yield return new MinFunction();
        yield return new MaxFunction();
        yield return new CountFunction();
        yield return new IfFunction();
        yield return Round("ROUND", "Rounds a number to a given number of digits.", RoundingMode.Nearest);
        yield return new LookupFunction();

        // --- counting ------------------------------------------------------
        yield return new CountAFunction();
        yield return new CountIfFunction();

        // --- arithmetic ----------------------------------------------------
        yield return Round("ROUNDUP", "Rounds away from zero.", RoundingMode.Up);
        yield return Round("ROUNDDOWN", "Rounds towards zero.", RoundingMode.Down);
        yield return PointwiseFunction.Number1("ABS", "Absolute value.", n => ValueCoercion.FromDouble(Math.Abs(n)));
        yield return PointwiseFunction.Number1("INT", "Rounds down to an integer.", n => ValueCoercion.FromDouble(Math.Floor(n)));
        yield return PointwiseFunction.Number1(
            "SQRT",
            "Square root.",
            n => n < 0
                ? CellValue.FromError(ErrorValue.Number.WithDetail("SQRT of a negative number"))
                : ValueCoercion.FromDouble(Math.Sqrt(n)));
        yield return PointwiseFunction.Number2(
            "MOD",
            "Remainder after division; the sign follows the divisor.",
            (a, b) => b == 0
                ? CellValue.FromError(ErrorValue.DivideByZero.WithDetail("MOD by zero"))
                : ValueCoercion.FromDouble(a - (b * Math.Floor(a / b))));
        yield return PointwiseFunction.Number2(
            "POWER",
            "Raises a number to a power.",
            (a, b) => ValueCoercion.FromDouble(Math.Pow(a, b)));

        // --- logic ---------------------------------------------------------
        yield return AndOrFunction.And();
        yield return AndOrFunction.Or();
        yield return new PointwiseFunction("NOT", 1, 1, "Reverses a logical value.", arguments =>
        {
            var value = arguments.Boolean(0);
            return value.IsError ? value : CellValue.Boolean(!value.AsBoolean);
        });
        yield return new IfErrorFunction();
        yield return PointwiseFunction.Predicate("ISERROR", "TRUE if the value is an error.", v => v.IsError);
        yield return PointwiseFunction.Predicate("ISBLANK", "TRUE if the cell is empty.", v => v.IsBlank);
        yield return PointwiseFunction.Predicate("ISNUMBER", "TRUE if the value is a number.", v => v.IsNumber);
        yield return PointwiseFunction.Predicate("ISTEXT", "TRUE if the value is text.", v => v.IsText);

        // --- text ----------------------------------------------------------
        yield return PointwiseFunction.Text1("LEN", "Number of characters.", t => CellValue.Number(t.Length));
        yield return PointwiseFunction.Text1("UPPER", "Converts to upper case.", t => CellValue.Text(t.ToUpperInvariant()));
        yield return PointwiseFunction.Text1("LOWER", "Converts to lower case.", t => CellValue.Text(t.ToLowerInvariant()));
        yield return PointwiseFunction.Text1("TRIM", "Removes leading, trailing and repeated spaces.", t => CellValue.Text(Trim(t)));
        yield return PointwiseFunction.Text1(
            "VALUE",
            "Converts text to a number.",
            t => ValueCoercion.TryParseNumber(t, out var n)
                ? CellValue.Number(n)
                : CellValue.FromError(ErrorValue.Value.WithDetail($"'{t}' is not a number")));
        yield return PointwiseFunction.TextAndNumber(
            "LEFT",
            "The first characters of a text.",
            (t, n) => Slice(t, 0, n));
        yield return PointwiseFunction.TextAndNumber(
            "RIGHT",
            "The last characters of a text.",
            (t, n) => Slice(t, t.Length - Clamp(n, t.Length), n));
        yield return new PointwiseFunction("MID", 3, 3, "A slice of a text.", arguments =>
        {
            var text = arguments.Text(0);
            if (text.IsError)
            {
                return text;
            }

            var start = arguments.Number(1);
            if (start.IsError)
            {
                return start;
            }

            var length = arguments.Number(2);
            if (length.IsError)
            {
                return length;
            }

            return start.AsNumber < 1
                ? arguments.Invalid("the starting position must be 1 or more")
                : Slice(text.AsText, (int)start.AsNumber - 1, length.AsNumber);
        });
        yield return new PointwiseFunction("EXACT", 2, 2, "Case-sensitive comparison.", arguments =>
        {
            var left = arguments.Value(0);
            if (left.IsError)
            {
                return left;
            }

            var right = arguments.Value(1);
            return right.IsError ? right : CellValue.Boolean(ValueCoercion.ExactEquals(left, right));
        });
        yield return new PointwiseFunction("CONCAT", 1, int.MaxValue, "Joins its arguments into one text.", arguments =>
        {
            var builder = new StringBuilder();
            foreach (var argument in arguments.FlattenAll())
            {
                if (argument.Value.IsError)
                {
                    return argument.Value;
                }

                builder.Append(argument.Value.ToDisplayString());
            }

            return CellValue.Text(builder.ToString());
        });
        yield return new PointwiseFunction(
            "SUBSTITUTE",
            3,
            4,
            "Replaces occurrences of one text with another.",
            Substitute);
    }

    private enum RoundingMode
    {
        Nearest,
        Up,
        Down,
    }

    /// <summary>
    /// ROUND and its two directional siblings.
    /// </summary>
    /// <remarks>
    /// Negative digit counts scale by division rather than by multiplying by a
    /// fractional factor: <c>1250/100</c> is exact where <c>1250*0.01</c> is
    /// not, and ROUND(1250,-2) must be 1300 rather than 1299.999999999999.
    /// Midpoints go away from zero, which is the spreadsheet convention and not
    /// .NET's default banker's rounding.
    /// </remarks>
    private static IFunction Round(string name, string description, RoundingMode mode) =>
        PointwiseFunction.Number2(name, description, (value, digits) =>
        {
            if (digits is > 15 or < -15)
            {
                return CellValue.FromError(
                    ErrorValue.Number.WithDetail($"{name} accepts between -15 and 15 digits"));
            }

            var places = (int)digits;
            var factor = Math.Pow(10, Math.Abs(places));
            var scaled = places >= 0 ? value * factor : value / factor;
            if (!double.IsFinite(scaled))
            {
                return CellValue.FromError(ErrorValue.Number.WithDetail("the result is too large to represent"));
            }

            var rounded = mode switch
            {
                RoundingMode.Nearest => Math.Round(scaled, MidpointRounding.AwayFromZero),
                RoundingMode.Up => scaled < 0 ? Math.Floor(scaled) : Math.Ceiling(scaled),
                _ => Math.Truncate(scaled),
            };

            return ValueCoercion.FromDouble(places >= 0 ? rounded / factor : rounded * factor);
        });

    /// <summary>Excel's TRIM: strips the ends and collapses internal runs of spaces to one.</summary>
    private static string Trim(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text.Trim())
        {
            if (c == ' ')
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace && builder.Length > 0)
            {
                builder.Append(' ');
            }

            pendingSpace = false;
            builder.Append(c);
        }

        return builder.ToString();
    }

    private static int Clamp(double count, int available) =>
        count < 0 ? 0 : count > available ? available : (int)count;

    private static CellValue Slice(string text, int start, double count)
    {
        if (count < 0)
        {
            return CellValue.FromError(ErrorValue.Value.WithDetail("a negative number of characters was requested"));
        }

        if (start >= text.Length || start < 0)
        {
            return CellValue.Text(string.Empty);
        }

        var take = Clamp(count, text.Length - start);
        return CellValue.Text(text.Substring(start, take));
    }

    private static CellValue Substitute(FunctionArguments arguments)
    {
        var text = arguments.Text(0);
        if (text.IsError)
        {
            return text;
        }

        var oldText = arguments.Text(1);
        if (oldText.IsError)
        {
            return oldText;
        }

        var newText = arguments.Text(2);
        if (newText.IsError)
        {
            return newText;
        }

        if (oldText.AsText.Length == 0)
        {
            return text;
        }

        if (arguments.Count == 3)
        {
            return CellValue.Text(text.AsText.Replace(oldText.AsText, newText.AsText, StringComparison.Ordinal));
        }

        var instance = arguments.Number(3);
        if (instance.IsError)
        {
            return instance;
        }

        var wanted = (int)instance.AsNumber;
        if (wanted < 1)
        {
            return arguments.Invalid("the occurrence number must be 1 or more");
        }

        var source = text.AsText;
        var index = -1;
        for (var found = 0; found < wanted; found++)
        {
            index = source.IndexOf(oldText.AsText, index + 1, StringComparison.Ordinal);
            if (index < 0)
            {
                return text;
            }
        }

        return CellValue.Text(
            string.Concat(source.AsSpan(0, index), newText.AsText, source.AsSpan(index + oldText.AsText.Length)));
    }
}
