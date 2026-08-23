using System.Diagnostics.CodeAnalysis;
using CalcEngine.Core.Evaluation;
using CalcEngine.Core.Functions;
using CalcEngine.Core.Model;

namespace CalcEngine.Core;

/// <summary>
/// Binds an expression tree to the workbook it is being evaluated in.
/// </summary>
/// <remarks>
/// One instance is reused for the whole recalculation, with
/// <see cref="SheetIndex"/> re-pointed before each cell. A context per cell
/// would be 100,000 allocations in a full recalculation, for an object whose
/// only state is one integer.
/// </remarks>
internal sealed class WorkbookEvaluationContext(Workbook workbook) : IEvaluationContext
{
    /// <summary>The sheet unqualified references resolve against.</summary>
    internal int SheetIndex { get; set; }

    /// <inheritdoc />
    public string CurrentSheet => workbook.SheetAt(SheetIndex).Name;

    /// <inheritdoc />
    public CellValue GetCellValue(string? sheetName, CellAddress address)
    {
        if (!TryResolveSheet(sheetName, out var sheetIndex))
        {
            return CellValue.FromError(
                ErrorValue.Reference.WithDetail($"there is no sheet called '{sheetName}'"));
        }

        // A reference to a cell the workbook has never heard of is blank, and
        // deliberately does not allocate an identifier: evaluation must not
        // grow the graph it is walking.
        return workbook.TryGetId(sheetIndex, address, out var id) ? workbook.ValueOf(id) : CellValue.Blank;
    }

    /// <inheritdoc />
    public IRangeView? GetRange(string? sheetName, CellRange range) =>
        TryResolveSheet(sheetName, out var sheetIndex)
            ? new WorkbookRangeView(workbook, sheetIndex, range)
            : null;

    /// <inheritdoc />
    public bool TryGetFunction(string name, [MaybeNullWhen(false)] out IFunction function) =>
        workbook.Functions.TryGet(name, out function);

    private bool TryResolveSheet(string? sheetName, out int sheetIndex)
    {
        if (sheetName is null)
        {
            sheetIndex = SheetIndex;
            return true;
        }

        return workbook.TryGetSheetIndex(sheetName, out sheetIndex);
    }

    /// <summary>
    /// A window onto a rectangle of a sheet.
    /// </summary>
    /// <remarks>
    /// <see cref="NonBlankValues"/> chooses its side: walking the rectangle
    /// costs one lookup per cell in it, walking the sheet costs one containment
    /// test per cell the sheet holds. Taking the smaller of the two is what
    /// keeps <c>=SUM(A1:A100000)</c> on a sheet with a dozen entries from
    /// touching a hundred thousand addresses.
    /// </remarks>
    private sealed class WorkbookRangeView(Workbook workbook, int sheetIndex, CellRange range) : IRangeView
    {
        public CellRange Range => range;

        public int RowCount => range.RowCount;

        public int ColumnCount => range.ColumnCount;

        public CellValue At(int rowOffset, int columnOffset)
        {
            var address = new CellAddress(range.TopLeft.Column + columnOffset, range.TopLeft.Row + rowOffset);
            return workbook.TryGetId(sheetIndex, address, out var id) ? workbook.ValueOf(id) : CellValue.Blank;
        }

        public IEnumerable<CellValue> NonBlankValues()
        {
            var sheet = workbook.SheetAt(sheetIndex);
            if (range.CellCount <= sheet.IdByAddress.Count)
            {
                foreach (var address in range.Cells())
                {
                    if (workbook.TryGetId(sheetIndex, address, out var id))
                    {
                        var value = workbook.ValueOf(id);
                        if (!value.IsBlank)
                        {
                            yield return value;
                        }
                    }
                }

                yield break;
            }

            foreach (var (address, id) in sheet.IdByAddress)
            {
                if (!range.Contains(address))
                {
                    continue;
                }

                var value = workbook.ValueOf(id);
                if (!value.IsBlank)
                {
                    yield return value;
                }
            }
        }
    }
}
