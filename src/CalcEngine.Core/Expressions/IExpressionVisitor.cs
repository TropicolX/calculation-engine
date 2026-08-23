namespace CalcEngine.Core.Expressions;

/// <summary>
/// A visitor over the expression tree (the Visitor half of the Composite).
/// </summary>
/// <remarks>
/// Evaluation is <em>not</em> done through this interface: it is implemented as
/// a virtual <c>Evaluate</c> on the nodes themselves (the Interpreter pattern),
/// because it is the hot path and one virtual call beats the double dispatch of
/// a visitor. Everything else — printing, dependency extraction, rewriting,
/// statistics — is open-ended and belongs here, where a new traversal costs a
/// new class instead of a new method on nine node types.
/// </remarks>
/// <typeparam name="TResult">What the traversal produces.</typeparam>
public interface IExpressionVisitor<out TResult>
{
    TResult VisitNumber(NumberLiteralExpression node);

    TResult VisitText(TextLiteralExpression node);

    TResult VisitBoolean(BooleanLiteralExpression node);

    TResult VisitError(ErrorLiteralExpression node);

    TResult VisitCellReference(CellReferenceExpression node);

    TResult VisitRangeReference(RangeReferenceExpression node);

    TResult VisitUnary(UnaryOperatorExpression node);

    TResult VisitBinary(BinaryOperatorExpression node);

    TResult VisitFunctionCall(FunctionCallExpression node);
}
