using CalcEngine.Core.Evaluation;
using CalcEngine.Core.Functions;
using CalcEngine.Core.Model;
using CalcEngine.Core.Parsing;

namespace CalcEngine.Core.Tests.Evaluation;

/// <summary>
/// A hand-built evaluation context over a dictionary of cells.
/// </summary>
/// <remarks>
/// The evaluator is tested against this rather than against a real workbook so
/// that a failure here can only mean the evaluator is wrong. Coupling the two
/// would make every evaluator test also a test of the dependency graph.
/// </remarks>
internal sealed class TestSheet : IEvaluationContext
{
    private readonly Dictionary<CellAddress, CellValue> _cells = [];

    public TestSheet(FunctionRegistry? functions = null) =>
        Functions = functions ?? FunctionRegistry.CreateDefault();

    public FunctionRegistry Functions { get; }

    public string CurrentSheet => "Sheet1";

    public CellValue this[string address]
    {
        get => _cells.TryGetValue(CellAddress.Parse(address), out var value) ? value : CellValue.Blank;
        set => _cells[CellAddress.Parse(address)] = value;
    }

    public TestSheet With(string address, double number)
    {
        this[address] = CellValue.Number(number);
        return this;
    }

    public TestSheet With(string address, string text)
    {
        this[address] = CellValue.Text(text);
        return this;
    }

    public TestSheet With(string address, bool flag)
    {
        this[address] = CellValue.Boolean(flag);
        return this;
    }

    public TestSheet WithError(string address, ErrorValue error)
    {
        this[address] = CellValue.FromError(error);
        return this;
    }

    public TestSheet WithColumn(string topCell, params object[] values)
    {
        var top = CellAddress.Parse(topCell);
        for (var i = 0; i < values.Length; i++)
        {
            var address = new CellAddress(top.Column, top.Row + i);
            _cells[address] = values[i] switch
            {
                null => CellValue.Blank,
                double d => CellValue.Number(d),
                int n => CellValue.Number(n),
                bool b => CellValue.Boolean(b),
                ErrorValue e => CellValue.FromError(e),
                var other => CellValue.Text(other.ToString()!),
            };
        }

        return this;
    }

    public CellValue GetCellValue(string? sheetName, CellAddress address)
    {
        if (sheetName is not null && !string.Equals(sheetName, CurrentSheet, StringComparison.OrdinalIgnoreCase))
        {
            return CellValue.FromError(ErrorValue.Reference.WithDetail($"there is no sheet called '{sheetName}'"));
        }

        return _cells.TryGetValue(address, out var value) ? value : CellValue.Blank;
    }

    public IRangeView? GetRange(string? sheetName, CellRange range)
    {
        if (sheetName is not null && !string.Equals(sheetName, CurrentSheet, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new DictionaryRangeView(_cells, range);
    }

    public bool TryGetFunction(string name, out IFunction function) => Functions.TryGet(name, out function);

    private sealed class DictionaryRangeView(Dictionary<CellAddress, CellValue> cells, CellRange range) : IRangeView
    {
        public CellRange Range => range;

        public int RowCount => range.RowCount;

        public int ColumnCount => range.ColumnCount;

        public CellValue At(int rowOffset, int columnOffset)
        {
            var address = new CellAddress(range.TopLeft.Column + columnOffset, range.TopLeft.Row + rowOffset);
            return cells.TryGetValue(address, out var value) ? value : CellValue.Blank;
        }

        public IEnumerable<CellValue> NonBlankValues()
        {
            foreach (var address in range.Cells())
            {
                if (cells.TryGetValue(address, out var value) && !value.IsBlank)
                {
                    yield return value;
                }
            }
        }
    }
}

internal static class Eval
{
    /// <summary>Parses and evaluates <paramref name="formula"/> against <paramref name="sheet"/>.</summary>
    public static CellValue Value(string formula, TestSheet? sheet = null)
    {
        var parsed = FormulaParser.Parse(formula);
        Assert.True(parsed.Success, string.Join("; ", parsed.Errors));
        return parsed.Expression!.Evaluate(sheet ?? new TestSheet());
    }

    /// <summary>Evaluates and returns the display text, the way a grid would show it.</summary>
    public static string Display(string formula, TestSheet? sheet = null) => Value(formula, sheet).ToDisplayString();

    /// <summary>Evaluates and asserts a numeric result, tolerating floating-point noise.</summary>
    public static void Number(string formula, double expected, TestSheet? sheet = null)
    {
        var value = Value(formula, sheet);
        Assert.True(value.IsNumber, $"{formula} produced {value}");
        Assert.Equal(expected, value.AsNumber, precision: 10);
    }

    /// <summary>Evaluates and asserts a particular error kind.</summary>
    public static void Error(string formula, ErrorKind expected, TestSheet? sheet = null)
    {
        var value = Value(formula, sheet);
        Assert.True(value.IsError, $"{formula} produced {value}, expected {expected}");
        Assert.Equal(expected, value.AsError.Kind);
    }
}
