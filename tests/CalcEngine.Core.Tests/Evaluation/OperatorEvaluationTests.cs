using CalcEngine.Core.Model;

namespace CalcEngine.Core.Tests.Evaluation;

/// <summary>Arithmetic, comparison, concatenation and the coercions between them.</summary>
public class OperatorEvaluationTests
{
    [Theory]
    [InlineData("=1+2", 3)]
    [InlineData("=10-4-3", 3)]
    [InlineData("=6*7", 42)]
    [InlineData("=100/8", 12.5)]
    [InlineData("=2^10", 1024)]
    [InlineData("=2^3^2", 512)]      // right associative: 2^(3^2)
    [InlineData("=-2^2", 4)]         // Excel precedence: (-2)^2
    [InlineData("=1+2*3", 7)]
    [InlineData("=(1+2)*3", 9)]
    [InlineData("=50%", 0.5)]
    [InlineData("=200*15%", 30)]
    [InlineData("=--5", 5)]
    public void Arithmetic(string formula, double expected) => Eval.Number(formula, expected);

    [Fact]
    public void DivisionByZero_IsAnErrorValueNotAnException()
    {
        Eval.Error("=1/0", ErrorKind.DivideByZero);
        Eval.Error("=1/(2-2)", ErrorKind.DivideByZero);
        Eval.Error("=0/0", ErrorKind.DivideByZero);
    }

    [Fact]
    public void OverflowAndUndefinedResultsBecomeNumberErrors()
    {
        Eval.Error("=1e308*10", ErrorKind.Number);
        Eval.Error("=(-8)^0.5", ErrorKind.Number);
    }

    [Theory]
    [InlineData("=\"Ngozi\"&\" \"&\"Okafor\"", "Ngozi Okafor")]
    [InlineData("=\"Total: \"&42", "Total: 42")]
    [InlineData("=TRUE&\"\"", "TRUE")]
    public void Concatenation(string formula, string expected) => Assert.Equal(expected, Eval.Display(formula));

    [Theory]
    [InlineData("=1=1", true)]
    [InlineData("=1=2", false)]
    [InlineData("=1<>2", true)]
    [InlineData("=2>1", true)]
    [InlineData("=2>=2", true)]
    [InlineData("=1<2", true)]
    [InlineData("=1<=0", false)]
    [InlineData("=\"abc\"=\"ABC\"", true)]     // text comparison is case-insensitive, as in Excel
    [InlineData("=\"abc\"<\"abd\"", true)]
    [InlineData("=1<\"a\"", true)]             // numbers sort before text
    [InlineData("=\"a\"<TRUE", true)]          // text sorts before booleans
    public void Comparison(string formula, bool expected) =>
        Assert.Equal(expected, Eval.Value(formula).AsBoolean);

    [Fact]
    public void BlankCoercesToZeroInArithmeticAndEmptyTextInConcatenation()
    {
        var sheet = new TestSheet();

        Eval.Number("=A1+5", 5, sheet);
        Assert.Equal("x", Eval.Display("=A1&\"x\"", sheet));
        Assert.True(Eval.Value("=A1=0", sheet).AsBoolean);
        Assert.True(Eval.Value("=A1=\"\"", sheet).AsBoolean);
    }

    [Fact]
    public void BooleansCoerceToOneAndZero()
    {
        Eval.Number("=TRUE+TRUE", 2);
        Eval.Number("=FALSE*10", 0);
    }

    [Fact]
    public void NumericTextCoercesButRealTextDoesNot()
    {
        Eval.Number("=\"3\"+4", 7);
        Eval.Number("=\"1.5e2\"*2", 300);
        Eval.Error("=\"three\"+4", ErrorKind.Value);
    }

    [Fact]
    public void ErrorsPropagateThroughEveryOperator()
    {
        var sheet = new TestSheet().WithError("A1", ErrorValue.NotAvailable);

        Eval.Error("=A1+1", ErrorKind.NotAvailable, sheet);
        Eval.Error("=1+A1", ErrorKind.NotAvailable, sheet);
        Eval.Error("=A1&\"x\"", ErrorKind.NotAvailable, sheet);
        Eval.Error("=A1>1", ErrorKind.NotAvailable, sheet);
        Eval.Error("=-A1", ErrorKind.NotAvailable, sheet);
        Eval.Error("=A1%", ErrorKind.NotAvailable, sheet);
    }

    [Fact]
    public void TheLeftmostErrorWins()
    {
        var sheet = new TestSheet()
            .WithError("A1", ErrorValue.DivideByZero)
            .WithError("B1", ErrorValue.NotAvailable);

        Eval.Error("=A1+B1", ErrorKind.DivideByZero, sheet);
        Eval.Error("=B1+A1", ErrorKind.NotAvailable, sheet);
    }

    [Fact]
    public void CellReferencesReadTheContext()
    {
        var sheet = new TestSheet().With("B2", 72.5).With("B3", "Ngozi");

        Eval.Number("=B2", 72.5, sheet);
        Eval.Number("=$B$2*2", 145, sheet);
        Assert.Equal("Ngozi", Eval.Display("=B3", sheet));
    }

    [Fact]
    public void AReferenceToAnUnknownSheetIsAReferenceError()
    {
        Eval.Error("=Missing!A1", ErrorKind.Reference);
    }

    [Fact]
    public void ARangeUsedWhereASingleValueIsExpectedIsAValueError()
    {
        // Excel would attempt implicit intersection; we reject explicitly rather
        // than guess which row the user meant.  docs/adt-specifications.md §6.
        Eval.Error("=A1:A5+1", ErrorKind.Value);
    }

    [Fact]
    public void ErrorLiteralsEvaluateToThemselves()
    {
        Eval.Error("=#N/A", ErrorKind.NotAvailable);
        Eval.Error("=#REF!", ErrorKind.Reference);
    }
}
