namespace CalcEngine.Core.Parsing;

/// <summary>
/// One reason a formula was rejected: what is wrong, and where.
/// </summary>
/// <param name="Column">1-based column into the cell's content, counting the leading <c>=</c>.</param>
/// <param name="Length">How many characters the complaint covers; at least 1.</param>
/// <param name="Message">A sentence in the user's vocabulary, not the parser's.</param>
public sealed record FormulaSyntaxError(int Column, int Length, string Message)
{
    /// <summary>Renders as <c>Column 12: the bracket opened at column 5 was never closed.</c></summary>
    public override string ToString() => $"Column {Column}: {Message}";
}
