namespace CalcEngine.Core.Commands;

/// <summary>Placeholder for the undo/redo history; implemented in the next commit.</summary>
public sealed class CommandStack
{
    internal CommandStack(int limit) => Limit = limit;

    /// <summary>How many operations the history keeps.</summary>
    public int Limit { get; }
}
