using CalcEngine.Core.Model;

namespace CalcEngine.Core.Tests.Evaluation;

/// <summary>
/// The eight functions the specification requires: SUM, AVERAGE, MIN, MAX,
/// COUNT, IF, ROUND and LOOKUP.
/// </summary>
public class RequiredFunctionTests
{
    private static TestSheet Marks() => new TestSheet()
        .WithColumn("B2", 70, 55, 40, 85, 62);

    [Fact]
    public void Sum_OverARange()
    {
        Eval.Number("=SUM(B2:B6)", 312, Marks());
    }

    [Fact]
    public void Sum_IgnoresTextAndBlanksInsideRanges()
    {
        var sheet = new TestSheet().WithColumn("A1", 1, "absent", null, true, 2);

        Eval.Number("=SUM(A1:A5)", 3, sheet);
    }

    [Fact]
    public void Sum_CoercesDirectScalarArguments()
    {
        // Excel's asymmetry: text inside a range is ignored, but text passed
        // directly is coerced.  =SUM("3") is 3; =SUM(A1) with A1="3" is 0.
        Eval.Number("=SUM(\"3\",TRUE,2)", 6);
    }

    [Fact]
    public void Sum_MixesRangesAndScalars()
    {
        Eval.Number("=SUM(B2:B6,100,B2)", 482, Marks());
    }

    [Fact]
    public void Sum_OfNothingIsZero() => Eval.Number("=SUM(A1:A9)", 0);

    [Fact]
    public void Average()
    {
        Eval.Number("=AVERAGE(B2:B6)", 62.4, Marks());
        Eval.Number("=AVERAGE(1,2,3,4)", 2.5);
    }

    [Fact]
    public void Average_OfNoNumbersIsDivideByZero() => Eval.Error("=AVERAGE(A1:A9)", ErrorKind.DivideByZero);

    [Fact]
    public void MinAndMax()
    {
        Eval.Number("=MIN(B2:B6)", 40, Marks());
        Eval.Number("=MAX(B2:B6)", 85, Marks());
        Eval.Number("=MIN(3,-2,7)", -2);
    }

    [Fact]
    public void MinAndMaxOfNothingAreZero()
    {
        Eval.Number("=MIN(A1:A9)", 0);
        Eval.Number("=MAX(A1:A9)", 0);
    }

    [Fact]
    public void Count_CountsNumbersOnly()
    {
        var sheet = new TestSheet().WithColumn("A1", 1, "text", null, true, 2);

        Eval.Number("=COUNT(A1:A5)", 2, sheet);
        Eval.Number("=COUNTA(A1:A5)", 4, sheet);
    }

    [Fact]
    public void If_ChoosesTheBranch()
    {
        var sheet = new TestSheet().With("A1", 72.0);

        Assert.Equal("PASS", Eval.Display("=IF(A1>=50,\"PASS\",\"FAIL\")", sheet));
        Assert.Equal("FAIL", Eval.Display("=IF(A1>=90,\"PASS\",\"FAIL\")", sheet));
    }

    [Fact]
    public void If_DoesNotEvaluateTheBranchItDoesNotTake()
    {
        // The point of laziness: this must not report #DIV/0!.
        var sheet = new TestSheet().With("A1", 0.0).With("B1", 10.0);

        Assert.Equal("n/a", Eval.Display("=IF(A1=0,\"n/a\",B1/A1)", sheet));
    }

    [Fact]
    public void If_WithoutAnElseBranchIsFalse()
    {
        Assert.Equal("FALSE", Eval.Display("=IF(1=2,\"yes\")"));
    }

    [Fact]
    public void If_PropagatesAnErrorInTheCondition()
    {
        Eval.Error("=IF(1/0,\"a\",\"b\")", ErrorKind.DivideByZero);
    }

    [Theory]
    [InlineData("=ROUND(2.4,0)", 2)]
    [InlineData("=ROUND(2.5,0)", 3)]        // half away from zero, as in Excel
    [InlineData("=ROUND(-2.5,0)", -3)]
    [InlineData("=ROUND(62.4567,2)", 62.46)]
    [InlineData("=ROUND(1234.5,-2)", 1200)]
    [InlineData("=ROUND(1250,-2)", 1300)]
    public void Round(string formula, double expected) => Eval.Number(formula, expected);

    [Fact]
    public void Lookup_FindsTheGradeBand()
    {
        // The canonical results-processing use: a grade band table.
        var sheet = new TestSheet()
            .WithColumn("E2", 0, 40, 45, 50, 60, 70)
            .WithColumn("F2", "F", "E", "D", "C", "B", "A");

        Assert.Equal("A", Eval.Display("=LOOKUP(85,E2:E7,F2:F7)", sheet));
        Assert.Equal("B", Eval.Display("=LOOKUP(62,E2:E7,F2:F7)", sheet));
        Assert.Equal("C", Eval.Display("=LOOKUP(50,E2:E7,F2:F7)", sheet));
        Assert.Equal("F", Eval.Display("=LOOKUP(12,E2:E7,F2:F7)", sheet));
    }

    [Fact]
    public void Lookup_BelowTheFirstEntryIsNotAvailable()
    {
        var sheet = new TestSheet().WithColumn("E2", 40, 50, 60).WithColumn("F2", "D", "C", "B");

        Eval.Error("=LOOKUP(10,E2:E4,F2:F4)", ErrorKind.NotAvailable, sheet);
    }

    [Fact]
    public void Lookup_TwoArgumentFormUsesTheLastColumnOfTheBlock()
    {
        var sheet = new TestSheet()
            .WithColumn("E2", 0, 40, 50)
            .WithColumn("F2", "F", "E", "C");

        Assert.Equal("C", Eval.Display("=LOOKUP(55,E2:F4)", sheet));
    }

    [Fact]
    public void Lookup_MatchesTextExactly()
    {
        var sheet = new TestSheet()
            .WithColumn("A1", "CSC322", "CSC323", "CSC324")
            .WithColumn("B1", 3, 2, 4);

        Eval.Number("=LOOKUP(\"CSC323\",A1:A3,B1:B3)", 2, sheet);
    }

    [Fact]
    public void UnknownFunctionIsANameError()
    {
        var value = Eval.Value("=NOSUCH(1)");

        Assert.Equal(ErrorKind.Name, value.AsError.Kind);
        Assert.Equal("there is no function called 'NOSUCH'", value.AsError.Detail);
    }

    [Fact]
    public void WrongArityIsAValueErrorThatSaysSo()
    {
        var value = Eval.Value("=ROUND(1)");

        Assert.Equal(ErrorKind.Value, value.AsError.Kind);
        Assert.Contains("ROUND", value.AsError.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkedExampleFromTheBrief()
    {
        // =SUM(B2:B45)*0.3 - a continuous-assessment column weighted at 30%.
        var sheet = new TestSheet();
        for (var row = 2; row <= 45; row++)
        {
            sheet[$"B{row}"] = CellValue.Number(10);
        }

        Eval.Number("=SUM(B2:B45)*0.3", 132, sheet);
    }
}
