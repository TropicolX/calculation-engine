using Antlr4.Runtime;
using CalcEngine.Core.Grammar.Generated;

namespace CalcEngine.Core.Parsing;

/// <summary>
/// A pass over the token stream that runs before the parser and catches the
/// three mistakes people actually make.
/// </summary>
/// <remarks>
/// <para>Stray characters, unterminated text and unbalanced brackets are the
/// overwhelming majority of real typing errors, and they are exactly the
/// mistakes a recursive-descent parser describes worst: ANTLR's recovery turns
/// one missing <c>)</c> into a cascade of "mismatched input" complaints pointing
/// at the wrong place.</para>
///
/// <para>Checking them first, on the flat token list, lets the engine say
/// <em>"the bracket opened at column 5 was never closed"</em> — naming the
/// opening bracket, which is the thing the user has to go and fix — and lets
/// the parser be responsible only for grammatical structure. It costs one
/// linear scan of a token list that has already been produced.</para>
/// </remarks>
internal static class TokenPreValidator
{
    internal static IReadOnlyList<FormulaSyntaxError> Validate(IList<IToken> tokens, string text)
    {
        var errors = new List<FormulaSyntaxError>();
        var openBrackets = new Stack<IToken>();

        foreach (var token in tokens)
        {
            switch (token.Type)
            {
                case FormulaLexer.UNEXPECTED_CHAR:
                    errors.Add(new FormulaSyntaxError(
                        token.StartIndex + 1,
                        1,
                        $"'{token.Text}' is not a valid character in a formula."));
                    break;

                case FormulaLexer.UNTERMINATED_STRING:
                    errors.Add(new FormulaSyntaxError(
                        token.StartIndex + 1,
                        token.StopIndex - token.StartIndex + 1,
                        "this text literal has no closing quotation mark."));
                    break;

                case FormulaLexer.LPAREN:
                    openBrackets.Push(token);
                    break;

                case FormulaLexer.RPAREN:
                    if (openBrackets.Count == 0)
                    {
                        errors.Add(new FormulaSyntaxError(
                            token.StartIndex + 1,
                            1,
                            "there is no opening bracket to match this ')'."));
                    }
                    else
                    {
                        openBrackets.Pop();
                    }

                    break;
            }
        }

        // Report the *innermost* unclosed bracket first: it is the one whose
        // missing partner the user has to type.
        while (openBrackets.Count > 0)
        {
            var open = openBrackets.Pop();
            errors.Add(new FormulaSyntaxError(
                text.Length + 1,
                1,
                $"the bracket opened at column {open.StartIndex + 1} was never closed."));
        }

        if (errors.Count == 0 && tokens.Count(t => t.Channel == TokenConstants.DefaultChannel) <= 2)
        {
            // Only '=' and EOF: there is nothing to compute.
            errors.Add(new FormulaSyntaxError(
                text.Length + 1,
                1,
                "a formula must contain an expression after the '='."));
        }

        return errors;
    }
}
