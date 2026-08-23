namespace CalcEngine.Core.Model;

/// <summary>The seven error conditions the engine can report, plus the parse failure.</summary>
public enum ErrorKind
{
    /// <summary>Division by zero: <c>#DIV/0!</c>.</summary>
    DivideByZero,

    /// <summary>An operand of the wrong type: <c>#VALUE!</c>.</summary>
    Value,

    /// <summary>A reference that cannot be resolved: <c>#REF!</c>.</summary>
    Reference,

    /// <summary>An unknown function or name: <c>#NAME?</c>.</summary>
    Name,

    /// <summary>A numeric result that does not exist: <c>#NUM!</c>.</summary>
    Number,

    /// <summary>A lookup that found nothing: <c>#N/A</c>.</summary>
    NotAvailable,

    /// <summary>A cell that takes part in a dependency cycle: <c>#CIRC!</c>.</summary>
    Circular,

    /// <summary>Cell content beginning with <c>=</c> that does not parse: <c>#PARSE!</c>.</summary>
    Parse,
}

/// <summary>
/// A spreadsheet error value: a first-class result, never an exception.
/// </summary>
/// <remarks>
/// <para><b>Abstraction function.</b> AF(e) = the error condition
/// <c>e.Kind</c>, displayed as <c>e.Code</c>. <c>e.Detail</c> is diagnostic
/// prose for the client (a cycle path, an unknown function name) and is
/// <em>not</em> part of the abstract value.</para>
///
/// <para><b>Representation invariant.</b> <c>Code</c> is the canonical spelling
/// of <c>Kind</c> and is non-empty; instances are immutable.</para>
///
/// <para>Equality is by <see cref="Kind"/> alone. Two <c>#REF!</c> cells hold
/// the same value even if one explains itself more fully than the other, which
/// keeps the change-notification path from reporting a spurious change every
/// time the wording of a diagnostic differs.</para>
/// </remarks>
public sealed class ErrorValue : IEquatable<ErrorValue>
{
    private ErrorValue(ErrorKind kind, string code, string? detail)
    {
        Kind = kind;
        Code = code;
        Detail = detail;
    }

    /// <summary>Which error this is.</summary>
    public ErrorKind Kind { get; }

    /// <summary>The spelling shown in a cell, for example <c>#DIV/0!</c>.</summary>
    public string Code { get; }

    /// <summary>Optional human-readable explanation; not part of the value.</summary>
    public string? Detail { get; }

    /// <summary><c>#DIV/0!</c></summary>
    public static ErrorValue DivideByZero { get; } = new(ErrorKind.DivideByZero, "#DIV/0!", null);

    /// <summary><c>#VALUE!</c></summary>
    public static ErrorValue Value { get; } = new(ErrorKind.Value, "#VALUE!", null);

    /// <summary><c>#REF!</c></summary>
    public static ErrorValue Reference { get; } = new(ErrorKind.Reference, "#REF!", null);

    /// <summary><c>#NAME?</c></summary>
    public static ErrorValue Name { get; } = new(ErrorKind.Name, "#NAME?", null);

    /// <summary><c>#NUM!</c></summary>
    public static ErrorValue Number { get; } = new(ErrorKind.Number, "#NUM!", null);

    /// <summary><c>#N/A</c></summary>
    public static ErrorValue NotAvailable { get; } = new(ErrorKind.NotAvailable, "#N/A", null);

    /// <summary><c>#CIRC!</c></summary>
    public static ErrorValue Circular { get; } = new(ErrorKind.Circular, "#CIRC!", null);

    /// <summary><c>#PARSE!</c></summary>
    public static ErrorValue ParseFailure { get; } = new(ErrorKind.Parse, "#PARSE!", null);

    /// <summary>Every canonical error value, in declaration order.</summary>
    public static IReadOnlyList<ErrorValue> All { get; } =
    [
        DivideByZero, Value, Reference, Name, Number, NotAvailable, Circular, ParseFailure,
    ];

    // NOTE: All is indexed by ErrorKind; the two orders must stay in step.

    /// <summary>The canonical error value for <paramref name="kind"/>, without detail.</summary>
    public static ErrorValue For(ErrorKind kind) => All[(int)kind];

    /// <summary>Recognises an error by its spelling, for example <c>#N/A</c>.</summary>
    public static bool TryParse(string? code, out ErrorValue error)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(candidate.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                error = candidate;
                return true;
            }
        }

        error = Value;
        return false;
    }

    /// <summary>Returns the same error carrying a diagnostic message for the client.</summary>
    public ErrorValue WithDetail(string detail) => new(Kind, Code, detail);

    /// <inheritdoc />
    public bool Equals(ErrorValue? other) => other is not null && Kind == other.Kind;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ErrorValue);

    /// <inheritdoc />
    public override int GetHashCode() => (int)Kind;

    /// <inheritdoc />
    public override string ToString() => Detail is null ? Code : $"{Code} ({Detail})";
}
