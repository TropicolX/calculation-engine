using CalcEngine.Core.Model;

namespace CalcEngine.Core;

/// <summary>
/// One named grid inside a <see cref="Workbook"/>.
/// </summary>
/// <remarks>
/// <para><b>Abstraction function.</b> AF(s) = a partial function from
/// <see cref="CellAddress"/> to cell content, together with the sheet's name.
/// Addresses not in the domain are blank.</para>
///
/// <para><b>Representation invariant.</b> <c>Name</c> is non-empty and unique
/// within its workbook; every identifier in <c>IdByAddress</c> addresses a cell
/// record of this sheet; the map holds an entry for every cell that has ever
/// been written to <em>or referred to</em>, which is why
/// <see cref="PopulatedCells"/> filters on content rather than trusting the
/// map.</para>
///
/// <para>A sheet is a view onto its workbook's flat cell array rather than a
/// container of its own: cell identifiers must be unique across the workbook so
/// that one dependency graph can span sheets.</para>
/// </remarks>
public sealed class Sheet
{
    private readonly Workbook _workbook;

    internal Sheet(Workbook workbook, string name, int index)
    {
        _workbook = workbook;
        Name = name;
        Index = index;
    }

    /// <summary>The sheet's name, as it appears in a qualified reference.</summary>
    public string Name { get; internal set; }

    /// <summary>Position of the sheet in its workbook; also its identifier in the dependency graph.</summary>
    public int Index { get; }

    /// <summary>Every address the workbook knows about on this sheet, written to or referred to.</summary>
    internal Dictionary<CellAddress, int> IdByAddress { get; } = [];

    /// <summary>How many cells actually hold content.</summary>
    public int PopulatedCellCount
    {
        get
        {
            var count = 0;
            foreach (var id in IdByAddress.Values)
            {
                if (_workbook.HasContent(id))
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>The addresses that hold content, in no particular order.</summary>
    public IEnumerable<CellAddress> PopulatedCells()
    {
        foreach (var (address, id) in IdByAddress)
        {
            if (_workbook.HasContent(id))
            {
                yield return address;
            }
        }
    }

    /// <summary>
    /// The smallest rectangle containing every populated cell, or the single
    /// cell A1 when the sheet is empty.
    /// </summary>
    public CellRange UsedRange
    {
        get
        {
            var left = int.MaxValue;
            var top = int.MaxValue;
            var right = 0;
            var bottom = 0;

            foreach (var address in PopulatedCells())
            {
                left = Math.Min(left, address.Column);
                right = Math.Max(right, address.Column);
                top = Math.Min(top, address.Row);
                bottom = Math.Max(bottom, address.Row);
            }

            return right == 0
                ? CellRange.Single(CellAddress.A1)
                : new CellRange(new CellAddress(left, top), new CellAddress(right, bottom));
        }
    }

    /// <summary>The value currently shown by a cell.</summary>
    public CellValue GetValue(CellAddress address) => _workbook.GetCellValue(Name, address);

    /// <summary>The raw content of a cell, exactly as typed.</summary>
    public string? GetContent(CellAddress address) => _workbook.GetCellContent(Name, address);

    /// <inheritdoc />
    public override string ToString() => Name;
}
