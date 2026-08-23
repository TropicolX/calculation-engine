using CalcEngine.Core.Expressions;

namespace CalcEngine.Core.Parsing;

/// <summary>
/// The outcome of parsing one formula: either a tree, or the reasons there is
/// not one. Never an exception — malformed input is normal input.
/// </summary>
public sealed class ParseResult
{
    private ParseResult(Expression? expression, IReadOnlyList<FormulaSyntaxError> errors)
    {
        Expression = expression;
        Errors = errors;
    }

    /// <summary>The expression tree, or null when parsing failed.</summary>
    public Expression? Expression { get; }

    /// <summary>Every reason the formula was rejected, ordered by column. Empty on success.</summary>
    public IReadOnlyList<FormulaSyntaxError> Errors { get; }

    /// <summary>True when a tree was produced.</summary>
    public bool Success => Expression is not null;

    internal static ParseResult Ok(Expression expression) => new(expression, []);

    internal static ParseResult Failed(IReadOnlyList<FormulaSyntaxError> errors)
    {
        if (errors.Count == 0)
        {
            throw new ArgumentException("A failed parse must report at least one error.", nameof(errors));
        }

        return new ParseResult(null, errors);
    }
}
