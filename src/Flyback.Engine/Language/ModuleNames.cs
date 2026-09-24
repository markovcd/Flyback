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
    /// <param name="nearest">The name there is that is closest to it, where one is close enough.</param>
    public NodeDef? Find(string name, out string refusal, out string code, out string? nearest)
    {
        refusal = string.Empty;
        code = string.Empty;
        nearest = null;

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

        nearest = Nearest(name);
        refusal = $"there is no module called '{name}'.{(nearest is null ? string.Empty : $" Did you mean '{nearest}'?")}";
        code = IssueCode.UnknownModule;
        return null;
    }

    /// <summary>The closest name there is, where one is close enough to be worth offering.</summary>
    private string? Nearest(string name)
    {
        var best = byShortName.Keys
            .Select(k => (Name: k, Distance: Distance(k, name)))
            .Where(k => k.Distance <= Math.Max(1, name.Length / 3))
            .OrderBy(k => k.Distance)
            .ThenBy(k => k.Name, StringComparer.Ordinal)
            .Select(k => k.Name)
            .FirstOrDefault();

        return best;
    }

    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= b.Length; j++)
            {
                var swap = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;

                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + swap);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
