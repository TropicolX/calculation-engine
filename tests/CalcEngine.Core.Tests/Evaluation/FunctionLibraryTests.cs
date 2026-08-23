using CalcEngine.Core.Evaluation;
using CalcEngine.Core.Functions;
using CalcEngine.Core.Model;

namespace CalcEngine.Core.Tests.Evaluation;

/// <summary>
/// The supporting library, and the registry that makes the library pluggable
/// (Strategy behind a Factory - docs/design-portfolio.md §5.4).
/// </summary>
public class FunctionLibraryTests
{
    [Theory]
    [InlineData("=ABS(-7)", 7)]
    [InlineData("=SQRT(81)", 9)]
    [InlineData("=MOD(17,5)", 2)]
    [InlineData("=POWER(2,8)", 256)]
    [InlineData("=INT(7.9)", 7)]
    [InlineData("=INT(-7.1)", -8)]
    [InlineData("=ROUNDUP(2.01,0)", 3)]
    [InlineData("=ROUNDDOWN(2.99,0)", 2)]
    public void Maths(string formula, double expected) => Eval.Number(formula, expected);

    [Fact]
    public void MathematicalDomainErrors()
    {
        Eval.Error("=SQRT(-1)", ErrorKind.Number);
        Eval.Error("=MOD(1,0)", ErrorKind.DivideByZero);
    }

    [Theory]
    [InlineData("=LEN(\"Ngozi\")", 5)]
    [InlineData("=LEN(\"\")", 0)]
    public void TextLength(string formula, double expected) => Eval.Number(formula, expected);

    [Theory]
    [InlineData("=UPPER(\"ngozi\")", "NGOZI")]
    [InlineData("=LOWER(\"NGOZI\")", "ngozi")]
    [InlineData("=TRIM(\"  Ngozi  Okafor \")", "Ngozi Okafor")]
    [InlineData("=LEFT(\"CSC322\",3)", "CSC")]
    [InlineData("=RIGHT(\"CSC322\",3)", "322")]
    [InlineData("=MID(\"CSC322\",4,2)", "32")]
    [InlineData("=CONCAT(\"CSC\",322,\"/\",TRUE)", "CSC322/TRUE")]
    [InlineData("=SUBSTITUTE(\"CSC 322 CSC\",\"CSC\",\"MTH\")", "MTH 322 MTH")]
    [InlineData("=SUBSTITUTE(\"a-a-a\",\"a\",\"b\",2)", "a-b-a")]
    public void TextFunctions(string formula, string expected) => Assert.Equal(expected, Eval.Display(formula));

    [Fact]
    public void Trim_CollapsesInternalRunsLikeExcel() =>
        Assert.Equal("a b c", Eval.Display("=TRIM(\"   a   b  c   \")"));

    [Fact]
    public void Exact_IsTheCaseSensitiveComparison()
    {
        Assert.True(Eval.Value("=EXACT(\"abc\",\"abc\")").AsBoolean);
        Assert.False(Eval.Value("=EXACT(\"abc\",\"ABC\")").AsBoolean);
        Assert.True(Eval.Value("=\"abc\"=\"ABC\"").AsBoolean);
    }

    [Fact]
    public void LogicalFunctions()
    {
        Assert.True(Eval.Value("=AND(TRUE,1,\"TRUE\")").AsBoolean);
        Assert.False(Eval.Value("=AND(TRUE,FALSE)").AsBoolean);
        Assert.True(Eval.Value("=OR(FALSE,TRUE)").AsBoolean);
        Assert.False(Eval.Value("=NOT(TRUE)").AsBoolean);
    }

    [Fact]
    public void ErrorInspection()
    {
        Assert.True(Eval.Value("=ISERROR(1/0)").AsBoolean);
        Assert.False(Eval.Value("=ISERROR(1)").AsBoolean);
        Assert.Equal("no mark", Eval.Display("=IFERROR(1/0,\"no mark\")"));
        Eval.Number("=IFERROR(10/2,\"no mark\")", 5);
    }

    [Fact]
    public void TypeInspection()
    {
        var sheet = new TestSheet().With("A1", 1.0).With("A2", "x");

        Assert.True(Eval.Value("=ISBLANK(A3)", sheet).AsBoolean);
        Assert.True(Eval.Value("=ISNUMBER(A1)", sheet).AsBoolean);
        Assert.True(Eval.Value("=ISTEXT(A2)", sheet).AsBoolean);
        Assert.False(Eval.Value("=ISBLANK(A1)", sheet).AsBoolean);
    }

    [Fact]
    public void CountIf_CountsMatchesAndSupportsComparisons()
    {
        var sheet = new TestSheet().WithColumn("A1", 70, 55, 40, 85, 55);

        Eval.Number("=COUNTIF(A1:A5,55)", 2, sheet);
        Eval.Number("=COUNTIF(A1:A5,\">=60\")", 2, sheet);
        Eval.Number("=COUNTIF(A1:A5,\"<>55\")", 3, sheet);
    }

    [Fact]
    public void CountIf_IsHowAUserFlagsDuplicatesWithAFormula()
    {
        // The formula-level counterpart of the duplicate-detection feature.
        var sheet = new TestSheet().WithColumn("A1", "CSC322", "CSC323", "CSC322");

        Assert.Equal("DUPLICATE", Eval.Display("=IF(COUNTIF(A1:A3,A1)>1,\"DUPLICATE\",\"\")", sheet));
        Assert.Equal(string.Empty, Eval.Display("=IF(COUNTIF(A1:A3,A2)>1,\"DUPLICATE\",\"\")", sheet));
    }

    [Fact]
    public void Value_ParsesNumericText()
    {
        Eval.Number("=VALUE(\"72.5\")", 72.5);
        Eval.Error("=VALUE(\"seventy\")", ErrorKind.Value);
    }

    [Fact]
    public void RegistryIsCaseInsensitiveAndEnumerable()
    {
        var registry = FunctionRegistry.CreateDefault();

        Assert.True(registry.TryGet("sum", out var lower));
        Assert.True(registry.TryGet("SUM", out var upper));
        Assert.Same(lower, upper);
        Assert.Contains("LOOKUP", registry.Names);
        Assert.All(
            new[] { "SUM", "AVERAGE", "MIN", "MAX", "COUNT", "IF", "ROUND", "LOOKUP" },
            required => Assert.Contains(required, registry.Names));
    }

    [Fact]
    public void ClientsCanRegisterTheirOwnFunctions()
    {
        // The Strategy is open: the results portal can add its own grade rules
        // without a fork of the engine.
        var registry = FunctionRegistry.CreateDefault();
        registry.Register(new WeightedTotalFunction());

        var sheet = new TestSheet(registry).With("A1", 60.0).With("B1", 80.0);

        Eval.Number("=CAWEIGHT(A1,B1)", 74, sheet); // 0.3*60 + 0.7*80
    }

    [Fact]
    public void RegisteringOverAnExistingNameIsRejectedUnlessAskedFor()
    {
        var registry = FunctionRegistry.CreateDefault();

        Assert.Throws<InvalidOperationException>(() => registry.Register(new WeightedTotalFunction("SUM")));
        registry.Register(new WeightedTotalFunction("SUM"), replaceExisting: true);
        Assert.True(registry.TryGet("SUM", out var replaced));
        Assert.IsType<WeightedTotalFunction>(replaced);
    }

    private sealed class WeightedTotalFunction(string name = "CAWEIGHT") : IFunction
    {
        public string Name { get; } = name;

        public int MinimumArguments => 2;

        public int MaximumArguments => 2;

        public string Description => "0.3 * continuous assessment + 0.7 * examination";

        public CellValue Invoke(FunctionArguments arguments)
        {
            var ca = arguments.Number(0);
            if (ca.IsError)
            {
                return ca;
            }

            var exam = arguments.Number(1);
            return exam.IsError ? exam : CellValue.Number((0.3 * ca.AsNumber) + (0.7 * exam.AsNumber));
        }
    }
}
