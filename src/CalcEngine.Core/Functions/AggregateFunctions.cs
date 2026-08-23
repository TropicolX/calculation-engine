using CalcEngine.Core.Evaluation;
using CalcEngine.Core.Model;

namespace CalcEngine.Core.Functions;

/// <summary>
/// The collection rule shared by SUM, AVERAGE, MIN, MAX and COUNT.
/// </summary>
/// <remarks>
/// Excel's rule looks inconsistent until you see the reason for it: a value
/// read <em>through a reference</em> is data, and data of the wrong type is
/// skipped; a value written <em>into the formula</em> is an instruction, and an
/// instruction of the wrong type is coerced. So <c>=SUM(A1:A9)</c> quietly
/// ignores the cell that says "absent" — which is what a lecturer wants — while
/// <c>=SUM("3",TRUE)</c> is 4.
/// </remarks>
internal static class Aggregation
{
    /// <summary>
    /// Collects the numbers an aggregate should see.
    /// </summary>
    /// <returns>The blocking error, or null when collection succeeded.</returns>
    internal static CellValue? CollectNumbers(FunctionArguments arguments, List<double> destination)
    {
        foreach (var argument in arguments.FlattenAll())
        {
            var value = argument.Value;
            if (value.IsError)
            {
                return value;
            }

            if (argument.FromReference)
            {
                if (value.IsNumber)
                {
                    destination.Add(value.AsNumber);
                }

                continue;
            }

            if (value.IsBlank)
            {
                continue;
            }

            var number = ValueCoercion.ToNumber(value);
            if (number.IsError)
            {
                return number;
            }

            destination.Add(number.AsNumber);
        }

        return null;
    }
}

/// <summary>SUM: adds every number in its arguments.</summary>
public sealed class SumFunction() : FunctionBase("SUM", 1, int.MaxValue, "Adds its arguments.")
{
    /// <inheritdoc />
    public override CellValue Invoke(FunctionArguments arguments)
    {
        var numbers = new List<double>();
        if (Aggregation.CollectNumbers(arguments, numbers) is { } error)
        {
            return error;
        }

        var total = 0.0;
        foreach (var number in numbers)
        {
            total += number;
        }

        return ValueCoercion.FromDouble(total);
    }
}

/// <summary>AVERAGE: the arithmetic mean; <c>#DIV/0!</c> when there is nothing to average.</summary>
public sealed class AverageFunction() : FunctionBase("AVERAGE", 1, int.MaxValue, "Arithmetic mean of its arguments.")
{
    /// <inheritdoc />
    public override CellValue Invoke(FunctionArguments arguments)
    {
        var numbers = new List<double>();
        if (Aggregation.CollectNumbers(arguments, numbers) is { } error)
        {
            return error;
        }

        if (numbers.Count == 0)
        {
            return CellValue.FromError(
                ErrorValue.DivideByZero.WithDetail("AVERAGE was given no numbers to average"));
        }

        var total = 0.0;
        foreach (var number in numbers)
        {
            total += number;
        }

        return ValueCoercion.FromDouble(total / numbers.Count);
    }
}

/// <summary>MIN: the smallest number, or 0 when there is none (as in Excel).</summary>
public sealed class MinFunction() : FunctionBase("MIN", 1, int.MaxValue, "Smallest number in its arguments.")
{
    /// <inheritdoc />
    public override CellValue Invoke(FunctionArguments arguments) =>
        Extremum.Compute(arguments, takeSmaller: true);
}

/// <summary>MAX: the largest number, or 0 when there is none (as in Excel).</summary>
public sealed class MaxFunction() : FunctionBase("MAX", 1, int.MaxValue, "Largest number in its arguments.")
{
    /// <inheritdoc />
    public override CellValue Invoke(FunctionArguments arguments) =>
        Extremum.Compute(arguments, takeSmaller: false);
}

internal static class Extremum
{
    internal static CellValue Compute(FunctionArguments arguments, bool takeSmaller)
    {
        var numbers = new List<double>();
        if (Aggregation.CollectNumbers(arguments, numbers) is { } error)
        {
            return error;
        }

        if (numbers.Count == 0)
        {
            return CellValue.Zero;
        }

        var best = numbers[0];
        for (var i = 1; i < numbers.Count; i++)
        {
            if (takeSmaller ? numbers[i] < best : numbers[i] > best)
            {
                best = numbers[i];
            }
        }

        return ValueCoercion.FromDouble(best);
    }
}

/// <summary>COUNT: how many of its arguments are numbers.</summary>
public sealed class CountFunction() : FunctionBase("COUNT", 1, int.MaxValue, "Counts the numbers in its arguments.")
{
    /// <inheritdoc />
    public override CellValue Invoke(FunctionArguments arguments)
    {
        var numbers = new List<double>();
        if (Aggregation.CollectNumbers(arguments, numbers) is { } error)
        {
            return error;
        }

        return CellValue.Number(numbers.Count);
    }
}

/// <summary>COUNTA: how many of its arguments are not blank.</summary>
public sealed class CountAFunction() : FunctionBase("COUNTA", 1, int.MaxValue, "Counts the non-empty arguments.")
{
    /// <inheritdoc />
    public override CellValue Invoke(FunctionArguments arguments)
    {
        var count = 0;
        foreach (var argument in arguments.FlattenAll())
        {
            if (!argument.Value.IsBlank)
            {
                count++;
            }
        }

        return CellValue.Number(count);
    }
}

/// <summary>
/// COUNTIF: how many cells of a range satisfy a criterion.
/// </summary>
/// <remarks>
/// The criterion is either a value (<c>=COUNTIF(A1:A9,55)</c>) or a comparison
/// written as text (<c>=COUNTIF(A1:A9,"&gt;=60")</c>). It is also the formula
/// most often used to flag duplicates by hand —
/// <c>=IF(COUNTIF($A$2:$A$500,A2)&gt;1,"DUPLICATE","")</c> — which is the same
/// question the duplicate-detection feature answers for a whole range at once.
/// </remarks>
public sealed class CountIfFunction()
    : FunctionBase("COUNTIF", 2, 2, "Counts cells in a range that match a criterion.")
{
    /// <inheritdoc />
    public override CellValue Invoke(FunctionArguments arguments)
    {
        var view = arguments.AsRange(0);
        if (view is null)
        {
            return arguments.Invalid("the first argument must be a range");
        }

        var criterion = arguments.Value(1);
        if (criterion.IsError)
        {
            return criterion;
        }

        var (comparison, operand) = ParseCriterion(criterion);

        var count = 0;
        foreach (var value in view.NonBlankValues())
        {
            if (value.IsError)
            {
                // An error cell can only satisfy an equality test against the
                // same error; it never satisfies an ordering test.
                if (comparison == Comparison.Equal && operand.IsError && value.AsError.Equals(operand.AsError))
                {
                    count++;
                }

                continue;
            }

            if (Matches(comparison, ValueCoercion.Compare(value, operand)))
            {
                count++;
            }
        }

        return CellValue.Number(count);
    }

    private static bool Matches(Comparison comparison, int order) => comparison switch
    {
        Comparison.Equal => order == 0,
        Comparison.NotEqual => order != 0,
        Comparison.LessThan => order < 0,
        Comparison.LessThanOrEqual => order <= 0,
        Comparison.GreaterThan => order > 0,
        _ => order >= 0,
    };

    private static (Comparison Comparison, CellValue Operand) ParseCriterion(CellValue criterion)
    {
        if (!criterion.IsText)
        {
            return (Comparison.Equal, criterion);
        }

        var text = criterion.AsText;
        var (comparison, rest) = text switch
        {
            _ when text.StartsWith(">=", StringComparison.Ordinal) => (Comparison.GreaterThanOrEqual, text[2..]),
            _ when text.StartsWith("<=", StringComparison.Ordinal) => (Comparison.LessThanOrEqual, text[2..]),
            _ when text.StartsWith("<>", StringComparison.Ordinal) => (Comparison.NotEqual, text[2..]),
            _ when text.StartsWith(">", StringComparison.Ordinal) => (Comparison.GreaterThan, text[1..]),
            _ when text.StartsWith("<", StringComparison.Ordinal) => (Comparison.LessThan, text[1..]),
            _ when text.StartsWith("=", StringComparison.Ordinal) => (Comparison.Equal, text[1..]),
            _ => (Comparison.Equal, text),
        };

        var operand = ValueCoercion.TryParseNumber(rest, out var number)
            ? CellValue.Number(number)
            : CellValue.Text(rest);

        return (comparison, operand);
    }

    private enum Comparison
    {
        Equal,
        NotEqual,
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual,
    }
}
