using CalcEngine.Core.Evaluation;
using CalcEngine.Core.Model;

namespace CalcEngine.Core.Functions;

/// <summary>
/// IF: chooses between two expressions without evaluating the one it rejects.
/// </summary>
/// <remarks>
/// Laziness is the whole point. <c>=IF(A1=0,"n/a",B1/A1)</c> is the standard
/// guard against a missing denominator, and an eager IF would report
/// <c>#DIV/0!</c> for precisely the input the formula was written to handle.
/// This is why <see cref="IFunction"/> receives unevaluated arguments.
/// </remarks>
public sealed class IfFunction()
    : FunctionBase("IF", 2, 3, "Returns one value if a condition is TRUE and another if it is FALSE.")
{
    /// <inheritdoc />
    public override CellValue Invoke(FunctionArguments arguments)
    {
        var condition = arguments.Boolean(0);
        if (condition.IsError)
        {
            return condition;
        }

        if (condition.AsBoolean)
        {
            return arguments.Value(1);
        }

        return arguments.Count > 2 ? arguments.Value(2) : CellValue.False;
    }
}

/// <summary>IFERROR: substitutes a value when the first argument is an error.</summary>
public sealed class IfErrorFunction()
    : FunctionBase("IFERROR", 2, 2, "Returns a fallback value if an expression is an error.")
{
    /// <inheritdoc />
    public override CellValue Invoke(FunctionArguments arguments)
    {
        var value = arguments.Value(0);
        return value.IsError ? arguments.Value(1) : value;
    }
}

/// <summary>AND / OR: conjunction and disjunction over logical values.</summary>
public sealed class AndOrFunction : FunctionBase
{
    private readonly bool _requireAll;

    private AndOrFunction(string name, bool requireAll, string description)
        : base(name, 1, int.MaxValue, description) =>
        _requireAll = requireAll;

    /// <summary>AND.</summary>
    public static AndOrFunction And() => new("AND", true, "TRUE if every argument is TRUE.");

    /// <summary>OR.</summary>
    public static AndOrFunction Or() => new("OR", false, "TRUE if any argument is TRUE.");

    /// <inheritdoc />
    public override CellValue Invoke(FunctionArguments arguments)
    {
        var seen = 0;
        var result = _requireAll;

        foreach (var argument in arguments.FlattenAll())
        {
            var value = argument.Value;
            if (value.IsError)
            {
                return value;
            }

            if (argument.FromReference)
            {
                // Text and blanks inside references are data, not logic: skipped.
                if (!value.IsBoolean && !value.IsNumber)
                {
                    continue;
                }
            }
            else if (value.IsBlank)
            {
                continue;
            }

            var flag = ValueCoercion.ToBoolean(value);
            if (flag.IsError)
            {
                return flag;
            }

            seen++;
            result = _requireAll ? result && flag.AsBoolean : result || flag.AsBoolean;
        }

        return seen == 0
            ? arguments.Invalid("no logical values were supplied")
            : CellValue.Boolean(result);
    }
}
