using CalcEngine.Core.Model;

namespace CalcEngine.Core.Commands;

/// <summary>
/// Writes content into a set of cells, remembering what was there.
/// </summary>
/// <remarks>
/// This is the only command that touches cell content; everything else — Find
/// &amp; Replace, Remove Duplicates, a paste, a bulk import — is a
/// <see cref="SetCellsCommand"/> with more edits in it, or a
/// <see cref="CompositeCommand"/> over several. Keeping one writing path means
/// there is one place where undo can be got wrong, and it is tested directly.
/// </remarks>
public sealed class SetCellsCommand : IUndoableCommand
{
    private readonly IReadOnlyList<CellEdit> _edits;
    private CellEdit[]? _previous;

    /// <summary>Creates a command that applies <paramref name="edits"/>.</summary>
    /// <param name="edits">What to write.</param>
    /// <param name="description">Menu text; a default is derived when null.</param>
    public SetCellsCommand(IReadOnlyList<CellEdit> edits, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(edits);
        _edits = edits;
        Description = description ?? Describe(edits);
    }

    /// <summary>A command that writes one cell.</summary>
    public static SetCellsCommand ForSingleCell(SheetCellAddress address, string? content) =>
        new([new CellEdit(address, content)]);

    /// <inheritdoc />
    public string Description { get; }

    /// <summary>How many cells the command writes.</summary>
    public int CellCount => _edits.Count;

    /// <inheritdoc />
    public CalculationResult Execute(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        // Captured on every execution rather than only the first, so that a
        // redo after intervening undos still restores the right content.
        var previous = new CellEdit[_edits.Count];
        for (var i = 0; i < _edits.Count; i++)
        {
            previous[i] = new CellEdit(_edits[i].Address, workbook.GetCellContent(_edits[i].Address));
        }

        _previous = previous;
        return workbook.ApplyEdits(_edits);
    }

    /// <inheritdoc />
    public CalculationResult Undo(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        if (_previous is null)
        {
            throw new InvalidOperationException("This command has not been executed, so there is nothing to undo.");
        }

        return workbook.ApplyEdits(_previous);
    }

    private static string Describe(IReadOnlyList<CellEdit> edits) => edits.Count switch
    {
        0 => "no change",
        1 => $"edit {edits[0].Address.ToA1()}",
        _ => $"edit {edits.Count} cells",
    };

    /// <inheritdoc />
    public override string ToString() => Description;
}

/// <summary>Several commands that undo and redo as one.</summary>
public sealed class CompositeCommand : IUndoableCommand
{
    private readonly IReadOnlyList<IUndoableCommand> _commands;

    /// <summary>Creates a macro command.</summary>
    public CompositeCommand(string description, IReadOnlyList<IUndoableCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        Description = description;
        _commands = commands;
    }

    /// <inheritdoc />
    public string Description { get; }

    /// <summary>The commands, in execution order.</summary>
    public IReadOnlyList<IUndoableCommand> Commands => _commands;

    /// <inheritdoc />
    public CalculationResult Execute(Workbook workbook)
    {
        var results = new List<CalculationResult>(_commands.Count);
        foreach (var command in _commands)
        {
            results.Add(command.Execute(workbook));
        }

        return CalculationResult.Merge(results);
    }

    /// <inheritdoc />
    public CalculationResult Undo(Workbook workbook)
    {
        // Reverse order: a later command may depend on what an earlier one did.
        var results = new List<CalculationResult>(_commands.Count);
        for (var i = _commands.Count - 1; i >= 0; i--)
        {
            results.Add(_commands[i].Undo(workbook));
        }

        return CalculationResult.Merge(results);
    }

    /// <inheritdoc />
    public override string ToString() => Description;
}
