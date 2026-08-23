using CalcEngine.Core.Expressions;
using CalcEngine.Core.Model;
using CalcEngine.Core.Parsing;

namespace CalcEngine.Core.Tests.Parsing;

/// <summary>
/// Tests of the expression tree ADT itself: printing (the inverse of parsing)
/// and dependency extraction (docs/adt-specifications.md §5).
/// </summary>
public class ExpressionTreeTests
{
    private static Expression Parse(string formula)
    {
        var result = FormulaParser.Parse(formula);
        Assert.True(result.Success, string.Join("; ", result.Errors));
        return result.Expression!;
    }

    [Theory]
    [InlineData("=SUM(B2:B45)*0.3")]
    [InlineData("=IF(A1>=50,\"PASS\",\"FAIL\")")]
    [InlineData("=ROUND(AVERAGE(Marks!B2:B45),2)")]
    [InlineData("=-2^2")]
    [InlineData("=1+2*3")]
    [InlineData("=(1+2)*3")]
    [InlineData("=2^3^2")]
    [InlineData("=\"she said \"\"yes\"\"\"")]
    [InlineData("='CSC 322'!$B$2")]
    [InlineData("=50%*200")]
    [InlineData("=A1&\"-\"&B1")]
    public void Print_IsAnInverseOfParse(string formula)
    {
        // Printing must round-trip through the parser to the same tree: that is
        // what makes it safe for Find & Replace to rewrite a formula.
        var once = FormulaPrinter.Print(Parse(formula));
        var twice = FormulaPrinter.Print(Parse(once));

        Assert.Equal(once, twice);
        Assert.Equal(
            FormulaPrinter.PrintFullyParenthesised(Parse(formula)),
            FormulaPrinter.PrintFullyParenthesised(Parse(once)));
    }

    [Fact]
    public void Print_OmitsParenthesesThatArentNeeded()
    {
        Assert.Equal("=1+2*3", FormulaPrinter.Print(Parse("=1+(2*3)")));
        Assert.Equal("=(1+2)*3", FormulaPrinter.Print(Parse("=(1+2)*3")));
    }

    [Fact]
    public void References_AreCollectedAsCellsAndRanges()
    {
        var references = ReferenceCollector.Collect(Parse("=SUM(B2:B45)+A1-Marks!C3"));

        Assert.Equal(
            new[] { "A1" },
            references.Cells.Where(c => c.SheetName is null).Select(c => c.Reference.Address.ToA1()));
        Assert.Single(references.Ranges);
        Assert.Equal(CellRange.Parse("B2:B45"), references.Ranges[0].Range);
        Assert.Contains(references.Cells, c => c.SheetName == "Marks");
    }

    [Fact]
    public void References_AreDeduplicated()
    {
        var references = ReferenceCollector.Collect(Parse("=A1+A1+A1"));

        Assert.Single(references.Cells);
    }

    [Fact]
    public void References_OfALiteralOnlyFormulaAreEmpty()
    {
        var references = ReferenceCollector.Collect(Parse("=1+2"));

        Assert.Empty(references.Cells);
        Assert.Empty(references.Ranges);
    }

    [Fact]
    public void EveryNodeCarriesItsSourceSpan()
    {
        // Spans are what let Find & Replace edit inside a formula without
        // re-printing the parts the user did not touch.
        var expression = Parse("=SUM(B2:B45)*0.3");
        var call = Assert.IsType<BinaryOperatorExpression>(expression).Left;

        Assert.Equal(1, call.Span.Start);
        Assert.Equal(11, call.Span.Length);
        Assert.Equal("SUM(B2:B45)", "=SUM(B2:B45)*0.3".Substring(call.Span.Start, call.Span.Length));
    }

    [Fact]
    public void TextLiteralSpansCoverTheQuotes()
    {
        const string formula = "=IF(A1,\"yes\",\"no\")";
        var literals = TextLiteralFinder.FindAll(Parse(formula));

        Assert.Equal(2, literals.Count);
        Assert.Equal("\"yes\"", formula.Substring(literals[0].Span.Start, literals[0].Span.Length));
        Assert.Equal("\"no\"", formula.Substring(literals[1].Span.Start, literals[1].Span.Length));
    }

    [Fact]
    public void Visitor_ReachesEveryNode()
    {
        var counter = new NodeCountingVisitor();
        Parse("=IF(A1>50,SUM(B1:B9),0)").Accept(counter);

        // IF, >, A1, 50, SUM, B1:B9, 0
        Assert.Equal(7, counter.Count);
    }

    private sealed class NodeCountingVisitor : IExpressionVisitor<int>
    {
        public int Count { get; private set; }

        private int Tick() => ++Count;

        public int VisitNumber(NumberLiteralExpression node) => Tick();

        public int VisitText(TextLiteralExpression node) => Tick();

        public int VisitBoolean(BooleanLiteralExpression node) => Tick();

        public int VisitError(ErrorLiteralExpression node) => Tick();

        public int VisitCellReference(CellReferenceExpression node) => Tick();

        public int VisitRangeReference(RangeReferenceExpression node) => Tick();

        public int VisitUnary(UnaryOperatorExpression node)
        {
            Tick();
            node.Operand.Accept(this);
            return Count;
        }

        public int VisitBinary(BinaryOperatorExpression node)
        {
            Tick();
            node.Left.Accept(this);
            node.Right.Accept(this);
            return Count;
        }

        public int VisitFunctionCall(FunctionCallExpression node)
        {
            Tick();
            foreach (var argument in node.Arguments)
            {
                argument.Accept(this);
            }

            return Count;
        }
    }
}
