using CalcEngine.Core.Evaluation;
using CalcEngine.Core.Model;

namespace CalcEngine.Core.Expressions;

/// <summary>The three unary operators of the formula language.</summary>
public enum UnaryOperator
{
    /// <summary>Prefix <c>+</c>; coerces to number and otherwise does nothing.</summary>
    Plus,

    /// <summary>Prefix <c>-</c>; negates.</summary>
    Minus,

    /// <summary>Postfix <c>%</c>; divides by 100.</summary>
    Percent,
}

/// <summary>The binary operators of the formula language.</summary>
public enum BinaryOperator
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Power,
    Concatenate,
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
}

/// <summary>Precedence and associativity facts shared by the parser, the printer and the tests.</summary>
public static class OperatorInfo
{
    /// <summary>
    /// Binding strength; <em>lower binds tighter</em>. The numbers are the rows
    /// of the precedence table in docs/grammar.md §4.
    /// </summary>
    public const int UnaryPrecedence = 1;

    /// <summary>Precedence of postfix <c>%</c>.</summary>
    public const int PercentPrecedence = 2;

    /// <summary>Precedence of an atom: binds tighter than everything.</summary>
    public const int AtomPrecedence = 0;

    /// <summary>The binding strength of a binary operator.</summary>
    public static int Precedence(BinaryOperator op) => op switch
    {
        BinaryOperator.Power => 3,
        BinaryOperator.Multiply or BinaryOperator.Divide => 4,
        BinaryOperator.Add or BinaryOperator.Subtract => 5,
        BinaryOperator.Concatenate => 6,
        _ => 7,
    };

    /// <summary><c>^</c> is the only right-associative operator.</summary>
    public static bool IsRightAssociative(BinaryOperator op) => op == BinaryOperator.Power;

    /// <summary>The symbol as it appears in a formula.</summary>
    public static string Symbol(BinaryOperator op) => op switch
    {
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        BinaryOperator.Power => "^",
        BinaryOperator.Concatenate => "&",
        BinaryOperator.Equal => "=",
        BinaryOperator.NotEqual => "<>",
        BinaryOperator.LessThan => "<",
        BinaryOperator.LessThanOrEqual => "<=",
        BinaryOperator.GreaterThan => ">",
        BinaryOperator.GreaterThanOrEqual => ">=",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown operator."),
    };

    /// <summary>The symbol of a unary operator.</summary>
    public static string Symbol(UnaryOperator op) => op switch
    {
        UnaryOperator.Plus => "+",
        UnaryOperator.Minus => "-",
        UnaryOperator.Percent => "%",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown operator."),
    };

    /// <summary>True for operators written after their operand.</summary>
    public static bool IsPostfix(UnaryOperator op) => op == UnaryOperator.Percent;

    /// <summary>True for the six comparison operators.</summary>
    public static bool IsComparison(BinaryOperator op) => Precedence(op) == 7;
}

/// <summary>A unary operator applied to one operand.</summary>
public sealed class UnaryOperatorExpression : Expression
{
    /// <summary>Creates a unary node.</summary>
    public UnaryOperatorExpression(UnaryOperator op, Expression operand)
    {
        Operator = op;
        Operand = operand ?? throw new ArgumentNullException(nameof(operand));
    }

    /// <summary>Which operator.</summary>
    public UnaryOperator Operator { get; }

    /// <summary>The single operand.</summary>
    public Expression Operand { get; }

    /// <summary>True when the operator is written after the operand (<c>%</c>).</summary>
    public bool IsPostfix => OperatorInfo.IsPostfix(Operator);

    /// <summary>Binding strength of this node; lower binds tighter.</summary>
    public int Precedence => IsPostfix ? OperatorInfo.PercentPrecedence : OperatorInfo.UnaryPrecedence;

    /// <inheritdoc />
    public override CellValue Evaluate(IEvaluationContext context)
    {
        var operand = ValueCoercion.ToNumber(Operand.Evaluate(context));
        if (operand.IsError)
        {
            return operand;
        }

        var number = operand.AsNumber;
        return Operator switch
        {
            UnaryOperator.Plus => ValueCoercion.FromDouble(number),
            UnaryOperator.Minus => ValueCoercion.FromDouble(-number),
            _ => ValueCoercion.FromDouble(number / 100.0),
        };
    }

    /// <inheritdoc />
    public override TResult Accept<TResult>(IExpressionVisitor<TResult> visitor) => visitor.VisitUnary(this);
}

/// <summary>A binary operator applied to two operands.</summary>
public sealed class BinaryOperatorExpression : Expression
{
    /// <summary>Creates a binary node.</summary>
    public BinaryOperatorExpression(BinaryOperator op, Expression left, Expression right)
    {
        Operator = op;
        Left = left ?? throw new ArgumentNullException(nameof(left));
        Right = right ?? throw new ArgumentNullException(nameof(right));
    }

    /// <summary>Which operator.</summary>
    public BinaryOperator Operator { get; }

    /// <summary>The left operand.</summary>
    public Expression Left { get; }

    /// <summary>The right operand.</summary>
    public Expression Right { get; }

    /// <summary>Binding strength of this node; lower binds tighter.</summary>
    public int Precedence => OperatorInfo.Precedence(Operator);

    /// <inheritdoc />
    public override CellValue Evaluate(IEvaluationContext context)
    {
        // Errors propagate left to right: the leftmost error is the one the user
        // sees, which is the one nearest the mistake they made.
        var left = Left.Evaluate(context);
        if (left.IsError)
        {
            return left;
        }

        var right = Right.Evaluate(context);
        if (right.IsError)
        {
            return right;
        }

        if (Operator == BinaryOperator.Concatenate)
        {
            var leftText = ValueCoercion.ToText(left);
            if (leftText.IsError)
            {
                return leftText;
            }

            var rightText = ValueCoercion.ToText(right);
            return rightText.IsError
                ? rightText
                : CellValue.Text(leftText.AsText + rightText.AsText);
        }

        if (OperatorInfo.IsComparison(Operator))
        {
            var order = ValueCoercion.Compare(left, right);
            return CellValue.Boolean(Operator switch
            {
                BinaryOperator.Equal => order == 0,
                BinaryOperator.NotEqual => order != 0,
                BinaryOperator.LessThan => order < 0,
                BinaryOperator.LessThanOrEqual => order <= 0,
                BinaryOperator.GreaterThan => order > 0,
                _ => order >= 0,
            });
        }

        var leftNumber = ValueCoercion.ToNumber(left);
        if (leftNumber.IsError)
        {
            return leftNumber;
        }

        var rightNumber = ValueCoercion.ToNumber(right);
        if (rightNumber.IsError)
        {
            return rightNumber;
        }

        var a = leftNumber.AsNumber;
        var b = rightNumber.AsNumber;
        return Operator switch
        {
            BinaryOperator.Add => ValueCoercion.FromDouble(a + b),
            BinaryOperator.Subtract => ValueCoercion.FromDouble(a - b),
            BinaryOperator.Multiply => ValueCoercion.FromDouble(a * b),
            BinaryOperator.Divide => b == 0
                ? CellValue.FromError(ErrorValue.DivideByZero)
                : ValueCoercion.FromDouble(a / b),
            BinaryOperator.Power => ValueCoercion.FromDouble(Math.Pow(a, b)),
            _ => CellValue.FromError(ErrorValue.Value.WithDetail($"unsupported operator {Operator}")),
        };
    }

    /// <inheritdoc />
    public override TResult Accept<TResult>(IExpressionVisitor<TResult> visitor) => visitor.VisitBinary(this);
}
