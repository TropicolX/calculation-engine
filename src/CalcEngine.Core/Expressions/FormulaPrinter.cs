using System.Globalization;
using System.Text;
using CalcEngine.Core.Model;

namespace CalcEngine.Core.Expressions;

/// <summary>
/// Renders an expression tree back to formula text — the inverse of parsing.
/// </summary>
/// <remarks>
/// <para>Two modes. <see cref="Print(Expression)"/> emits the <em>minimal</em>
/// text: a bracket appears only where removing it would change the tree. That
/// property is what makes the printer safe for Find &amp; Replace to write a
/// rewritten formula back into a cell — <c>ExpressionTreeTests.Print_IsAnInverseOfParse</c>
/// checks it by re-parsing the output and comparing trees.</para>
///
/// <para><see cref="PrintFullyParenthesised(Expression)"/> brackets every
/// operator. Nobody wants to read that in a formula bar, but it makes tree shape
/// visible in a single string, which is how the precedence tests assert on
/// structure without walking the tree by hand.</para>
/// </remarks>
public sealed class FormulaPrinter : IExpressionVisitor<string>
{
    private readonly bool _explicitBrackets;

    private FormulaPrinter(bool explicitBrackets) => _explicitBrackets = explicitBrackets;

    /// <summary>Renders a whole formula, leading <c>=</c> included, with minimal brackets.</summary>
    public static string Print(Expression expression) => "=" + PrintExpression(expression);

    /// <summary>Renders an expression without the leading <c>=</c>.</summary>
    public static string PrintExpression(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return expression.Accept(new FormulaPrinter(explicitBrackets: false));
    }

    /// <summary>Renders a whole formula with every operator bracketed, for diagnostics and tests.</summary>
    public static string PrintFullyParenthesised(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return "=" + expression.Accept(new FormulaPrinter(explicitBrackets: true));
    }

    /// <inheritdoc />
    public string VisitNumber(NumberLiteralExpression node) =>
        node.Value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public string VisitText(TextLiteralExpression node) => Quote(node.Value);

    /// <inheritdoc />
    public string VisitBoolean(BooleanLiteralExpression node) => node.Value ? "TRUE" : "FALSE";

    /// <inheritdoc />
    public string VisitError(ErrorLiteralExpression node) => node.Value.Code;

    /// <inheritdoc />
    public string VisitCellReference(CellReferenceExpression node) =>
        SheetPrefix(node.SheetName) + node.Reference.ToA1();

    /// <inheritdoc />
    public string VisitRangeReference(RangeReferenceExpression node) =>
        $"{SheetPrefix(node.SheetName)}{node.From.ToA1()}:{node.To.ToA1()}";

    /// <inheritdoc />
    public string VisitUnary(UnaryOperatorExpression node)
    {
        var operand = Wrap(node.Operand, node.Precedence, isRightOperand: !node.IsPostfix);
        var symbol = OperatorInfo.Symbol(node.Operator);
        var text = node.IsPostfix ? operand + symbol : symbol + operand;
        return _explicitBrackets ? "(" + text + ")" : text;
    }

    /// <inheritdoc />
    public string VisitBinary(BinaryOperatorExpression node)
    {
        var left = Wrap(node.Left, node.Precedence, isRightOperand: false, node.Operator);
        var right = Wrap(node.Right, node.Precedence, isRightOperand: true, node.Operator);
        var symbol = OperatorInfo.Symbol(node.Operator);
        return _explicitBrackets
            ? $"({left} {symbol} {right})"
            : left + symbol + right;
    }

    /// <inheritdoc />
    public string VisitFunctionCall(FunctionCallExpression node)
    {
        var separator = _explicitBrackets ? ", " : ",";
        var builder = new StringBuilder(node.Name).Append('(');
        for (var i = 0; i < node.Arguments.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(separator);
            }

            builder.Append(node.Arguments[i].Accept(this));
        }

        return builder.Append(')').ToString();
    }

    /// <summary>Binding strength of a node; atoms bind tightest.</summary>
    public static int PrecedenceOf(Expression expression) => expression switch
    {
        BinaryOperatorExpression binary => binary.Precedence,
        UnaryOperatorExpression unary => unary.Precedence,
        _ => OperatorInfo.AtomPrecedence,
    };

    private string Wrap(Expression child, int parentPrecedence, bool isRightOperand, BinaryOperator? parent = null)
    {
        var text = child.Accept(this);
        if (_explicitBrackets)
        {
            // Operator nodes bracket themselves in this mode; atoms need nothing.
            return text;
        }

        var childPrecedence = PrecedenceOf(child);
        if (childPrecedence > parentPrecedence)
        {
            return "(" + text + ")";
        }

        if (childPrecedence == parentPrecedence && parent is { } op)
        {
            // Same strength: the side that associativity does *not* favour needs
            // brackets, so that 10-(4-3) never prints as 10-4-3.
            var needsBrackets = OperatorInfo.IsRightAssociative(op) ? !isRightOperand : isRightOperand;
            if (needsBrackets)
            {
                return "(" + text + ")";
            }
        }

        return text;
    }

    private static string SheetPrefix(string? sheetName) =>
        sheetName is null ? string.Empty : SheetCellAddress.QuoteSheetName(sheetName) + "!";

    private static string Quote(string value) =>
        "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
