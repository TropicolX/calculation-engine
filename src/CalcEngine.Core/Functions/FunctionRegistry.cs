using System.Diagnostics.CodeAnalysis;

namespace CalcEngine.Core.Functions;

/// <summary>
/// The function library: a name-to-<see cref="IFunction"/> lookup that a client
/// can extend.
/// </summary>
/// <remarks>
/// <para>This is the Factory half of the Strategy/Factory pairing.
/// <see cref="CreateDefault"/> builds the standard library;
/// <see cref="Register(IFunction, bool)"/> lets the results portal add its own
/// rules — a <c>CAWEIGHT</c> that encodes the departmental 30/70 split, say —
/// without forking the engine or touching the grammar, because the grammar
/// already accepts any identifier followed by <c>(</c>.</para>
///
/// <para>Registering over an existing name requires saying so explicitly. A
/// client that shadows <c>SUM</c> by accident would produce a workbook whose
/// numbers cannot be reproduced anywhere else, and that is worth one extra
/// argument to prevent.</para>
/// </remarks>
public sealed class FunctionRegistry
{
    private readonly Dictionary<string, IFunction> _functions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates an empty registry.</summary>
    public FunctionRegistry()
    {
    }

    /// <summary>The names currently registered, sorted.</summary>
    public IReadOnlyList<string> Names => _functions.Keys.Order(StringComparer.Ordinal).ToList();

    /// <summary>Every registered function, sorted by name.</summary>
    public IReadOnlyList<IFunction> Functions =>
        _functions.Values.OrderBy(f => f.Name, StringComparer.Ordinal).ToList();

    /// <summary>Builds a registry holding the standard library.</summary>
    public static FunctionRegistry CreateDefault()
    {
        var registry = new FunctionRegistry();
        foreach (var function in StandardLibrary.All())
        {
            registry.Register(function);
        }

        return registry;
    }

    /// <summary>Adds a function.</summary>
    /// <param name="function">The implementation.</param>
    /// <param name="replaceExisting">Pass true to deliberately shadow a built-in.</param>
    /// <exception cref="InvalidOperationException">The name is taken and <paramref name="replaceExisting"/> is false.</exception>
    public void Register(IFunction function, bool replaceExisting = false)
    {
        ArgumentNullException.ThrowIfNull(function);
        if (!replaceExisting && _functions.ContainsKey(function.Name))
        {
            throw new InvalidOperationException(
                $"A function called '{function.Name}' is already registered. " +
                "Pass replaceExisting: true if shadowing it is intended.");
        }

        _functions[function.Name] = function;
    }

    /// <summary>Removes a function; returns false if it was not registered.</summary>
    public bool Unregister(string name) => _functions.Remove(name);

    /// <summary>Resolves a function by name, case-insensitively.</summary>
    public bool TryGet(string name, [MaybeNullWhen(false)] out IFunction function) =>
        _functions.TryGetValue(name, out function);
}
