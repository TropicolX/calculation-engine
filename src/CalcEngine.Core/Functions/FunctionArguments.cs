using CalcEngine.Core.Evaluation;
using CalcEngine.Core.Expressions;
using CalcEngine.Core.Model;

namespace CalcEngine.Core.Functions;

/// <summary>One value reaching a function, and whether it was read through a reference.</summary>
/// <remarks>
/// The flag is not bookkeeping for its own sake. Excel treats the two
/// differently and departmental workbooks rely on it: <c>=SUM(A1:A9)</c> and
/// <c>=SUM(A1)</c> ignore a cell containing the word "absent", but
/// <c>=SUM("3")</c> is 3. Losing the distinction would silently change the
/// totals of every sheet that records absences as text.
/// </remarks>
/// <param name="Value">The value.</param>
/// <param name="FromReference">True when the value was read through a cell or range reference.</param>
public readonly record struct ArgumentValue(CellValue Value, bool FromReference);

/// <summary>
/// The argument list handed to a function: lazy, cached and range-aware.
/// </summary>
/// <remarks>
/// <para>Arguments are evaluated on first access and remembered, so a function
/// that reads the same argument twice pays for it once, and a function that
/// never reads an argument never evaluates it.</para>
/// </remarks>
public sealed class FunctionArguments
{
    private readonly IReadOnlyList<Expression> _expressions;
    private readonly CellValue[] _cache;
    private readonly bool[] _evaluated;

    internal FunctionArguments(string functionName, IReadOnlyList<Expression> expressions, IEvaluationContext context)
    {
        FunctionName = functionName;
        _expressions = expressions;
        Context = context;
        _cache = new CellValue[expressions.Count];
        _evaluated = new bool[expressions.Count];
    }

    /// <summary>The name under which the function was called, for diagnostics.</summary>
    public string FunctionName { get; }

    /// <summary>The context the call is being evaluated in.</summary>
    public IEvaluationContext Context { get; }

    /// <summary>How many arguments were supplied.</summary>
    public int Count => _expressions.Count;

    /// <summary>The unevaluated argument, for functions that need to look before they leap.</summary>
    public Expression Raw(int index) => _expressions[index];

    /// <summary>Evaluates the argument (once) and returns its value.</summary>
    public CellValue Value(int index)
    {
        if (!_evaluated[index])
        {
            _cache[index] = _expressions[index].Evaluate(Context);
            _evaluated[index] = true;
        }

        return _cache[index];
    }

    /// <summary>The argument coerced to a number, or the blocking error.</summary>
    public CellValue Number(int index) => ValueCoercion.ToNumber(Value(index));

    /// <summary>The argument coerced to text, or the blocking error.</summary>
    public CellValue Text(int index) => ValueCoercion.ToText(Value(index));

    /// <summary>The argument coerced to a truth value, or the blocking error.</summary>
    public CellValue Boolean(int index) => ValueCoercion.ToBoolean(Value(index));

    /// <summary>True when the argument is written as a range, such as <c>B2:B45</c>.</summary>
    public bool IsRange(int index) => _expressions[index] is RangeReferenceExpression;

    /// <summary>
    /// Opens the argument as a range view. Returns null when the argument is not
    /// a range, or when it names a sheet that does not exist.
    /// </summary>
    public IRangeView? AsRange(int index) =>
        _expressions[index] is RangeReferenceExpression range
            ? Context.GetRange(range.SheetName, range.Range)
            : null;

    /// <summary>
    /// Flattens one argument: a range yields its non-blank cells, anything else
    /// yields its single value. Ranges naming a missing sheet yield <c>#REF!</c>.
    /// </summary>
    public IEnumerable<ArgumentValue> Flatten(int index)
    {
        if (_expressions[index] is RangeReferenceExpression range)
        {
            var view = Context.GetRange(range.SheetName, range.Range);
            if (view is null)
            {
                yield return new ArgumentValue(
                    CellValue.FromError(
                        ErrorValue.Reference.WithDetail($"there is no sheet called '{range.SheetName}'")),
                    FromReference: true);
                yield break;
            }

            foreach (var value in view.NonBlankValues())
            {
                yield return new ArgumentValue(value, FromReference: true);
            }

            yield break;
        }

        yield return new ArgumentValue(Value(index), _expressions[index] is CellReferenceExpression);
    }

    /// <summary>Flattens every argument, in order.</summary>
    public IEnumerable<ArgumentValue> FlattenAll()
    {
        for (var i = 0; i < Count; i++)
        {
            foreach (var value in Flatten(i))
            {
                yield return value;
            }
        }
    }

    /// <summary>Builds the <c>#VALUE!</c> a function returns when its arguments make no sense.</summary>
    public CellValue Invalid(string reason) =>
        CellValue.FromError(ErrorValue.Value.WithDetail($"{FunctionName}: {reason}"));
}
