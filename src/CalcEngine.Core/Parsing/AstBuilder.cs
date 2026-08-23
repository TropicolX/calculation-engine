using System.Globalization;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using CalcEngine.Core.Expressions;
using CalcEngine.Core.Grammar.Generated;
using CalcEngine.Core.Model;

// The generated parser class is also called FormulaParser; the alias keeps the
// public facade in this namespace unambiguous.
using FormulaParserGenerated = CalcEngine.Core.Grammar.Generated.FormulaParser;

namespace CalcEngine.Core.Parsing;

/// <summary>
/// Lowers ANTLR's parse tree into the engine's own expression tree.
/// </summary>
/// <remarks>
/// <para>The two trees are deliberately different. ANTLR's tree mirrors the
/// grammar — it has a node for every rule, including the ones that exist only to
/// encode precedence, and a node for every bracket. The engine's tree is the
/// ADT: nine node types, no rule scaffolding, no brackets (structure encodes
/// them), and no dependency on ANTLR at all. Nothing outside this file and the
/// generated parser mentions <c>Antlr4.Runtime</c>, so the parser generator can
/// be swapped without touching the evaluator, the dependency graph or the
/// features.</para>
///
/// <para>This is also where <em>semantic</em> validation of references happens:
/// the lexer accepts <c>ZZZZ9</c> as a cell reference because it is
/// letters-then-digits, and it is here that it becomes the message "'ZZZZ' is
/// beyond the last column, XFD".</para>
/// </remarks>
internal sealed class AstBuilder : FormulaBaseVisitor<Expression>
{
    private readonly List<FormulaSyntaxError> _errors = [];

    internal IReadOnlyList<FormulaSyntaxError> Errors => _errors;

    /// <inheritdoc />
    public override Expression VisitFormula(FormulaParserGenerated.FormulaContext context) =>
        Visit(context.expression());

    /// <inheritdoc />
    public override Expression VisitAtomExpression(FormulaParserGenerated.AtomExpressionContext context) =>
        Visit(context.atom());

    /// <inheritdoc />
    public override Expression VisitParenthesisedExpression(
        FormulaParserGenerated.ParenthesisedExpressionContext context) =>
        // Brackets are not a node: the shape of the tree already records them.
        Visit(context.expression());

    /// <inheritdoc />
    public override Expression VisitNumberLiteral(FormulaParserGenerated.NumberLiteralContext context)
    {
        var text = context.NUMBER().GetText();
        var value = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : double.PositiveInfinity; // out of range for a double; the evaluator turns it into #NUM!
        return new NumberLiteralExpression(value) { Span = SpanOf(context) };
    }

    /// <inheritdoc />
    public override Expression VisitTextLiteral(FormulaParserGenerated.TextLiteralContext context)
    {
        var raw = context.STRING().GetText();
        var value = raw[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
        return new TextLiteralExpression(value) { Span = SpanOf(context) };
    }

    /// <inheritdoc />
    public override Expression VisitBooleanLiteral(FormulaParserGenerated.BooleanLiteralContext context) =>
        new BooleanLiteralExpression(context.TRUE() is not null) { Span = SpanOf(context) };

    /// <inheritdoc />
    public override Expression VisitErrorLiteral(FormulaParserGenerated.ErrorLiteralContext context)
    {
        ErrorValue.TryParse(context.ERROR_LITERAL().GetText(), out var error);
        return new ErrorLiteralExpression(error) { Span = SpanOf(context) };
    }

    /// <inheritdoc />
    public override Expression VisitFunctionCallAtom(FormulaParserGenerated.FunctionCallAtomContext context) =>
        Visit(context.functionCall());

    /// <inheritdoc />
    public override Expression VisitReferenceAtom(FormulaParserGenerated.ReferenceAtomContext context) =>
        Visit(context.reference());

    /// <inheritdoc />
    public override Expression VisitFunctionCall(FormulaParserGenerated.FunctionCallContext context)
    {
        var arguments = new List<Expression>();
        var list = context.argumentList();
        if (list is not null)
        {
            foreach (var argument in list.expression())
            {
                arguments.Add(Visit(argument));
            }
        }

        return new FunctionCallExpression(context.functionName().GetText(), arguments)
        {
            Span = SpanOf(context),
        };
    }

    /// <inheritdoc />
    public override Expression VisitCellReference(FormulaParserGenerated.CellReferenceContext context)
    {
        var reference = ReadReference(context.CELL_REF());
        return new CellReferenceExpression(ReadSheetName(context.SHEET_QUALIFIER()), reference)
        {
            Span = SpanOf(context),
        };
    }

    /// <inheritdoc />
    public override Expression VisitRangeReference(FormulaParserGenerated.RangeReferenceContext context)
    {
        var corners = context.CELL_REF();
        return new RangeReferenceExpression(
            ReadSheetName(context.SHEET_QUALIFIER()),
            ReadReference(corners[0]),
            ReadReference(corners[1]))
        {
            Span = SpanOf(context),
        };
    }

    /// <inheritdoc />
    public override Expression VisitUnaryExpression(FormulaParserGenerated.UnaryExpressionContext context)
    {
        var op = context.op.Type == FormulaLexer.MINUS ? UnaryOperator.Minus : UnaryOperator.Plus;
        return new UnaryOperatorExpression(op, Visit(context.expression())) { Span = SpanOf(context) };
    }

    /// <inheritdoc />
    public override Expression VisitPercentExpression(FormulaParserGenerated.PercentExpressionContext context) =>
        new UnaryOperatorExpression(UnaryOperator.Percent, Visit(context.expression()))
        {
            Span = SpanOf(context),
        };

    /// <inheritdoc />
    public override Expression VisitPowerExpression(FormulaParserGenerated.PowerExpressionContext context) =>
        Binary(BinaryOperator.Power, context, context.expression(0), context.expression(1));

    /// <inheritdoc />
    public override Expression VisitMultiplicativeExpression(
        FormulaParserGenerated.MultiplicativeExpressionContext context) =>
        Binary(
            context.op.Type == FormulaLexer.STAR ? BinaryOperator.Multiply : BinaryOperator.Divide,
            context,
            context.expression(0),
            context.expression(1));

    /// <inheritdoc />
    public override Expression VisitAdditiveExpression(FormulaParserGenerated.AdditiveExpressionContext context) =>
        Binary(
            context.op.Type == FormulaLexer.PLUS ? BinaryOperator.Add : BinaryOperator.Subtract,
            context,
            context.expression(0),
            context.expression(1));

    /// <inheritdoc />
    public override Expression VisitConcatenationExpression(
        FormulaParserGenerated.ConcatenationExpressionContext context) =>
        Binary(BinaryOperator.Concatenate, context, context.expression(0), context.expression(1));

    /// <inheritdoc />
    public override Expression VisitComparisonExpression(
        FormulaParserGenerated.ComparisonExpressionContext context)
    {
        var op = context.op.Type switch
        {
            FormulaLexer.EQUAL => BinaryOperator.Equal,
            FormulaLexer.NOT_EQUAL => BinaryOperator.NotEqual,
            FormulaLexer.LESS => BinaryOperator.LessThan,
            FormulaLexer.LESS_EQUAL => BinaryOperator.LessThanOrEqual,
            FormulaLexer.GREATER => BinaryOperator.GreaterThan,
            _ => BinaryOperator.GreaterThanOrEqual,
        };

        return Binary(op, context, context.expression(0), context.expression(1));
    }

    private Expression Binary(
        BinaryOperator op,
        ParserRuleContext context,
        FormulaParserGenerated.ExpressionContext left,
        FormulaParserGenerated.ExpressionContext right) =>
        new BinaryOperatorExpression(op, Visit(left), Visit(right)) { Span = SpanOf(context) };

    private string? ReadSheetName(ITerminalNode? qualifier)
    {
        if (qualifier is null)
        {
            return null;
        }

        var text = qualifier.GetText();
        text = text[..^1]; // drop the trailing '!'
        if (text.Length >= 2 && text[0] == '\'' && text[^1] == '\'')
        {
            text = text[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        return text;
    }

    /// <summary>
    /// Reads a <c>CELL_REF</c> token, reporting the two ways a lexically valid
    /// reference can still be off the grid.
    /// </summary>
    private CellReference ReadReference(ITerminalNode node)
    {
        var token = node.Symbol;
        var text = token.Text;
        var i = 0;

        var columnAbsolute = text[i] == '$';
        if (columnAbsolute)
        {
            i++;
        }

        var letterStart = i;
        while (i < text.Length && char.IsAsciiLetter(text[i]))
        {
            i++;
        }

        var letters = text[letterStart..i];

        var rowAbsolute = i < text.Length && text[i] == '$';
        if (rowAbsolute)
        {
            i++;
        }

        var digits = text[i..];

        if (!CellAddress.TryParseColumnName(letters, out var column))
        {
            AddError(token, $"'{letters.ToUpperInvariant()}' is beyond the last column, XFD.");
            column = 1;
        }

        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var row) || row > CellAddress.MaxRow)
        {
            AddError(token, $"row {digits} is beyond the last row, {CellAddress.MaxRow}.");
            row = 1;
        }
        else if (row < 1)
        {
            AddError(token, "row 0 does not exist; rows are numbered from 1.");
            row = 1;
        }

        return new CellReference(new CellAddress(column, (int)row), columnAbsolute, rowAbsolute);
    }

    private void AddError(IToken token, string message) =>
        _errors.Add(new FormulaSyntaxError(
            token.StartIndex + 1,
            token.StopIndex - token.StartIndex + 1,
            message));

    private static SourceSpan SpanOf(ParserRuleContext context)
    {
        var start = context.Start.StartIndex;
        var stop = context.Stop?.StopIndex ?? (start - 1);
        return new SourceSpan(start, Math.Max(0, stop - start + 1));
    }
}
