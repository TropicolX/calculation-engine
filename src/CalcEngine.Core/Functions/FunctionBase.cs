using CalcEngine.Core.Evaluation;
using CalcEngine.Core.Model;

namespace CalcEngine.Core.Functions;

/// <summary>Boilerplate shared by every library function.</summary>
public abstract class FunctionBase : IFunction
{
    /// <summary>Declares the function's name, arity and help text.</summary>
    protected FunctionBase(string name, int minimumArguments, int maximumArguments, string description)
    {
        Name = name;
        MinimumArguments = minimumArguments;
        MaximumArguments = maximumArguments;
        Description = description;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public int MinimumArguments { get; }

    /// <inheritdoc />
    public int MaximumArguments { get; }

    /// <inheritdoc />
    public string Description { get; }

    /// <inheritdoc />
    public abstract CellValue Invoke(FunctionArguments arguments);

    /// <inheritdoc />
    public override string ToString() => $"{Name} - {Description}";
}

/// <summary>
/// Adapter for the point-wise functions: those that take a fixed number of
/// scalars, coerce them, and compute.
/// </summary>
/// <remarks>
/// ABS, SQRT, LEN and their two dozen siblings differ only in one lambda.
/// Giving each its own class would be twenty files of ceremony around one line
/// of arithmetic; giving them all this adapter keeps the Strategy interface
/// intact — the registry still holds <see cref="IFunction"/>s and knows nothing
/// about how they were built — while the interesting functions (IF, LOOKUP,
/// the aggregates) remain real classes because they have real logic.
/// </remarks>
internal sealed class PointwiseFunction : FunctionBase
{
    private readonly Func<FunctionArguments, CellValue> _implementation;

    internal PointwiseFunction(
        string name,
        int minimumArguments,
        int maximumArguments,
        string description,
        Func<FunctionArguments, CellValue> implementation)
        : base(name, minimumArguments, maximumArguments, description) =>
        _implementation = implementation;

    /// <inheritdoc />
    public override CellValue Invoke(FunctionArguments arguments) => _implementation(arguments);

    /// <summary>Builds a one-number-in, value-out function.</summary>
    internal static IFunction Number1(string name, string description, Func<double, CellValue> body) =>
        new PointwiseFunction(name, 1, 1, description, arguments =>
        {
            var value = arguments.Number(0);
            return value.IsError ? value : body(value.AsNumber);
        });

    /// <summary>Builds a two-number-in, value-out function.</summary>
    internal static IFunction Number2(string name, string description, Func<double, double, CellValue> body) =>
        new PointwiseFunction(name, 2, 2, description, arguments =>
        {
            var first = arguments.Number(0);
            if (first.IsError)
            {
                return first;
            }

            var second = arguments.Number(1);
            return second.IsError ? second : body(first.AsNumber, second.AsNumber);
        });

    /// <summary>Builds a one-text-in, value-out function.</summary>
    internal static IFunction Text1(string name, string description, Func<string, CellValue> body) =>
        new PointwiseFunction(name, 1, 1, description, arguments =>
        {
            var value = arguments.Text(0);
            return value.IsError ? value : body(value.AsText);
        });

    /// <summary>Builds a text-and-number-in, value-out function.</summary>
    internal static IFunction TextAndNumber(
        string name,
        string description,
        Func<string, double, CellValue> body) =>
        new PointwiseFunction(name, 2, 2, description, arguments =>
        {
            var text = arguments.Text(0);
            if (text.IsError)
            {
                return text;
            }

            var count = arguments.Number(1);
            return count.IsError ? count : body(text.AsText, count.AsNumber);
        });

    /// <summary>Builds a predicate over the raw (uncoerced) first argument.</summary>
    internal static IFunction Predicate(string name, string description, Func<CellValue, bool> body) =>
        new PointwiseFunction(name, 1, 1, description, arguments => CellValue.Boolean(body(arguments.Value(0))));
}
