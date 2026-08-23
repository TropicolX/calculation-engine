namespace CalcEngine.Core.Expressions;

/// <summary>A call of a library function, for example <c>SUM(B2:B45)</c>.</summary>
/// <remarks>
/// The node stores the arguments <em>unevaluated</em>. That is not an
/// oversight: <c>IF</c> must not evaluate the branch it does not take, or
/// <c>=IF(A1=0,"n/a",B1/A1)</c> would report <c>#DIV/0!</c> for exactly the
/// input it was written to guard against. Functions receive an argument list
/// that evaluates lazily and caches (see <c>FunctionArguments</c>).
/// </remarks>
public sealed class FunctionCallExpression : Expression
{
    /// <summary>Creates a function call node.</summary>
    /// <param name="name">The function name; stored upper-cased for lookup.</param>
    /// <param name="arguments">The unevaluated arguments, in source order.</param>
    public FunctionCallExpression(string name, IReadOnlyList<Expression> arguments)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A function name is required.", nameof(name));
        }

        Name = name.ToUpperInvariant();
        Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
    }

    /// <summary>The function name, upper-cased.</summary>
    public string Name { get; }

    /// <summary>The unevaluated arguments, in source order.</summary>
    public IReadOnlyList<Expression> Arguments { get; }

    /// <inheritdoc />
    public override TResult Accept<TResult>(IExpressionVisitor<TResult> visitor) => visitor.VisitFunctionCall(this);
}
