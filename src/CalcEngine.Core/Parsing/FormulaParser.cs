using Antlr4.Runtime;
using CalcEngine.Core.Expressions;
using CalcEngine.Core.Grammar.Generated;
using FormulaParserGenerated = CalcEngine.Core.Grammar.Generated.FormulaParser;

namespace CalcEngine.Core.Parsing;

/// <summary>
/// The engine's front door for formula text: string in, expression tree or
/// error list out.
/// </summary>
/// <remarks>
/// <para>The pipeline is: lex → pre-validate the flat token list → parse →
/// lower to the ADT. Each stage can stop the pipeline with a list of errors,
/// and no stage throws for bad input.</para>
///
/// <para>Parsing is deliberately independent of the function library: a call to
/// a function that does not exist is a well-formed formula that evaluates to
/// <c>#NAME?</c>, exactly as in Excel. That keeps the parser free of any
/// dependency on the engine's configuration, so the same tree can be evaluated
/// by two workbooks with different function sets.</para>
/// </remarks>
public static class FormulaParser
{
    /// <summary>
    /// Parses cell content that has already been classified as a formula.
    /// </summary>
    /// <param name="formulaText">Cell content beginning with <c>=</c>.</param>
    /// <returns>A tree, or the reasons there is not one.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="formulaText"/> does not begin with <c>=</c>. Deciding
    /// what is a formula is the classifier's job, not the parser's; calling the
    /// parser with a student's name is a bug in the caller.
    /// </exception>
    public static ParseResult Parse(string formulaText)
    {
        ArgumentNullException.ThrowIfNull(formulaText);
        if (formulaText.Length == 0 || formulaText[0] != '=')
        {
            throw new ArgumentException(
                "Formula text must begin with '='. Use CellContentClassifier for arbitrary cell content.",
                nameof(formulaText));
        }

        var lexer = new FormulaLexer(new AntlrInputStream(formulaText));
        lexer.RemoveErrorListeners(); // every character is a token; nothing for the lexer to report
        var tokenStream = new CommonTokenStream(lexer);
        tokenStream.Fill();
        var tokens = tokenStream.GetTokens();

        var lexicalErrors = TokenPreValidator.Validate(tokens, formulaText);
        if (lexicalErrors.Count > 0)
        {
            return ParseResult.Failed(Ordered(lexicalErrors));
        }

        tokenStream.Seek(0);
        var parser = new FormulaParserGenerated(tokenStream);
        var listener = new DescriptiveErrorListener(tokens, formulaText);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(listener);

        var parseTree = parser.formula();
        if (listener.Errors.Count > 0)
        {
            return ParseResult.Failed(Ordered(listener.Errors));
        }

        var builder = new AstBuilder();
        var expression = builder.Visit(parseTree);
        if (builder.Errors.Count > 0)
        {
            return ParseResult.Failed(Ordered(builder.Errors));
        }

        return ParseResult.Ok(expression);
    }

    /// <summary>
    /// Parses a formula, returning null instead of a result object.
    /// A convenience for callers that have already validated the text.
    /// </summary>
    public static Expression? ParseOrNull(string formulaText) => Parse(formulaText).Expression;

    private static IReadOnlyList<FormulaSyntaxError> Ordered(IReadOnlyList<FormulaSyntaxError> errors)
    {
        var ordered = errors.ToList();
        ordered.Sort(static (a, b) => a.Column.CompareTo(b.Column));
        return ordered;
    }
}
