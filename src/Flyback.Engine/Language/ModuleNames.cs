using Flyback.Core.Graph;

namespace Flyback.Engine.Language;

/// <summary>
/// What the text may call a module: its type id, or the short name after the last dot where only one module has it.
/// A plugin's module never takes a short name from a built-in one, so a text reads the same whichever plugins are loaded.
/// </summary>
internal sealed class ModuleNames
{
    private readonly ModuleCatalog modules;
    private readonly Dictionary<string, NodeDef> byShortName = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> ambiguous = new(StringComparer.OrdinalIgnoreCase);

    public ModuleNames(ModuleCatalog modules)
    {
        this.modules = modules;

        foreach (var def in modules.All.OrderBy(d => IsBuiltIn(d) ? 0 : 1))
        {
            var plain = ShortName(def);

            if (byShortName.TryGetValue(plain, out var held))
            {
                if (!IsBuiltIn(def) && IsBuiltIn(held)) continue;

                ambiguous.Add(plain);
            }
            else
            {
                byShortName.Add(plain, def);
            }
        }
    }

    private bool IsBuiltIn(NodeDef def) => modules.ProviderOf(def.TypeId) == NodeCatalog.BuiltInProvider;

    private static string ShortName(NodeDef def)
    {
        var dot = def.TypeId.LastIndexOf('.');

        return dot < 0 ? def.TypeId : def.TypeId[(dot + 1)..];
    }

    /// <summary>Whether a name is a module's, ambiguous or not.</summary>
    public bool Knows(string name) =>
        modules.Get(name) is not null || byShortName.ContainsKey(name) || ambiguous.Contains(name);

    /// <summary>The module a name means, or null and what to tell the writer instead.</summary>
    /// <param name="code">What kind of refusal it is, one of <see cref="IssueCode"/>.</param>
    public NodeDef? Find(string name, out string refusal, out string code)
    {
        refusal = string.Empty;
        code = string.Empty;

        if (modules.Get(name) is { } exact) return exact;

        if (ambiguous.Contains(name))
        {
            var both = modules.All
                .Where(d => d.TypeId.EndsWith('.' + name) || d.TypeId == name)
                .Where(d => IsBuiltIn(d) || !modules.All.Any(o => IsBuiltIn(o) && ShortName(o).Equals(name, StringComparison.OrdinalIgnoreCase)))
                .Select(d => d.TypeId)
                .Order(StringComparer.Ordinal);

            refusal = $"'{name}' could be {string.Join(" or ", both)}. Write the one you mean in full.";
            code = IssueCode.AmbiguousModule;
            return null;
        }

        if (byShortName.TryGetValue(name, out var def)) return def;

        refusal = $"there is no module called '{name}'.";
        code = IssueCode.UnknownModule;
        return null;
    }
}
