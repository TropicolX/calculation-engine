using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using CalcEngine.Core.Grammar.Generated;

namespace CalcEngine.Core.Parsing;

/// <summary>
/// Turns ANTLR's diagnostics into sentences a spreadsheet user can act on.
/// </summary>
/// <remarks>
/// ANTLR's default message — <c>mismatched input ')' expecting {NUMBER, STRING,
/// TRUE, FALSE, ERROR_LITERAL, IDENTIFIER, CELL_REF, ...}</c> — is written for
/// the person who wrote the grammar. This listener reads the same expected-token
/// set and classifies it into the three things a user can be missing: a value,
/// an operator, or a bracket.
/// </remarks>
internal sealed class DescriptiveErrorListener : BaseErrorListener
{
    private static readonly int[] AtomStartTokens =
    [
        FormulaLexer.NUMBER, FormulaLexer.STRING, FormulaLexer.TRUE, FormulaLexer.FALSE,
        FormulaLexer.ERROR_LITERAL, FormulaLexer.IDENTIFIER, FormulaLexer.CELL_REF,
        FormulaLexer.SHEET_QUALIFIER, FormulaLexer.LPAREN, FormulaLexer.PLUS, FormulaLexer.MINUS,
    ];

    private readonly List<FormulaSyntaxError> _errors = [];
    private readonly IList<IToken> _tokens;
    private readonly string _text;

    internal DescriptiveErrorListener(IList<IToken> tokens, string text)
    {
        _tokens = tokens;
        _text = text;
    }

    internal IReadOnlyList<FormulaSyntaxError> Errors => _errors;

    /// <inheritdoc />
    public override void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        IToken offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e)
    {
        var column = Math.Min(offendingSymbol.StartIndex, _text.Length) + 1;
        var length = Math.Max(1, offendingSymbol.StopIndex - offendingSymbol.StartIndex + 1);
        if (offendingSymbol.Type == TokenConstants.EOF)
        {
            length = 1;
        }

        _errors.Add(new FormulaSyntaxError(column, length, Describe(recognizer, offendingSymbol, e)));
    }

    private string Describe(IRecognizer recognizer, IToken offending, RecognitionException? e)
    {
        var expectation = DescribeExpectation(Expected(recognizer, e));

        if (offending.Type == TokenConstants.EOF)
        {
            var previous = PreviousVisibleToken(offending);
            return previous is null
                ? $"{expectation} was expected."
                : $"{expectation} was expected after '{previous.Text}'.";
        }

        return $"{expectation} was expected, but '{offending.Text}' was found.";
    }

    private static IntervalSet? Expected(IRecognizer recognizer, RecognitionException? e)
    {
        try
        {
            if (recognizer is Parser parser)
            {
                return parser.GetExpectedTokens();
            }

            return e?.GetExpectedTokens();
        }
        catch (Exception)
        {
            // GetExpectedTokens throws if the recogniser is in an unusual state;
            // a vaguer message is better than a crashing parser.
            return null;
        }
    }

    private static string DescribeExpectation(IntervalSet? expected)
    {
        if (expected is null || expected.Count == 0)
        {
            return "a value, cell reference or function call";
        }

        foreach (var token in AtomStartTokens)
        {
            if (expected.Contains(token))
            {
                return "a value, cell reference or function call";
            }
        }

        if (expected.Contains(FormulaLexer.RPAREN))
        {
            return "a closing bracket ')'";
        }

        if (expected.Contains(FormulaLexer.COLON))
        {
            return "a cell reference to complete the range";
        }

        return "an operator";
    }

    /// <summary>
    /// The token the user typed immediately before the failure, used to phrase
    /// "... was expected after '+'".  Looked up in the token list the caller
    /// already lexed, so it costs nothing and is exact.
    /// </summary>
    private IToken? PreviousVisibleToken(IToken offending)
    {
        IToken? previous = null;
        foreach (var token in _tokens)
        {
            if (token.Type == TokenConstants.EOF || token.StartIndex >= offending.StartIndex)
            {
                break;
            }

            previous = token;
        }

        return previous;
    }

}
