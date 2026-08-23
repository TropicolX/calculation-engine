using System.Globalization;

namespace CalcEngine.Core.Model;

/// <summary>The five variants a cell value can take.</summary>
public enum CellValueKind
{
    /// <summary>An empty cell. The default.</summary>
    Blank,

    /// <summary>A double-precision number. Dates and booleans coerce to this.</summary>
    Number,

    /// <summary>A string.</summary>
    Text,

    /// <summary>TRUE or FALSE.</summary>
    Boolean,

    /// <summary>An error value such as <c>#DIV/0!</c>.</summary>
    Error,
}

/// <summary>
/// The value of a cell: a tagged union of blank, number, text, boolean and error.
/// </summary>
/// <remarks>
/// <para><b>Abstraction function.</b>
/// AF(v) = blank when <c>v.Kind = Blank</c>; the real number <c>v._number</c>
/// when <c>Number</c>; the string <c>(string)v._payload</c> when <c>Text</c>;
/// the truth value <c>v._number != 0</c> when <c>Boolean</c>; and the error
/// <c>(ErrorValue)v._payload</c> when <c>Error</c>.</para>
///
/// <para><b>Representation invariant.</b>
/// <c>Kind = Text ⇒ _payload is string</c>;
/// <c>Kind = Error ⇒ _payload is ErrorValue</c>;
/// <c>Kind ∈ {Blank, Number, Boolean} ⇒ _payload is null</c>;
/// <c>Kind = Boolean ⇒ _number ∈ {0, 1}</c>;
/// <c>Kind = Number ⇒ _number is finite</c> (the evaluator maps NaN and
/// infinity to <c>#NUM!</c> and <c>#DIV/0!</c> before a value is ever built).</para>
///
/// <para>This is a <c>readonly struct</c> with one <c>double</c> and one
/// reference field: 16 bytes, no allocation for the numeric case that dominates
/// a workbook. That choice is what lets a 100,000-cell recalculation stay
/// inside the 2-second budget without a garbage-collection pause.</para>
///
/// <para>Reading the wrong variant (<see cref="AsNumber"/> on text) throws
/// <see cref="InvalidOperationException"/>: that is a bug in the caller, not a
/// spreadsheet error. Spreadsheet-level type confusion is expressed by
/// returning <c>#VALUE!</c>, never by throwing.</para>
/// </remarks>
public readonly struct CellValue : IEquatable<CellValue>
{
    private readonly double _number;
    private readonly object? _payload;

    private CellValue(CellValueKind kind, double number, object? payload)
    {
        Kind = kind;
        _number = number;
        _payload = payload;
    }

    /// <summary>Which variant this value is.</summary>
    public CellValueKind Kind { get; }

    /// <summary>The empty value; also <c>default(CellValue)</c>.</summary>
    public static CellValue Blank => default;

    /// <summary>The boolean TRUE.</summary>
    public static CellValue True { get; } = Boolean(true);

    /// <summary>The boolean FALSE.</summary>
    public static CellValue False { get; } = Boolean(false);

    /// <summary>Zero, the value a blank cell contributes to arithmetic.</summary>
    public static CellValue Zero { get; } = Number(0);

    /// <summary>Builds a numeric value.</summary>
    public static CellValue Number(double value) => new(CellValueKind.Number, value, null);

    /// <summary>Builds a text value.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static CellValue Text(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new CellValue(CellValueKind.Text, 0, value);
    }

    /// <summary>Builds a boolean value.</summary>
    public static CellValue Boolean(bool value) => new(CellValueKind.Boolean, value ? 1 : 0, null);

    /// <summary>Builds an error value.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
    public static CellValue FromError(ErrorValue error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new CellValue(CellValueKind.Error, 0, error);
    }

    /// <summary>Builds an error value from its kind.</summary>
    public static CellValue FromError(ErrorKind kind) => FromError(ErrorValue.For(kind));

    /// <summary>True when the cell is empty.</summary>
    public bool IsBlank => Kind == CellValueKind.Blank;

    /// <summary>True when the value is an error.</summary>
    public bool IsError => Kind == CellValueKind.Error;

    /// <summary>True when the value is a number.</summary>
    public bool IsNumber => Kind == CellValueKind.Number;

    /// <summary>True when the value is text.</summary>
    public bool IsText => Kind == CellValueKind.Text;

    /// <summary>True when the value is a boolean.</summary>
    public bool IsBoolean => Kind == CellValueKind.Boolean;

    /// <summary>The number held by a <see cref="CellValueKind.Number"/> value.</summary>
    /// <exception cref="InvalidOperationException">This value is not a number.</exception>
    public double AsNumber => Kind == CellValueKind.Number
        ? _number
        : throw WrongVariant(CellValueKind.Number);

    /// <summary>The string held by a <see cref="CellValueKind.Text"/> value.</summary>
    /// <exception cref="InvalidOperationException">This value is not text.</exception>
    public string AsText => Kind == CellValueKind.Text
        ? (string)_payload!
        : throw WrongVariant(CellValueKind.Text);

    /// <summary>The truth value held by a <see cref="CellValueKind.Boolean"/> value.</summary>
    /// <exception cref="InvalidOperationException">This value is not a boolean.</exception>
    public bool AsBoolean => Kind == CellValueKind.Boolean
        ? _number != 0
        : throw WrongVariant(CellValueKind.Boolean);

    /// <summary>The error held by a <see cref="CellValueKind.Error"/> value.</summary>
    /// <exception cref="InvalidOperationException">This value is not an error.</exception>
    public ErrorValue AsError => Kind == CellValueKind.Error
        ? (ErrorValue)_payload!
        : throw WrongVariant(CellValueKind.Error);

    /// <summary>
    /// How the value appears in a grid cell: blank as the empty string, booleans
    /// upper case, errors as their code, numbers in the invariant culture so that
    /// the same workbook reads the same way on every machine.
    /// </summary>
    public string ToDisplayString() => Kind switch
    {
        CellValueKind.Blank => string.Empty,
        CellValueKind.Number => _number.ToString(CultureInfo.InvariantCulture),
        CellValueKind.Text => (string)_payload!,
        CellValueKind.Boolean => _number != 0 ? "TRUE" : "FALSE",
        CellValueKind.Error => ((ErrorValue)_payload!).Code,
        _ => string.Empty,
    };

    /// <inheritdoc />
    public bool Equals(CellValue other)
    {
        if (Kind != other.Kind)
        {
            return false;
        }

        return Kind switch
        {
            CellValueKind.Blank => true,
            CellValueKind.Number or CellValueKind.Boolean => _number.Equals(other._number),
            CellValueKind.Text => string.Equals((string)_payload!, (string)other._payload!, StringComparison.Ordinal),
            CellValueKind.Error => ((ErrorValue)_payload!).Equals((ErrorValue)other._payload!),
            _ => false,
        };
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CellValue other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Kind switch
    {
        CellValueKind.Blank => 0,
        CellValueKind.Number or CellValueKind.Boolean => HashCode.Combine(Kind, _number),
        CellValueKind.Text => HashCode.Combine(Kind, (string)_payload!),
        CellValueKind.Error => HashCode.Combine(Kind, (ErrorValue)_payload!),
        _ => 0,
    };

    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        CellValueKind.Blank => "(blank)",
        CellValueKind.Text => $"\"{(string)_payload!}\"",
        _ => ToDisplayString(),
    };

    private InvalidOperationException WrongVariant(CellValueKind expected) =>
        new($"This CellValue is {Kind}, not {expected}.");

    public static bool operator ==(CellValue left, CellValue right) => left.Equals(right);

    public static bool operator !=(CellValue left, CellValue right) => !left.Equals(right);
}
