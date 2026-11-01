using Flyback.Engine.Language.Ast.Statements;
using Flyback.Engine.Language.Values;

namespace Flyback.Engine.Language;

/// <summary>Names and defs in sight, and the ones the enclosing scope had.</summary>
/// <param name="parent">Where names and defs not bound here are looked for.</param>
/// <param name="declaredIn">
/// For a def's body, the scope the def was written in: its defs are in sight, and none of its names.
/// </param>
internal sealed class Scope(Scope? parent, Scope? declaredIn = null)
{
    private readonly Dictionary<string, (Value Value, int Line)> names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (DefStatement Def, Scope Where)> defs = new(StringComparer.Ordinal);

    /// <param name="line">Where the name is bound, for a second binding to point back at.</param>
    public void Set(string name, Value value, int line) => names[name] = (value, line);

    public Value? Find(string name) => Entry(name)?.Value;

    public (Value Value, int Line)? Entry(string name) =>
        names.TryGetValue(name, out var entry) ? entry : parent?.Entry(name);

    /// <summary>Puts a def in sight here and in every scope inside this one.</summary>
    public void Declare(DefStatement def) => defs[def.Name] = (def, this);

    /// <summary>The def a name calls, and the scope it was written in, which its body sees the defs of.</summary>
    public (DefStatement Def, Scope Where)? Def(string name) =>
        defs.TryGetValue(name, out var found) ? found : (parent ?? declaredIn)?.Def(name);

    /// <summary>Hands the defs declared here to <paramref name="outer"/>, for a group, whose defs stay in sight after it.</summary>
    public void LiftDefs(Scope outer)
    {
        foreach (var (name, (def, _)) in defs) outer.defs[name] = (def, outer);
    }
}
