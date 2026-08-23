namespace CalcEngine.Core.Commands;

/// <summary>
/// One reversible editing operation — the Command pattern.
/// </summary>
/// <remarks>
/// <para>A command records what it did, not what the workbook looked like: the
/// history holds one entry per operation, not a snapshot per operation, so a
/// hundred-deep history of a 100,000-cell workbook costs a hundred small
/// objects rather than ten million cells.</para>
///
/// <para>Both directions return a <see cref="CalculationResult"/>, because
/// undoing an edit is an edit: it re-enters the same recalculation path, which
/// is why undoing a formula restores its dependency edges as well as its text.
/// </para>
/// </remarks>
public interface IUndoableCommand
{
    /// <summary>What to show next to "Undo" in a menu, for example <c>edit Sheet1!B2</c>.</summary>
    string Description { get; }

    /// <summary>Performs the operation.</summary>
    CalculationResult Execute(Workbook workbook);

    /// <summary>Reverses it, restoring exactly what was there before.</summary>
    CalculationResult Undo(Workbook workbook);
}
