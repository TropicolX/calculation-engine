using CalcEngine.Core.Model;

namespace CalcEngine.Core.Dependencies;

/// <summary>One formula's dependence on a rectangle of cells.</summary>
internal readonly record struct RangeDependency(int SheetIndex, CellRange Range, int DependentId);

/// <summary>
/// A spatial index from "a cell changed" to "which formulas read a range
/// containing it".
/// </summary>
/// <remarks>
/// <para><b>Why not just expand the range into edges?</b> Because
/// <c>=SUM(B2:B45)</c> would become 44 edges, and a results workbook with one
/// such formula per course per level reaches millions of edges that must be
/// torn down and rebuilt every time a lecturer widens a range by one row. Worse,
/// expansion cannot answer the question that matters: typing into a cell that
/// was empty when the formula was written must still trigger it, and an empty
/// cell has no edges.</para>
///
/// <para><b>The index.</b> The grid is cut into 64x64 blocks. A range is filed
/// under every block it touches; a changed cell is looked up by its own block
/// and the (few) candidate ranges filed there are tested for containment. A
/// range spanning more blocks than <see cref="MaxBlocksPerRange"/> goes into a
/// small always-scanned list instead, so a pathological <c>A1:XFD1048576</c>
/// costs one list entry rather than four million index entries.</para>
///
/// <para><b>Abstraction function.</b> AF(index) = the set of range dependencies
/// { (sheet, range, dependent) } it holds, irrespective of how they are filed.
/// <b>Representation invariant.</b> Every dependency appears either in
/// <c>_large</c> or under exactly the blocks its range intersects, and never in
/// both; no block list is empty.</para>
/// </remarks>
internal sealed class RangeDependencyIndex
{
    /// <summary>log2 of the block edge; 6 gives 64x64 = 4,096 cells per block.</summary>
    private const int BlockShift = 6;

    /// <summary>Ranges touching more blocks than this are not indexed spatially.</summary>
    private const int MaxBlocksPerRange = 1024;

    private readonly Dictionary<long, List<RangeDependency>> _blocks = [];
    private readonly List<RangeDependency> _large = [];

    /// <summary>How many dependencies are held spatially; diagnostics only.</summary>
    internal int BlockCount => _blocks.Count;

    /// <summary>How many dependencies were too big to index; diagnostics only.</summary>
    internal int LargeRangeCount => _large.Count;

    internal void Add(RangeDependency dependency)
    {
        if (BlocksTouched(dependency.Range) > MaxBlocksPerRange)
        {
            _large.Add(dependency);
            return;
        }

        foreach (var key in BlockKeys(dependency))
        {
            if (!_blocks.TryGetValue(key, out var bucket))
            {
                bucket = [];
                _blocks[key] = bucket;
            }

            bucket.Add(dependency);
        }
    }

    internal void Remove(RangeDependency dependency)
    {
        if (BlocksTouched(dependency.Range) > MaxBlocksPerRange)
        {
            RemoveFrom(_large, dependency);
            return;
        }

        foreach (var key in BlockKeys(dependency))
        {
            if (_blocks.TryGetValue(key, out var bucket))
            {
                RemoveFrom(bucket, dependency);
                if (bucket.Count == 0)
                {
                    _blocks.Remove(key);
                }
            }
        }
    }

    /// <summary>Appends the formulas that read a range containing this cell.</summary>
    internal void CollectDependents(int sheetIndex, CellAddress address, List<int> destination)
    {
        var key = BlockKey(sheetIndex, address.Column >> BlockShift, address.Row >> BlockShift);
        if (_blocks.TryGetValue(key, out var bucket))
        {
            foreach (var dependency in bucket)
            {
                if (dependency.SheetIndex == sheetIndex && dependency.Range.Contains(address))
                {
                    destination.Add(dependency.DependentId);
                }
            }
        }

        foreach (var dependency in _large)
        {
            if (dependency.SheetIndex == sheetIndex && dependency.Range.Contains(address))
            {
                destination.Add(dependency.DependentId);
            }
        }
    }

    private static void RemoveFrom(List<RangeDependency> bucket, RangeDependency dependency)
    {
        // Order carries no meaning here, so removal is a swap with the tail.
        var index = bucket.IndexOf(dependency);
        if (index < 0)
        {
            return;
        }

        bucket[index] = bucket[^1];
        bucket.RemoveAt(bucket.Count - 1);
    }

    private static long BlocksTouched(CellRange range)
    {
        var columns = (range.BottomRight.Column >> BlockShift) - (range.TopLeft.Column >> BlockShift) + 1;
        var rows = (range.BottomRight.Row >> BlockShift) - (range.TopLeft.Row >> BlockShift) + 1;
        return (long)columns * rows;
    }

    private static IEnumerable<long> BlockKeys(RangeDependency dependency)
    {
        var range = dependency.Range;
        var firstColumn = range.TopLeft.Column >> BlockShift;
        var lastColumn = range.BottomRight.Column >> BlockShift;
        var firstRow = range.TopLeft.Row >> BlockShift;
        var lastRow = range.BottomRight.Row >> BlockShift;

        for (var column = firstColumn; column <= lastColumn; column++)
        {
            for (var row = firstRow; row <= lastRow; row++)
            {
                yield return BlockKey(dependency.SheetIndex, column, row);
            }
        }
    }

    private static long BlockKey(int sheetIndex, int blockColumn, int blockRow) =>
        ((long)sheetIndex << 40) | ((long)blockColumn << 20) | (uint)blockRow;
}
