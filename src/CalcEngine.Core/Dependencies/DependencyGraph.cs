using CalcEngine.Core.Model;

namespace CalcEngine.Core.Dependencies;

/// <summary>
/// The dependency graph: who reads whom, and in what order they must be
/// recomputed.
/// </summary>
/// <remarks>
/// <para><b>Abstraction function.</b> AF(g) = a directed graph whose vertices
/// are cell identifiers and which contains the edge u → v exactly when the
/// formula in v reads u — either directly (<c>=u+1</c>) or through a range that
/// contains u (<c>=SUM(...u...)</c>). The direction is "flows into", so a
/// topological order of the graph is a safe evaluation order.</para>
///
/// <para><b>Representation invariant.</b>
/// <c>v ∈ _precedents[u] ⇔ u ∈ _dependents[v]</c> for the direct edges;
/// every range in <c>_rangePrecedents[v]</c> is filed in <c>_rangeIndex</c>
/// against v and nowhere else; all three arrays are at least
/// <c>Capacity</c> long; no list contains duplicates.</para>
///
/// <para><b>Why integer identifiers.</b> Vertices are dense <c>int</c>s, not
/// addresses, so adjacency is array indexing rather than hashing, and the
/// traversal marks are flat arrays. Marking is done with a generation stamp
/// rather than by clearing: an edit in a 100,000-cell workbook must not pay
/// 100,000 writes just to reset the visited flags, and that single decision is
/// most of the difference between the 50 ms budget and missing it.</para>
/// </remarks>
internal sealed class DependencyGraph
{
    private List<int>?[] _precedents = new List<int>?[64];
    private List<int>?[] _dependents = new List<int>?[64];
    private List<RangeDependency>?[] _rangePrecedents = new List<RangeDependency>?[64];
    private readonly RangeDependencyIndex _rangeIndex = new();

    // Traversal scratch, reused across calls so that a recalculation allocates
    // nothing per cell.
    private int[] _visitStamp = new int[64];
    private byte[] _state = new byte[64];
    private int[] _frameIndex = new int[64];
    private int _currentStamp;
    private readonly List<int> _childBuffer = [];
    private readonly List<Frame> _frames = [];

    private const byte InProgress = 1;
    private const byte Done = 2;

    /// <summary>How many vertices the arrays can hold.</summary>
    internal int Capacity => _precedents.Length;

    /// <summary>Diagnostics: how many ranges were too large to index spatially.</summary>
    internal int LargeRangeCount => _rangeIndex.LargeRangeCount;

    /// <summary>Grows the adjacency arrays so that <paramref name="id"/> is addressable.</summary>
    internal void EnsureCapacity(int id)
    {
        if (id < _precedents.Length)
        {
            return;
        }

        var size = Math.Max(_precedents.Length * 2, id + 1);
        Array.Resize(ref _precedents, size);
        Array.Resize(ref _dependents, size);
        Array.Resize(ref _rangePrecedents, size);
        Array.Resize(ref _visitStamp, size);
        Array.Resize(ref _state, size);
        Array.Resize(ref _frameIndex, size);
    }

    /// <summary>Removes every edge that says "<paramref name="id"/> reads something".</summary>
    internal void ClearPrecedents(int id)
    {
        var precedents = _precedents[id];
        if (precedents is not null)
        {
            foreach (var precedent in precedents)
            {
                var dependents = _dependents[precedent];
                if (dependents is null)
                {
                    continue;
                }

                var at = dependents.IndexOf(id);
                if (at >= 0)
                {
                    dependents[at] = dependents[^1];
                    dependents.RemoveAt(dependents.Count - 1);
                }
            }

            precedents.Clear();
        }

        var ranges = _rangePrecedents[id];
        if (ranges is not null)
        {
            foreach (var range in ranges)
            {
                _rangeIndex.Remove(range);
            }

            ranges.Clear();
        }
    }

    /// <summary>Records that <paramref name="dependentId"/> reads <paramref name="precedentId"/>.</summary>
    internal void AddCellEdge(int precedentId, int dependentId)
    {
        var precedents = _precedents[dependentId] ??= [];
        if (precedents.Contains(precedentId))
        {
            return;
        }

        precedents.Add(precedentId);
        (_dependents[precedentId] ??= []).Add(dependentId);
    }

    /// <summary>Records that <paramref name="dependentId"/> reads a whole range.</summary>
    internal void AddRangeEdge(int sheetIndex, CellRange range, int dependentId)
    {
        var dependency = new RangeDependency(sheetIndex, range, dependentId);
        var ranges = _rangePrecedents[dependentId] ??= [];
        if (ranges.Contains(dependency))
        {
            return;
        }

        ranges.Add(dependency);
        _rangeIndex.Add(dependency);
    }

    /// <summary>The cells that directly read <paramref name="id"/>, appended to the buffer.</summary>
    internal void CollectDependents(int id, int sheetIndex, CellAddress address, List<int> destination)
    {
        var direct = _dependents[id];
        if (direct is not null)
        {
            destination.AddRange(direct);
        }

        _rangeIndex.CollectDependents(sheetIndex, address, destination);
    }

    /// <summary>Whether the cell has any dependents at all; a cheap early exit.</summary>
    internal bool HasDependents(int id) => _dependents[id] is { Count: > 0 };

    /// <summary>
    /// Produces a safe evaluation order for everything reachable from
    /// <paramref name="seeds"/>, and the cycles found on the way.
    /// </summary>
    /// <remarks>
    /// <para>Depth-first search over the "flows into" edges, pushing each vertex
    /// after its descendants and reversing at the end: the classic
    /// Tarjan/Cormen topological sort, restricted to the affected subgraph so
    /// that the cost is proportional to what actually changed rather than to the
    /// size of the workbook.</para>
    ///
    /// <para>The search is explicitly stacked rather than recursive. A chain of
    /// 20,000 dependent cells is a perfectly ordinary import artefact, and a
    /// recursive walk would meet it with a <see cref="StackOverflowException"/> —
    /// which cannot be caught, so the client would simply lose the process.</para>
    ///
    /// <para>A back edge to a vertex still on the stack is a cycle. It is
    /// recorded and then <em>not</em> followed, so the traversal still
    /// terminates and still orders everything else correctly; the caller marks
    /// the cycle's members <c>#CIRC!</c> instead of evaluating them.</para>
    /// </remarks>
    /// <param name="seeds">The cells whose values have just changed.</param>
    /// <param name="addressOf">Maps an identifier to its sheet and address, for the range index.</param>
    /// <param name="order">Receives the evaluation order, precedents first.</param>
    /// <param name="cycles">Receives one entry per distinct cycle, in reference order.</param>
    internal void TopologicalOrderFrom(
        IReadOnlyList<int> seeds,
        Func<int, (int SheetIndex, CellAddress Address)> addressOf,
        List<int> order,
        List<int[]> cycles)
    {
        order.Clear();
        cycles.Clear();
        _currentStamp++;
        _childBuffer.Clear();
        _frames.Clear();

        HashSet<string>? seenCycles = null;

        foreach (var seed in seeds)
        {
            if (_visitStamp[seed] == _currentStamp)
            {
                continue;
            }

            Push(seed, addressOf);

            while (_frames.Count > 0)
            {
                var frame = _frames[^1];
                if (frame.Next < frame.End)
                {
                    _frames[^1] = frame with { Next = frame.Next + 1 };
                    var child = _childBuffer[frame.Next];

                    if (_visitStamp[child] != _currentStamp)
                    {
                        Push(child, addressOf);
                    }
                    else if (_state[child] == InProgress)
                    {
                        var cycle = ExtractCycle(child, addressOf);
                        seenCycles ??= [];
                        if (seenCycles.Add(string.Join(',', cycle)))
                        {
                            cycles.Add(cycle);
                        }
                    }

                    continue;
                }

                _state[frame.Node] = Done;
                order.Add(frame.Node);
                _childBuffer.RemoveRange(frame.Start, _childBuffer.Count - frame.Start);
                _frames.RemoveAt(_frames.Count - 1);
            }
        }

        order.Reverse();
    }

    private void Push(int node, Func<int, (int SheetIndex, CellAddress Address)> addressOf)
    {
        _visitStamp[node] = _currentStamp;
        _state[node] = InProgress;
        _frameIndex[node] = _frames.Count;

        var start = _childBuffer.Count;
        var (sheetIndex, address) = addressOf(node);
        CollectDependents(node, sheetIndex, address, _childBuffer);
        _frames.Add(new Frame(node, start, start, _childBuffer.Count));
    }

    /// <summary>
    /// Turns the stack slice that closed a cycle into the path a user can read.
    /// </summary>
    /// <remarks>
    /// The stack holds the ring in "is read by" order, because that is the
    /// direction the search travels. A user thinks in the other direction — "A1
    /// refers to B3, which refers to C7, which refers back to A1" — so the tail
    /// is reversed. The result is then rotated to start at the smallest address,
    /// which makes the report independent of where the search happened to enter
    /// the ring: the same broken workbook always produces the same sentence.
    /// </remarks>
    private int[] ExtractCycle(int closingNode, Func<int, (int SheetIndex, CellAddress Address)> addressOf)
    {
        var from = _frameIndex[closingNode];
        var length = _frames.Count - from;

        var path = new int[length];
        path[0] = _frames[from].Node;
        for (var i = 1; i < length; i++)
        {
            path[i] = _frames[_frames.Count - i].Node;
        }

        return Rotate(path, addressOf);
    }

    private static int[] Rotate(int[] path, Func<int, (int SheetIndex, CellAddress Address)> addressOf)
    {
        var best = 0;
        var bestKey = addressOf(path[0]);
        for (var i = 1; i < path.Length; i++)
        {
            var key = addressOf(path[i]);
            if (key.SheetIndex < bestKey.SheetIndex ||
                (key.SheetIndex == bestKey.SheetIndex && key.Address < bestKey.Address))
            {
                best = i;
                bestKey = key;
            }
        }

        if (best == 0)
        {
            return path;
        }

        var rotated = new int[path.Length];
        for (var i = 0; i < path.Length; i++)
        {
            rotated[i] = path[(best + i) % path.Length];
        }

        return rotated;
    }

    private readonly record struct Frame(int Node, int Start, int Next, int End);
}
