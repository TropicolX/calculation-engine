using CalcEngine.Core.Model;

namespace CalcEngine.Core.Functions;

/// <summary>
/// One function of the formula library — the Strategy the evaluator selects by
/// name.
/// </summary>
/// <remarks>
/// A function receives its arguments <em>unevaluated</em>, wrapped in
/// <see cref="FunctionArguments"/>. That is what allows <c>IF</c> to skip the
/// branch it does not take and <c>IFERROR</c> to skip its fallback, and it is
/// why this interface does not simply take an <c>IReadOnlyList&lt;CellValue&gt;</c>.
/// </remarks>
public interface IFunction
{
    /// <summary>The name as written in a formula, upper case.</summary>
    string Name { get; }

    /// <summary>Fewest arguments accepted.</summary>
    int MinimumArguments { get; }

    /// <summary>Most arguments accepted; <see cref="int.MaxValue"/> for variadic functions.</summary>
    int MaximumArguments { get; }

    /// <summary>One line of help, shown by the GUI's formula bar.</summary>
    string Description { get; }

    /// <summary>Computes the function. Must return an error value rather than throwing.</summary>
    CellValue Invoke(FunctionArguments arguments);
}
