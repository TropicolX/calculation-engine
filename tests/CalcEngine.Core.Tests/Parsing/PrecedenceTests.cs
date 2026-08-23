using CalcEngine.Core.Expressions;
using CalcEngine.Core.Parsing;

namespace CalcEngine.Core.Tests.Parsing;

/// <summary>
/// The precedence table of docs/grammar.md §4, expressed as tests so that it is
/// checkable rather than folklore.
/// </summary>
public class PrecedenceTests
{
    private static string Shape(string formula)
    {
        var result = FormulaParser.Parse(formula);
        Assert.True(result.Success, string.Join("; ", result.Errors));
        return FormulaPrinter.PrintFullyParenthesised(result.Expression!);
    }

    [Fact]
    public void MultiplicationBindsTighterThanAddition()
    {
        Assert.Equal("=(1 + (2 * 3))", Shape("=1+2*3"));
        Assert.Equal("=((1 + 2) * 3)", Shape("=(1+2)*3"));
    }

    [Fact]
    public void AdditionAndSubtractionAreLeftAssociative()
    {
        Assert.Equal("=((10 - 4) - 3)", Shape("=10-4-3"));
    }

    [Fact]
    public void DivisionIsLeftAssociative()
    {
        Assert.Equal("=((100 / 5) / 2)", Shape("=100/5/2"));
    }

    [Fact]
    public void PowerIsRightAssociative()
    {
        Assert.Equal("=(2 ^ (3 ^ 2))", Shape("=2^3^2"));
    }

    [Fact]
    public void Negation_BindsTighterThanPower_LikeExcel()
    {
        // Deliberate Excel compatibility: =-2^2 is 4, not -4.
        // docs/grammar.md §4.1 records why, and one alternative reordering in
        // Formula.g4 flips it if the department ever wants the other rule.
        Assert.Equal("=((-2) ^ 2)", Shape("=-2^2"));
    }

    [Fact]
    public void PowerBindsTighterThanMultiplication()
    {
        Assert.Equal("=(3 * (2 ^ 4))", Shape("=3*2^4"));
    }

    [Fact]
    public void PercentIsPostfixAndBindsAlmostTightest()
    {
        Assert.Equal("=(50% * 200)", Shape("=50%*200"));
        Assert.Equal("=((-50)%)", Shape("=-50%"));
    }

    [Fact]
    public void ConcatenationIsLooserThanArithmetic()
    {
        Assert.Equal("=(\"Total: \" & (1 + 2))", Shape("=\"Total: \"&1+2"));
    }

    [Fact]
    public void ComparisonIsTheLoosestOperator()
    {
        Assert.Equal("=((A1 + 1) > (B1 * 2))", Shape("=A1+1>B1*2"));
        Assert.Equal("=((\"a\" & \"b\") = \"ab\")", Shape("=\"a\"&\"b\"=\"ab\""));
    }

    [Fact]
    public void ComparisonsChainLeftAssociatively()
    {
        Assert.Equal("=((1 < 2) < 3)", Shape("=1<2<3"));
    }

    [Fact]
    public void UnaryMinusStacks()
    {
        Assert.Equal("=(-(-A1))", Shape("=--A1"));
    }

    [Fact]
    public void PowerAcceptsANegativeExponent()
    {
        Assert.Equal("=(2 ^ (-3))", Shape("=2^-3"));
    }
}
