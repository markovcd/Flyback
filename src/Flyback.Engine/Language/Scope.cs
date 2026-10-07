using Flyback.Engine.Language.Values;

namespace Flyback.Engine.Language;

/// <summary>Names in sight, and the names the enclosing scope had.</summary>
internal sealed class Scope(Scope? parent)
{
    private readonly Dictionary<string, (Value Value, int Line)> names = new(StringComparer.Ordinal);

    /// <param name="line">Where the name is bound, for a second binding to point back at.</param>
    public void Set(string name, Value value, int line) => names[name] = (value, line);

    public Value? Find(string name) => Entry(name)?.Value;

    public (Value Value, int Line)? Entry(string name) =>
        names.TryGetValue(name, out var entry) ? entry : parent?.Entry(name);
}
