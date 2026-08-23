namespace CalcEngine.Core.Expressions;

/// <summary>
/// The half-open slice of the original formula text that produced a node.
/// </summary>
/// <remarks>
/// <para><b>Abstraction function.</b> AF(s) = the characters
/// <c>[s.Start, s.Start + s.Length)</c> of the formula the node was parsed
/// from.</para>
/// <para><b>Representation invariant.</b> <c>Start &gt;= 0 &amp;&amp; Length &gt;= 0</c>.</para>
/// <para>A span is <em>metadata</em>: it is not part of the abstract value of an
/// expression and takes no part in equality. It exists so that a rewrite (Find
/// &amp; Replace inside a formula) can splice new text into the user's original
/// spelling instead of re-printing the whole formula and losing their
/// layout.</para>
/// </remarks>
public readonly record struct SourceSpan(int Start, int Length)
{
    /// <summary>The empty span at offset 0, used by programmatically built trees.</summary>
    public static SourceSpan None => default;

    /// <summary>One past the last character covered.</summary>
    public int End => Start + Length;

    /// <summary>The 1-based column at which the span begins, for error messages.</summary>
    public int Column => Start + 1;
}
