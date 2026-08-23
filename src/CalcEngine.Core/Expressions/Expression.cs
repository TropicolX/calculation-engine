using CalcEngine.Core.Evaluation;
using CalcEngine.Core.Model;

namespace CalcEngine.Core.Expressions;

/// <summary>
/// A node of the expression tree: the Composite at the heart of the engine.
/// </summary>
/// <remarks>
/// <para><b>Abstraction function.</b> An <see cref="Expression"/> denotes a
/// function from an evaluation context (the values of the cells it reads and
/// the function library in force) to a <see cref="Model.CellValue"/>. Leaves —
/// literals and references — denote constant or one-cell lookups; the composite
/// nodes denote the operator or function applied to the denotations of their
/// children.</para>
///
/// <para><b>Representation invariant.</b> The tree is finite and acyclic; every
/// child reference is non-null; a <see cref="FunctionCallExpression"/> has a
/// non-empty, upper-cased name; the arity of an operator node matches its
/// operator. Immutability enforces acyclicity: a node's children are fixed at
/// construction, so no node can ever become its own descendant.</para>
///
/// <para>Nodes are immutable and freely shareable. That matters for undo: a
/// command can keep a reference to the previous tree without copying it.</para>
/// </remarks>
public abstract class Expression
{
    /// <summary>Where in the original formula text this node came from. Not part of the value.</summary>
    public SourceSpan Span { get; init; }

    /// <summary>
    /// Computes this node's value in <paramref name="context"/>.
    /// </summary>
    /// <remarks>
    /// This is the Interpreter pattern: every node knows how to evaluate
    /// itself, so evaluation is one virtual call per node rather than the two
    /// of a visitor's double dispatch.  On the hot path — a full recalculation
    /// walks every tree in the workbook — that difference is the reason
    /// evaluation lives here and every other traversal lives in
    /// <see cref="IExpressionVisitor{TResult}"/>.
    ///
    /// Implementations must never throw for bad data: type confusion, missing
    /// references and division by zero are returned as error values.
    /// </remarks>
    public abstract CellValue Evaluate(IEvaluationContext context);

    /// <summary>Double-dispatches to the matching method of <paramref name="visitor"/>.</summary>
    public abstract TResult Accept<TResult>(IExpressionVisitor<TResult> visitor);

    /// <summary>Renders the node as formula text (without the leading <c>=</c>).</summary>
    public override string ToString() => FormulaPrinter.PrintExpression(this);
}
