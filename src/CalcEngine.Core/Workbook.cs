using System.Diagnostics;
using CalcEngine.Core.Dependencies;
using CalcEngine.Core.Evaluation;
using CalcEngine.Core.Expressions;
using CalcEngine.Core.Functions;
using CalcEngine.Core.Model;
using CalcEngine.Core.Parsing;

namespace CalcEngine.Core;

/// <summary>One cell and the content to put in it.</summary>
/// <param name="Address">Which cell.</param>
/// <param name="Content">The new content; null clears the cell.</param>
public readonly record struct CellEdit(SheetCellAddress Address, string? Content);

/// <summary>
/// The calculation engine: a collection of sheets, one dependency graph, and a
/// reactive recalculation loop.
/// </summary>
/// <remarks>
/// <para><b>Abstraction function.</b> AF(w) = a mapping from every
/// <see cref="SheetCellAddress"/> of every sheet to a pair (content, value),
/// where value is the result of evaluating content in the workbook, together
/// with an undo history.</para>
///
/// <para><b>Representation invariant.</b>
/// Sheet names are unique, case-insensitively.
/// Every identifier appears in exactly one sheet's address map, and
/// <c>_cells[id]</c> records the sheet and address that map back to it.
/// After any public call returns with automatic calculation on, every formula
/// cell not in a cycle holds the value obtained by evaluating its tree against
/// the current values of the cells it reads — that is, the workbook is
/// <em>consistent</em>.
/// The dependency graph holds an edge u → v exactly when v's stored tree reads
/// u, and no edges for cells whose content is a literal or failed to parse.</para>
///
/// <para><b>Design.</b> Cells live in one flat array indexed by a dense
/// identifier, not in per-sheet dictionaries of objects. That gives array-speed
/// adjacency in the graph, lets one graph span sheets, and keeps a
/// 100,000-cell workbook to a handful of allocations rather than 100,000.</para>
/// </remarks>
public sealed class Workbook
{
    private CellRecord[] _cells = new CellRecord[1024];
    private int _nextId;

    private readonly Dictionary<long, int> _idByKey = [];
    private readonly List<Sheet> _sheets = [];
    private readonly Dictionary<string, Sheet> _sheetsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly DependencyGraph _graph = new();
    private readonly List<ICellChangeObserver> _observers = [];
    private readonly WorkbookEvaluationContext _context;
    private readonly Func<int, (int SheetIndex, CellAddress Address)> _addressOf;

    // Traversal scratch, reused so that an edit allocates almost nothing.
    private readonly List<int> _order = [];
    private readonly List<int[]> _cycleIds = [];
    private readonly List<int> _seeds = [];
    private readonly HashSet<int> _pendingSeeds = [];

    /// <summary>Creates a workbook with a single sheet.</summary>
    public Workbook(WorkbookOptions? options = null)
    {
        Options = options ?? new WorkbookOptions();
        Functions = Options.Functions ?? FunctionRegistry.CreateDefault();
        _context = new WorkbookEvaluationContext(this);
        _addressOf = id => (_cells[id].SheetIndex, new CellAddress(_cells[id].Column, _cells[id].Row));
        History = new Commands.CommandStack(this, Options.HistoryLimit);
        AddSheet(Options.InitialSheetName);
    }

    /// <summary>The options this workbook was created with.</summary>
    public WorkbookOptions Options { get; }

    /// <summary>The function library in force. Clients may register their own functions.</summary>
    public FunctionRegistry Functions { get; }

    /// <summary>The undo/redo history.</summary>
    public Commands.CommandStack History { get; }

    /// <summary>The sheets, in creation order.</summary>
    public IReadOnlyList<Sheet> Sheets => _sheets;

    /// <summary>Raised once per recalculation, after every value is settled.</summary>
    public event EventHandler<CellsChangedEventArgs>? CellsChanged;

    /// <summary>True when edits recalculate immediately.</summary>
    public bool AutomaticCalculation { get; set; } = true;

    /// <summary>Total number of cells the workbook has identifiers for; diagnostics.</summary>
    public int TrackedCellCount => _nextId;

    // ---------------------------------------------------------------- sheets

    /// <summary>Adds a sheet.</summary>
    /// <exception cref="InvalidOperationException">The name is already taken.</exception>
    public Sheet AddSheet(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A sheet name is required.", nameof(name));
        }

        if (_sheetsByName.ContainsKey(name))
        {
            throw new InvalidOperationException($"A sheet called '{name}' already exists.");
        }

        var sheet = new Sheet(this, name, _sheets.Count);
        _sheets.Add(sheet);
        _sheetsByName[name] = sheet;
        return sheet;
    }

    /// <summary>Finds a sheet by name, case-insensitively.</summary>
    public bool TryGetSheet(string name, out Sheet sheet) => _sheetsByName.TryGetValue(name, out sheet!);

    /// <summary>Finds a sheet by name.</summary>
    /// <exception cref="KeyNotFoundException">There is no such sheet.</exception>
    public Sheet GetSheet(string name) =>
        TryGetSheet(name, out var sheet) ? sheet : throw new KeyNotFoundException($"There is no sheet called '{name}'.");

    // ----------------------------------------------------------------- reads

    /// <summary>The value currently shown by a cell.</summary>
    public CellValue GetCellValue(string sheetName, CellAddress address) =>
        TryGetSheet(sheetName, out var sheet) && sheet.IdByAddress.TryGetValue(address, out var id)
            ? _cells[id].Value
            : CellValue.Blank;

    /// <inheritdoc cref="GetCellValue(string, CellAddress)"/>
    public CellValue GetCellValue(string sheetName, string address) =>
        GetCellValue(sheetName, CellAddress.Parse(address));

    /// <inheritdoc cref="GetCellValue(string, CellAddress)"/>
    public CellValue GetCellValue(SheetCellAddress address) => GetCellValue(address.Sheet, address.Address);

    /// <summary>The raw content of a cell, exactly as typed; null when empty.</summary>
    public string? GetCellContent(string sheetName, CellAddress address) =>
        TryGetSheet(sheetName, out var sheet) && sheet.IdByAddress.TryGetValue(address, out var id)
            ? _cells[id].Content
            : null;

    /// <inheritdoc cref="GetCellContent(string, CellAddress)"/>
    public string? GetCellContent(string sheetName, string address) =>
        GetCellContent(sheetName, CellAddress.Parse(address));

    /// <inheritdoc cref="GetCellContent(string, CellAddress)"/>
    public string? GetCellContent(SheetCellAddress address) => GetCellContent(address.Sheet, address.Address);

    /// <summary>The expression tree of a formula cell, or null when the cell holds a literal.</summary>
    public Expression? GetCellFormula(SheetCellAddress address) =>
        TryGetSheet(address.Sheet, out var sheet) && sheet.IdByAddress.TryGetValue(address.Address, out var id)
            ? _cells[id].Formula
            : null;

    internal bool HasContent(int id) => _cells[id].Content is not null;

    // ---------------------------------------------------------------- writes

    /// <summary>Sets one cell's content and recalculates what depends on it.</summary>
    public CalculationResult SetCellContent(string sheetName, CellAddress address, string? content) =>
        SetCellContents([new CellEdit(new SheetCellAddress(sheetName, address), content)]);

    /// <summary>Applies a batch of edits as one undoable operation.</summary>
    /// <param name="edits">What to write.</param>
    /// <param name="description">Menu text for the undo history.</param>
    public CalculationResult SetCellContents(IReadOnlyList<CellEdit> edits, string? description = null) =>
        History.Execute(new Commands.SetCellsCommand(edits, description));

    /// <inheritdoc cref="SetCellContent(string, CellAddress, string?)"/>
    public CalculationResult SetCellContent(string sheetName, string address, string? content) =>
        SetCellContent(sheetName, CellAddress.Parse(address), content);

    /// <inheritdoc cref="SetCellContent(string, CellAddress, string?)"/>
    public CalculationResult SetCellContent(SheetCellAddress address, string? content) =>
        SetCellContent(address.Sheet, address.Address, content);

    /// <summary>
    /// Applies a batch of edits as one operation: one recalculation, one
    /// notification. Bypasses the undo history, which is the caller's business.
    /// </summary>
    /// <remarks>
    /// This is the primitive every mutating path goes through, including undo
    /// itself. Replacing 400 occurrences of a course code must not run 400
    /// recalculations, and undoing it must not take 400 presses of Ctrl+Z.
    /// </remarks>
    internal CalculationResult ApplyEdits(IReadOnlyList<CellEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var start = Stopwatch.GetTimestamp();
        var syntaxErrors = new List<FormulaSyntaxError>();
        _seeds.Clear();

        foreach (var edit in edits)
        {
            if (!TryGetSheet(edit.Address.Sheet, out var sheet))
            {
                throw new KeyNotFoundException($"There is no sheet called '{edit.Address.Sheet}'.");
            }

            var content = string.IsNullOrEmpty(edit.Content) ? null : edit.Content;
            var id = GetOrCreateId(sheet, edit.Address.Address);
            if (string.Equals(_cells[id].Content, content, StringComparison.Ordinal))
            {
                continue;
            }

            ApplyContent(id, content, syntaxErrors);
            _seeds.Add(id);
        }

        if (_seeds.Count == 0)
        {
            return new CalculationResult([], [], syntaxErrors, 0, Stopwatches.Elapsed(start));
        }

        if (!AutomaticCalculation || !Options.AutomaticCalculation)
        {
            return DeferRecalculation(syntaxErrors, start);
        }

        return Recalculate(_seeds, syntaxErrors, start);
    }

    /// <summary>Recalculates every formula in the workbook, from scratch.</summary>
    public CalculationResult RecalculateAll()
    {
        var start = Stopwatch.GetTimestamp();
        var all = new List<int>(_nextId);
        for (var id = 0; id < _nextId; id++)
        {
            all.Add(id);
        }

        _pendingSeeds.Clear();
        return Recalculate(all, [], start);
    }

    /// <summary>
    /// Recalculates the edits accumulated since the last calculation. Only
    /// meaningful when automatic calculation is off.
    /// </summary>
    public CalculationResult CalculateNow()
    {
        var start = Stopwatch.GetTimestamp();
        if (_pendingSeeds.Count == 0)
        {
            return CalculationResult.Empty;
        }

        var seeds = _pendingSeeds.ToList();
        _pendingSeeds.Clear();
        return Recalculate(seeds, [], start);
    }

    // ------------------------------------------------------------- observers

    /// <summary>Registers an observer; dispose the result to stop being notified.</summary>
    public IDisposable Subscribe(ICellChangeObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _observers.Add(observer);
        return new Subscription(this, observer);
    }

    // ----------------------------------------------------------- internals

    private CalculationResult DeferRecalculation(List<FormulaSyntaxError> syntaxErrors, long start)
    {
        // Manual mode still settles the cell the user just typed - Excel does the
        // same - but leaves everything downstream stale until CalculateNow.
        var changes = new List<CellChange>();
        var evaluated = 0;

        foreach (var id in _seeds)
        {
            _pendingSeeds.Add(id);
            ref var record = ref _cells[id];
            var previous = record.Value;
            CellValue current;
            if (record.Formula is { } formula)
            {
                _context.SheetIndex = record.SheetIndex;
                current = formula.Evaluate(_context);
                evaluated++;
            }
            else
            {
                current = record.Literal;
            }

            if (current != previous)
            {
                record.Value = current;
                changes.Add(new CellChange(AddressOf(id), previous, current));
            }
        }

        var result = new CalculationResult(changes, [], syntaxErrors, evaluated, Stopwatches.Elapsed(start));
        Notify(result);
        return result;
    }

    private CalculationResult Recalculate(IReadOnlyList<int> seeds, List<FormulaSyntaxError> syntaxErrors, long start)
    {
        _graph.TopologicalOrderFrom(seeds, _addressOf, _order, _cycleIds);

        var cycles = new List<CircularReference>(_cycleIds.Count);
        Dictionary<int, ErrorValue>? cycleErrorById = null;
        foreach (var ring in _cycleIds)
        {
            var path = new SheetCellAddress[ring.Length];
            for (var i = 0; i < ring.Length; i++)
            {
                path[i] = AddressOf(ring[i]);
            }

            var cycle = new CircularReference(path);
            cycles.Add(cycle);

            // One error object, shared by every cell in the ring.  Building the
            // message per cell would be quadratic in the length of the cycle,
            // and a 20,000-cell ring would exhaust memory rendering itself.
            cycleErrorById ??= [];
            var error = ErrorValue.Circular.WithDetail($"circular reference: {cycle}");
            foreach (var id in ring)
            {
                cycleErrorById[id] = error;
            }
        }

        var changes = new List<CellChange>();
        var evaluated = 0;

        foreach (var id in _order)
        {
            ref var record = ref _cells[id];
            var previous = record.Value;

            CellValue current;
            if (cycleErrorById is not null && cycleErrorById.TryGetValue(id, out var cycleError))
            {
                current = CellValue.FromError(cycleError);
            }
            else if (record.Formula is { } formula)
            {
                _context.SheetIndex = record.SheetIndex;
                current = formula.Evaluate(_context);
                evaluated++;
            }
            else
            {
                current = record.Literal;
            }

            if (current != previous)
            {
                record.Value = current;
                changes.Add(new CellChange(AddressOf(id), previous, current));
            }
        }

        var result = new CalculationResult(changes, cycles, syntaxErrors, evaluated, Stopwatches.Elapsed(start));
        Notify(result);
        return result;
    }

    private void ApplyContent(int id, string? content, List<FormulaSyntaxError> syntaxErrors)
    {
        _graph.ClearPrecedents(id);

        ref var record = ref _cells[id];
        record.Content = content;
        record.Formula = null;
        record.Literal = CellValue.Blank;

        if (content is null)
        {
            return;
        }

        if (CellContentClassifier.Classify(content) != CellContentKind.Formula)
        {
            record.Literal = CellContentClassifier.LiteralValue(content);
            return;
        }

        var parsed = FormulaParser.Parse(content);
        if (!parsed.Success)
        {
            syntaxErrors.AddRange(parsed.Errors);
            record.Literal = CellValue.FromError(
                ErrorValue.ParseFailure.WithDetail(parsed.Errors[0].ToString()));
            return;
        }

        record.Formula = parsed.Expression;
        RecordDependencies(id, record.SheetIndex, parsed.Expression!);
    }

    private void RecordDependencies(int id, int sheetIndex, Expression expression)
    {
        var references = ReferenceCollector.Collect(expression);
        if (references.IsEmpty)
        {
            return;
        }

        foreach (var cell in references.Cells)
        {
            if (!ResolveSheet(cell.SheetName, sheetIndex, out var target))
            {
                // A reference into a sheet that does not exist has no edge; the
                // evaluator reports #REF! and there is nothing to be notified of.
                continue;
            }

            var precedentId = GetOrCreateId(target, cell.Reference.Address);
            _graph.AddCellEdge(precedentId, id);
        }

        foreach (var range in references.Ranges)
        {
            if (ResolveSheet(range.SheetName, sheetIndex, out var target))
            {
                _graph.AddRangeEdge(target.Index, range.Range, id);
            }
        }
    }

    private bool ResolveSheet(string? sheetName, int fallbackIndex, out Sheet sheet)
    {
        if (sheetName is null)
        {
            sheet = _sheets[fallbackIndex];
            return true;
        }

        return TryGetSheet(sheetName, out sheet);
    }

    private int GetOrCreateId(Sheet sheet, CellAddress address)
    {
        if (sheet.IdByAddress.TryGetValue(address, out var existing))
        {
            return existing;
        }

        var id = _nextId++;
        if (id >= _cells.Length)
        {
            Array.Resize(ref _cells, _cells.Length * 2);
        }

        _graph.EnsureCapacity(id);

        _cells[id] = new CellRecord
        {
            SheetIndex = sheet.Index,
            Column = address.Column,
            Row = address.Row,
            Value = CellValue.Blank,
            Literal = CellValue.Blank,
        };

        sheet.IdByAddress[address] = id;
        _idByKey[Key(sheet.Index, address)] = id;
        return id;
    }

    internal bool TryGetId(int sheetIndex, CellAddress address, out int id) =>
        _idByKey.TryGetValue(Key(sheetIndex, address), out id);

    internal CellValue ValueOf(int id) => _cells[id].Value;

    internal SheetCellAddress AddressOf(int id)
    {
        ref var record = ref _cells[id];
        return new SheetCellAddress(_sheets[record.SheetIndex].Name, new CellAddress(record.Column, record.Row));
    }

    internal Sheet SheetAt(int index) => _sheets[index];

    /// <summary>The position of a sheet in the workbook; -1 when there is no such sheet.</summary>
    public bool TryGetSheetIndexPublic(string name, out int index) => TryGetSheetIndex(name, out index);

    internal bool TryGetSheetIndex(string name, out int index)
    {
        if (_sheetsByName.TryGetValue(name, out var sheet))
        {
            index = sheet.Index;
            return true;
        }

        index = -1;
        return false;
    }

    private static long Key(int sheetIndex, CellAddress address) =>
        ((long)sheetIndex << 36) | ((long)address.Column << 21) | (uint)address.Row;

    private void Notify(CalculationResult result)
    {
        if (result.Changes.Count == 0 && result.Cycles.Count == 0)
        {
            return;
        }

        foreach (var observer in _observers.ToArray())
        {
            observer.OnCellsChanged(result);
        }

        CellsChanged?.Invoke(this, new CellsChangedEventArgs(result));
    }

    private struct CellRecord
    {
        public string? Content;
        public Expression? Formula;

        /// <summary>The value of literal content; unused for formula cells.</summary>
        public CellValue Literal;

        /// <summary>The last computed value: what a client sees.</summary>
        public CellValue Value;

        public int SheetIndex;
        public int Column;
        public int Row;
    }

    private sealed class Subscription(Workbook workbook, ICellChangeObserver observer) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            workbook._observers.Remove(observer);
        }
    }
}
