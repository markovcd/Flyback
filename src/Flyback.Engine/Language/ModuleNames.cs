using Flyback.Core.Graph;

namespace Flyback.Core.Language;

/// <summary>What the text may call a module: its type id, or the short name after the last dot where only one module has it.</summary>
internal sealed class ModuleNames
{
    private readonly ModuleCatalog modules;
    private readonly Dictionary<string, NodeDef> byShortName = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> ambiguous = new(StringComparer.OrdinalIgnoreCase);

    public ModuleNames(ModuleCatalog modules)
    {
        this.modules = modules;

        foreach (var def in modules.All)
        {
            var dot = def.TypeId.LastIndexOf('.');
            var plain = dot < 0 ? def.TypeId : def.TypeId[(dot + 1)..];

            if (!byShortName.TryAdd(plain, def)) ambiguous.Add(plain);
        }
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
