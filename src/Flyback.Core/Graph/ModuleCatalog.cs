namespace Flyback.Core.Graph;

/// <summary>
/// Who a set of modules came from. Written into saved patches, so a file names
/// what it needs by both id and title — the title matters because a missing
/// plugin cannot be looked up to find out what it was called.
/// </summary>
public sealed record ModuleProvider(string Id, string Name);

/// <summary>
/// A catalogue with a provider folded in, and anything that provider was
/// refused. Refusals are values rather than exceptions: one bad module in a
/// plugin should cost that module, not the plugin and not the program.
/// </summary>
public sealed record ModuleAddition(ModuleCatalog Catalog, IReadOnlyList<string> Rejected);

/// <summary>
/// The set of modules that exist. Immutable: adding a provider produces a new
/// catalogue rather than mutating this one, so what a patch was compiled
/// against cannot change underneath it.
/// </summary>
/// <remarks>
/// A plugin's module type ids must begin with its provider id and a dot. That
/// single rule does three jobs: it makes shadowing a built-in impossible, it
/// makes collisions between two plugins impossible, and it means the provider
/// of a module can be read off a saved file without having the plugin that
/// defines it.
/// </remarks>
public sealed class ModuleCatalog
{
    private readonly Dictionary<string, NodeDef> index;
    private readonly Dictionary<string, ModuleProvider> owners;

    private ModuleCatalog(
        IReadOnlyList<NodeDef> all,
        IReadOnlyList<ModuleProvider> providers,
        Dictionary<string, ModuleProvider> owners)
    {
        All = all;
        Providers = providers;
        this.owners = owners;
        index = all.ToDictionary(d => d.TypeId);
    }

    internal static ModuleCatalog Of(ModuleProvider provider, IReadOnlyList<NodeDef> modules) =>
        new(modules, [provider], modules.ToDictionary(m => m.TypeId, _ => provider));

    public IReadOnlyList<NodeDef> All { get; }

    public IReadOnlyList<ModuleProvider> Providers { get; }

    public IEnumerable<string> Categories => All.Select(d => d.Category).Distinct();

    public NodeDef? Get(string typeId) => index.GetValueOrDefault(typeId);

    public NodeDef Require(string typeId) =>
        Get(typeId) ?? throw new KeyNotFoundException($"Unknown node type '{typeId}'.");

    /// <summary>Which provider defines a module, or null if nothing here does.</summary>
    public ModuleProvider? ProviderOf(string typeId) => owners.GetValueOrDefault(typeId);

    /// <summary>
    /// What is driving a socket nothing is patched into, named as it should be
    /// written out, or null where the socket is on its own knob.
    /// </summary>
    /// <remarks>
    /// One place, because the node on the canvas, the row in the inspector and
    /// the line an assistant reads have to agree about what an unpatched socket
    /// is reading — the same reason <see cref="PortSpec.Format"/> is one place.
    /// <para>
    /// Asked of a catalogue rather than of the running one, because whether a
    /// normal holds depends on which catalogue the patch is being compiled
    /// against: a socket normalled to a plugin's module is on its knob wherever
    /// that plugin is not loaded, and saying otherwise would be the one reading
    /// nothing here could correct.
    /// </para>
    /// </remarks>
    public string? Normalled(PortSpec spec)
    {
        if (spec.NormalledTo is not { } bus || Get(bus.TypeId) is not { } def) return null;
        if (bus.Port < 0 || bus.Port >= def.Outputs.Count) return null;

        // The module alone where it has one output and there is nothing to pick
        // between, both where it has several: "Time" says all there is to say,
        // and "Coordinates" on its own would not tell x from radius.
        return def.Outputs.Count == 1 ? def.Name : $"{def.Name} {def.Outputs[bus.Port].Name}";
    }

    public bool HasProvider(string providerId) => Providers.Any(p => p.Id == providerId);

    /// <summary>
    /// Folds a provider's modules in. The provider appears only if at least one
    /// of its modules was accepted, so a patch can never come to require a
    /// plugin that contributed nothing.
    /// </summary>
    public ModuleAddition With(ModuleProvider provider, IReadOnlyList<NodeDef> modules)
    {
        if (Refuse(provider) is { } refusal) return new ModuleAddition(this, [refusal]);

        var prefix = provider.Id + ".";
        var accepted = new List<NodeDef>();
        var rejected = new List<string>();

        foreach (var module in modules)
        {
            if (!module.TypeId.StartsWith(prefix, StringComparison.Ordinal))
            {
                rejected.Add($"'{module.TypeId}' is not named '{prefix}…' and was ignored.");
                continue;
            }

            if (index.ContainsKey(module.TypeId) || accepted.Any(m => m.TypeId == module.TypeId))
            {
                rejected.Add($"'{module.TypeId}' is defined twice and the later one was ignored.");
                continue;
            }

            accepted.Add(module);
        }

        if (accepted.Count == 0) return new ModuleAddition(this, rejected);

        var owned = new Dictionary<string, ModuleProvider>(owners);
        foreach (var module in accepted) owned[module.TypeId] = provider;

        return new ModuleAddition(
            new ModuleCatalog([.. All, .. accepted], [.. Providers, provider], owned),
            rejected);
    }

    /// <summary>
    /// Why a provider cannot be added at all, or null if it can. Claiming a
    /// prefix that is already in use is the dangerous one: it would let a plugin
    /// answer for module ids that saved patches already mean something else by.
    /// </summary>
    private string? Refuse(ModuleProvider provider)
    {
        if (string.IsNullOrWhiteSpace(provider.Id))
            return "a provider with no id was ignored.";

        if (provider.Id == NodeCatalog.BuiltInProvider.Id)
            return $"'{provider.Id}' is reserved for the modules that ship with the engine.";

        if (HasProvider(provider.Id))
            return $"'{provider.Id}' is already loaded and the second one was ignored.";

        return All.Any(d => d.TypeId.StartsWith(provider.Id + ".", StringComparison.Ordinal))
            ? $"'{provider.Id}' would claim module ids that already exist."
            : null;
    }
}
