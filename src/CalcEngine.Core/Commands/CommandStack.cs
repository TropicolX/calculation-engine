namespace CalcEngine.Core.Commands;

/// <summary>
/// The undo/redo history: a bounded stack of executed commands and a stack of
/// undone ones.
/// </summary>
/// <remarks>
/// <para><b>Abstraction function.</b> AF(h) = the sequence of operations
/// performed on the workbook and not yet undone (<c>_undo</c>, oldest first),
/// together with the sequence undone and not yet redone (<c>_redo</c>, most
/// recently undone last).</para>
///
/// <para><b>Representation invariant.</b>
/// <c>0 &lt;= _undo.Count &lt;= Limit</c>;
/// every command in either stack has been executed at least once, so its
/// captured "previous content" is valid;
/// <c>_redo</c> is empty immediately after any call to
/// <see cref="Execute(IUndoableCommand)"/>.</para>
///
/// <para>The brief asks for at least 100 operations. The limit is a
/// <em>count of operations</em>, not of cells: replacing 4,000 occurrences of a
/// course code is one operation and takes one press of Ctrl+Z.</para>
/// </remarks>
public sealed class CommandStack
{
    private readonly Workbook _workbook;
    private readonly List<IUndoableCommand> _undo = [];
    private readonly List<IUndoableCommand> _redo = [];
    private int _suspendDepth;

    internal CommandStack(Workbook workbook, int limit)
    {
        if (limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "The history must hold at least one operation.");
        }

        _workbook = workbook;
        Limit = limit;
    }

    /// <summary>How many operations the history keeps.</summary>
    public int Limit { get; }

    /// <summary>How many operations can be undone.</summary>
    public int UndoCount => _undo.Count;

    /// <summary>How many operations can be redone.</summary>
    public int RedoCount => _redo.Count;

    /// <summary>True when there is something to undo.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>True when there is something to redo.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Menu text for the next undo, or null.</summary>
    public string? NextUndoDescription => CanUndo ? _undo[^1].Description : null;

    /// <summary>Menu text for the next redo, or null.</summary>
    public string? NextRedoDescription => CanRedo ? _redo[^1].Description : null;

    /// <summary>True while a <see cref="Suspend"/> scope is open.</summary>
    public bool IsSuspended => _suspendDepth > 0;

    /// <summary>Raised whenever the history changes, so a UI can re-enable its buttons.</summary>
    public event EventHandler? Changed;

    /// <summary>Runs a command and records it, unless the history is suspended.</summary>
    public CalculationResult Execute(IUndoableCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = command.Execute(_workbook);
        if (IsSuspended)
        {
            return result;
        }

        // An operation that changed nothing is not an operation: recording it
        // would make Ctrl+Z appear to do nothing, which users read as a bug.
        if (result.Changes.Count == 0 && result.Cycles.Count == 0)
        {
            return result;
        }

        _undo.Add(command);
        if (_undo.Count > Limit)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    /// <summary>Reverses the most recent operation.</summary>
    public CalculationResult Undo()
    {
        if (!CanUndo)
        {
            return CalculationResult.Empty;
        }

        var command = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        var result = command.Undo(_workbook);
        _redo.Add(command);
        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    /// <summary>Re-applies the most recently undone operation.</summary>
    public CalculationResult Redo()
    {
        if (!CanRedo)
        {
            return CalculationResult.Empty;
        }

        var command = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        var result = command.Execute(_workbook);
        _undo.Add(command);
        if (_undo.Count > Limit)
        {
            _undo.RemoveAt(0);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    /// <summary>Forgets everything. Used after a load, when there is nothing to go back to.</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens a scope in which edits are applied but not recorded.
    /// </summary>
    /// <remarks>
    /// A CSV import of 40,000 rows should not fill the history with 40,000
    /// entries that push out everything the user actually did. Scopes nest.
    /// </remarks>
    public IDisposable Suspend()
    {
        _suspendDepth++;
        return new SuspendScope(this);
    }

    private sealed class SuspendScope(CommandStack stack) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            stack._suspendDepth--;
        }
    }
}
