namespace Flyback.Plugins.Hosting;

/// <summary>
/// Turns plugin types into a <see cref="PluginCatalog"/>: each instantiated and registered in turn,
/// rolled back whole if it throws or offers a module it did not declare, and an id two of them
/// offer refused to both once all have had their turn.
/// </summary>
internal sealed class PluginCatalogBuilder
{
    private readonly List<LoadedPlugin> plugins = [];
    private readonly List<PluginProblem> problems = [];
    private readonly PluginRegistry registry;

    public PluginCatalogBuilder() => registry = new(problems);

    /// <summary>How many plugins have loaded so far.</summary>
    public int Loaded => plugins.Count;

    public IReadOnlyList<PluginProblem> Problems => problems;

    /// <summary>Whether the plugins added next may register a secret store.</summary>
    public bool Secrets
    {
        set => registry.Secrets = value;
    }

    public void Problem(PluginProblem problem) => problems.Add(problem);

    /// <summary>Puts <paramref name="folder"/> on every problem from the <paramref name="since"/>th on.</summary>
    public void Blame(int since, string folder)
    {
        for (var i = since; i < problems.Count; i++) problems[i] = problems[i] with { Folder = folder };
    }

    /// <summary>Instantiates <paramref name="type"/>, loaded from <paramref name="path"/>, and lets it register.</summary>
    public void Add(Type type, string path)
    {
        PluginRegistry.Checkpoint? mark = null;

        try
        {
            var plugin = (IFlybackPlugin)Activator.CreateInstance(type)!;
            var info = plugin.Info;

            if (plugins.Any(p => p.Info.Id == info.Id))
            {
                problems.Add(new PluginProblem(
                    Path.GetFileName(path),
                    $"ignored — a plugin with id '{info.Id}' is already loaded."));
                return;
            }

            registry.Source = info;
            var checkpoint = registry.Mark();
            mark = checkpoint;
            plugin.Register(registry);

            // Its code has run by now, but nothing it registered is kept unless it was declared.
            if (ModuleDeclarations.Mismatch(ModuleDeclarations.Of(type.Assembly), registry.OfferedSince(checkpoint)) is { } mismatch)
            {
                registry.Restore(checkpoint);
                problems.Add(new PluginProblem(Path.GetFileName(path), mismatch));
                return;
            }

            plugins.Add(new LoadedPlugin(info, path));
        }
        catch (Exception ex)
        {
            // A plugin that did not finish registering is not loaded, so nothing it registered is kept.
            if (mark is { } undo) registry.Restore(undo);

            problems.Add(new PluginProblem(type.Name, ex.Message));
        }
    }

    public PluginCatalog Build()
    {
        var providers = registry.Providers;

        return new(
            plugins,
            Unclashed(registry.AudioOutputs, o => o.Id, "audio output"),
            registry.Modules,
            registry.Presets,
            problems,
            Unclashed(registry.Assistants, a => a.Id, "assistant"),
            Unclashed(registry.SecretStores, s => s.Id, "secret store"),
            Unclashed(registry.MidiInputs, i => i.Id, "MIDI input"),
            providers,
            Unclashed(registry.AudioInputs, i => i.Id, "audio input"),
            Unclashed(registry.DecisionModels, m => m.Id, "decision model"));
    }

    /// <summary>
    /// Everything offered under an id nobody else offered. An id offered twice is refused
    /// to both, so a folder that loads first cannot stand in for a plugin it shares an id with.
    /// </summary>
    private List<T> Unclashed<T>(IReadOnlyList<T> offered, Func<T, string> id, string what) where T : notnull
    {
        var providers = registry.Providers;
        var kept = new List<T>();

        foreach (var group in offered.GroupBy(id, StringComparer.Ordinal))
        {
            if (group.Count() == 1)
            {
                kept.Add(group.First());
                continue;
            }

            var by = group.Select(o => providers.GetValueOrDefault(o) ?? new PluginInfo("", "")).ToList();

            foreach (var source in by)
            {
                var others = by.Where(o => !ReferenceEquals(o, source)).Select(o => o.Name).Distinct().ToList();
                var also = others.Count == 0 ? "more than once by the same plugin" : $"by {string.Join(" and ", others)} as well";
                var folder = plugins.FirstOrDefault(p => ReferenceEquals(p.Info, source))?.AssemblyPath is { } path ? Path.GetDirectoryName(path) : null;

                problems.Add(new PluginProblem(source.Id, $"{what} '{group.Key}' is registered {also}, so neither is used.") { Folder = folder });
            }
        }

        return kept;
    }
}
