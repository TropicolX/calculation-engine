using System.Diagnostics.CodeAnalysis;
using CalcEngine.Core.Functions;
using CalcEngine.Core.Model;

namespace CalcEngine.Core.Evaluation;

/// <summary>
/// Everything an expression needs in order to evaluate itself: the values it can
/// read and the functions it may call.
/// </summary>
/// <remarks>
/// The interface is deliberately narrow. It is the seam between the expression
/// tree and the workbook, and it is what lets the evaluator be tested against a
/// dictionary of cells with no dependency graph in sight. It is also the hook
/// the recalculation engine uses to record which cells a formula actually read.
/// </remarks>
public interface IEvaluationContext
{
    /// <summary>The sheet an unqualified reference resolves against.</summary>
    string CurrentSheet { get; }

    /// <summary>Reads one cell. A blank or missing cell reads as <see cref="CellValue.Blank"/>.</summary>
    /// <param name="sheetName">A qualified sheet name, or null for <see cref="CurrentSheet"/>.</param>
    /// <param name="address">The cell to read.</param>
    /// <returns>The cell's value, or <c>#REF!</c> if the sheet does not exist.</returns>
    CellValue GetCellValue(string? sheetName, CellAddress address);

    /// <summary>Opens a window onto a rectangle of cells.</summary>
    /// <returns>A view, or null if the sheet does not exist.</returns>
    IRangeView? GetRange(string? sheetName, CellRange range);

    /// <summary>Resolves a function by name, case-insensitively.</summary>
    bool TryGetFunction(string name, [MaybeNullWhen(false)] out IFunction function);
}

/// <summary>
/// A read-only window onto a rectangle of cells.
/// </summary>
/// <remarks>
/// The two access paths exist for two different costs.
/// <see cref="NonBlankValues"/> is what the aggregates use, and a workbook
/// implements it by walking whichever is smaller — the rectangle or the sheet's
/// populated cells — so <c>SUM(A1:A100000)</c> over a sheet holding twelve
/// values touches twelve values. <see cref="At"/> is positional, which
/// <c>LOOKUP</c> needs and the aggregates do not.
/// </remarks>
public interface IRangeView
{
    /// <summary>The rectangle this view covers.</summary>
    CellRange Range { get; }

    /// <summary>Height of the rectangle.</summary>
    int RowCount { get; }

    /// <summary>Width of the rectangle.</summary>
    int ColumnCount { get; }

    /// <summary>Reads a cell by offset from the top-left corner.</summary>
    CellValue At(int rowOffset, int columnOffset);

    /// <summary>Enumerates the non-blank cells, in row-major order.</summary>
    IEnumerable<CellValue> NonBlankValues();
}
