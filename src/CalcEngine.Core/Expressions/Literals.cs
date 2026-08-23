using CalcEngine.Core.Model;

namespace CalcEngine.Core.Expressions;

/// <summary>A numeric literal such as <c>0.3</c>.</summary>
public sealed class NumberLiteralExpression : Expression
{
    /// <summary>Creates a numeric literal.</summary>
    public NumberLiteralExpression(double value) => Value = value;

    /// <summary>The literal's value.</summary>
    public double Value { get; }

    /// <inheritdoc />
    public override TResult Accept<TResult>(IExpressionVisitor<TResult> visitor) => visitor.VisitNumber(this);
}

/// <summary>A text literal such as <c>"PASS"</c>, already unescaped.</summary>
public sealed class TextLiteralExpression : Expression
{
    /// <summary>Creates a text literal from its <em>unescaped</em> value.</summary>
    public TextLiteralExpression(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>The literal's value, with doubled quotes already collapsed.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override TResult Accept<TResult>(IExpressionVisitor<TResult> visitor) => visitor.VisitText(this);
}

/// <summary>A boolean literal, <c>TRUE</c> or <c>FALSE</c>.</summary>
public sealed class BooleanLiteralExpression : Expression
{
    /// <summary>Creates a boolean literal.</summary>
    public BooleanLiteralExpression(bool value) => Value = value;

    /// <summary>The literal's value.</summary>
    public bool Value { get; }

    /// <inheritdoc />
    public override TResult Accept<TResult>(IExpressionVisitor<TResult> visitor) => visitor.VisitBoolean(this);
}

/// <summary>An error literal such as <c>#N/A</c>, which evaluates to itself.</summary>
public sealed class ErrorLiteralExpression : Expression
{
    /// <summary>Creates an error literal.</summary>
    public ErrorLiteralExpression(ErrorValue value) => Value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>The error this literal denotes.</summary>
    public ErrorValue Value { get; }

    /// <inheritdoc />
    public override TResult Accept<TResult>(IExpressionVisitor<TResult> visitor) => visitor.VisitError(this);
}
