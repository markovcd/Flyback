using System.Reflection;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Midi;
using Flyback.Plugins.Secrets;

namespace Flyback.Plugins.Hosting;

/// <summary>
/// Finds and loads plugins. One scan, at startup — there is no reload, because an
/// assembly the audio thread is calling into cannot be unloaded safely.
/// </summary>
/// <remarks>
/// One folder per plugin under <c>plugins/</c>, each holding the plugin assembly,
/// its <c>.deps.json</c> and its private dependencies. Nothing here throws: a
/// plugin that is missing, broken, built against another runtime or against a
/// contract this host does not offer (<see cref="ContractVersion"/>), or simply
/// hostile is a line in <see cref="PluginCatalog.Problems"/>.
/// </remarks>
public static class PluginHost
{
    public const string DirectoryName = "plugins";

    /// <summary>The <c>plugins</c> folder beside the executable.</summary>
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, DirectoryName);

    public static PluginCatalog Load() => Load(DefaultDirectory);

    public static PluginCatalog Load(string directory)
    {
        if (!Directory.Exists(directory)) return PluginCatalog.Empty;

        var plugins = new List<LoadedPlugin>();
        var problems = new List<PluginProblem>();
        var registry = new Registry(problems);

        foreach (var folder in Folders(directory)) LoadFolder(folder, plugins, registry, problems);

        return Catalog(plugins, registry, problems);
    }

    /// <summary>
    /// Loads the plugins in one folder alone and unloads them again, as <c>pack-plugin</c>
    /// tries a build: what went wrong, and how many plugins loaded.
    /// </summary>
    internal static (IReadOnlyList<PluginProblem> Problems, int Loaded) Try(string folder)
    {
        var contexts = new List<PluginLoadContext>();
        var result = Tried(folder, contexts);

        foreach (var context in contexts) context.Unload();

        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        return result;
    }

    /// <summary>Kept apart from <see cref="Try"/> so nothing it loaded is still referenced once it returns.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (IReadOnlyList<PluginProblem> Problems, int Loaded) Tried(string folder, List<PluginLoadContext> contexts)
    {
        var plugins = new List<LoadedPlugin>();
        var problems = new List<PluginProblem>();

        LoadFolder(folder, plugins, new Registry(problems), problems, contexts);

        return (problems, plugins.Count);
    }

    /// <summary>Loads plugin types already in this process, for the tests.</summary>
    internal static PluginCatalog LoadTypes(params Type[] types)
    {
        var plugins = new List<LoadedPlugin>();
        var problems = new List<PluginProblem>();
        var registry = new Registry(problems);

        foreach (var type in types) Instantiate(type, type.Assembly.Location, plugins, registry, problems);

        return Catalog(plugins, registry, problems);
    }

    private static PluginCatalog Catalog(List<LoadedPlugin> plugins, Registry registry, List<PluginProblem> problems) =>
        new(
            plugins,
            registry.AudioOutputs,
            registry.Modules,
            registry.Presets,
            problems,
            registry.Assistants,
            registry.SecretStores,
            registry.MidiInputs,
            registry.Providers);

    /// <summary>
    /// The plugin folders in the order they load: the same on every run, so priority
    /// ties break the same way, with a package's plugins after every other, so an id
    /// one shares with a plugin shipped or copied in by hand is the package's to lose.
    /// A folder whose name starts with a dot is an installation in progress, never a plugin.
    /// </summary>
    internal static IEnumerable<string> Folders(string directory) => Directory.EnumerateDirectories(directory)
        .Where(folder => !Path.GetFileName(folder).StartsWith('.'))
        .OrderBy(folder => File.Exists(Path.Combine(folder, PluginPackage.MarkerName)))
        .ThenBy(folder => folder, StringComparer.Ordinal);

    private static void LoadFolder(
        string folder,
        List<LoadedPlugin> plugins,
        Registry registry,
        List<PluginProblem> problems,
        List<PluginLoadContext>? collectible = null)
    {
        var entries = EntryAssemblies(folder);
        if (entries.Count == 0) return;

        // One context for the whole folder: its dependencies are shared by
        // everything in it, and isolation is wanted *between* plugins.
        var context = new PluginLoadContext(entries[0], collectible is not null);
        collectible?.Add(context);

        foreach (var entry in entries)
        {
            var assembly = TryLoad(context, entry, problems);
            if (assembly is null) continue;

            // Before a type of it is looked at: loading has bound nothing yet, so
            // a plugin refused here has run none of its code and named none of ours.
            if (ContractVersion.Refusal(assembly) is { } refusal)
            {
                problems.Add(new PluginProblem(Path.GetFileName(entry), refusal));
                continue;
            }

            foreach (var type in PluginTypes(assembly, problems))
                Instantiate(type, entry, plugins, registry, problems);
        }
    }

    /// <summary>
    /// A <c>.deps.json</c> beside an assembly is what <c>EnableDynamicLoading</c>
    /// produces, so it identifies the plugin among its own dependencies without a
    /// manifest to keep in step. A folder without one is scanned whole.
    /// </summary>
    /// <remarks>
    /// A copy of a host-owned assembly is skipped rather than treated as a
    /// candidate: it is easy to ship by accident, and its dependency file would
    /// otherwise be picked as the folder's.
    /// </remarks>
    private static List<string> EntryAssemblies(string folder)
    {
        var dlls = Directory.GetFiles(folder, "*.dll")
            .Where(d => !PluginLoadContext.IsHostOwned(Path.GetFileNameWithoutExtension(d)))
            .ToList();

        var declared = dlls.Where(d => File.Exists(Path.ChangeExtension(d, ".deps.json"))).ToList();

        return declared.Count > 0 ? declared : dlls;
    }

    private static Assembly? TryLoad(PluginLoadContext context, string path, List<PluginProblem> problems)
    {
        try
        {
            return context.LoadFromAssemblyPath(path);
        }
        catch (BadImageFormatException)
        {
            // A native library sitting next to the managed ones. Not a problem,
            // just not a plugin.
            return null;
        }
        catch (Exception ex)
        {
            problems.Add(new PluginProblem(Path.GetFileName(path), ex.Message));
            return null;
        }
    }

    private static IEnumerable<Type> PluginTypes(Assembly assembly, List<PluginProblem> problems)
    {
        Type[] types;

        try
        {
            types = assembly.GetExportedTypes();
        }
        catch (Exception ex)
        {
            problems.Add(new PluginProblem(assembly.GetName().Name ?? "plugin", ex.Message));
            return [];
        }

        return types.Where(t =>
            t is { IsAbstract: false, IsInterface: false }
            && typeof(IFlybackPlugin).IsAssignableFrom(t)
            && t.GetConstructor(Type.EmptyTypes) is not null);
    }

    private static void Instantiate(
        Type type,
        string path,
        List<LoadedPlugin> plugins,
        Registry registry,
        List<PluginProblem> problems)
    {
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
            var mark = registry.Mark();
            plugin.Register(registry);

            // Its code has run by now, but nothing it registered is kept unless it was declared.
            if (ModuleDeclarations.Required(type.Assembly.GetReferencedAssemblies())
                && ModuleDeclarations.Mismatch(ModuleDeclarations.Of(type.Assembly), registry.OfferedSince(mark)) is { } mismatch)
            {
                registry.Restore(mark);
                problems.Add(new PluginProblem(Path.GetFileName(path), mismatch));
                return;
            }

            plugins.Add(new LoadedPlugin(info, path));
        }
        catch (Exception ex)
        {
            problems.Add(new PluginProblem(type.Name, ex.Message));
        }
    }

    /// <summary>
    /// Collects what plugins offer. Registration is last-loser: the first
    /// backend to claim an id keeps it, so a plugin cannot shadow one that
    /// loaded before it.
    /// </summary>
    private sealed class Registry(List<PluginProblem> problems) : IPluginRegistry
    {
        private readonly List<IAudioOutput> audioOutputs = [];
        private readonly List<IPatchAssistant> assistants = [];
        private readonly List<ISecretStore> secretStores = [];
        private readonly List<IMidiInput> midiInputs = [];

        /// <summary>Who registered each thing kept above, keyed by the thing itself.</summary>
        private readonly Dictionary<object, PluginInfo> providers = new(ReferenceEqualityComparer.Instance);

        /// <summary>Whoever is registering right now, for blaming in messages.</summary>
        public PluginInfo Source { get; set; } = new("", "");

        public IReadOnlyDictionary<object, PluginInfo> Providers => providers;

        public IReadOnlyList<IAudioOutput> AudioOutputs => audioOutputs;

        public IReadOnlyList<IPatchAssistant> Assistants => assistants;

        public IReadOnlyList<ISecretStore> SecretStores => secretStores;

        public IReadOnlyList<IMidiInput> MidiInputs => midiInputs;

        /// <summary>Every module offered, accepted or not, in order.</summary>
        private readonly List<NodeDef> offered = [];

        /// <summary>How far everything had got, to undo a plugin's registration back to.</summary>
        public readonly record struct Checkpoint(
            ModuleCatalog Modules, int Offered, int Presets, int AudioOutputs, int Assistants, int SecretStores, int MidiInputs, int Problems);

        public Checkpoint Mark() => new(
            Modules, offered.Count, presets.Count, audioOutputs.Count, assistants.Count, secretStores.Count, midiInputs.Count, problems.Count);

        public IEnumerable<NodeDef> OfferedSince(Checkpoint mark) => offered.Skip(mark.Offered);

        /// <summary>Forgets everything registered since <paramref name="mark"/>, and the problems it caused.</summary>
        public void Restore(Checkpoint mark)
        {
            Modules = mark.Modules;
            offered.RemoveRange(mark.Offered, offered.Count - mark.Offered);
            presets.RemoveRange(mark.Presets, presets.Count - mark.Presets);
            Trim(audioOutputs, mark.AudioOutputs);
            Trim(assistants, mark.Assistants);
            Trim(secretStores, mark.SecretStores);
            Trim(midiInputs, mark.MidiInputs);
            problems.RemoveRange(mark.Problems, problems.Count - mark.Problems);
        }

        private void Trim<T>(List<T> list, int count) where T : notnull
        {
            foreach (var removed in list.Skip(count)) providers.Remove(removed);

            list.RemoveRange(count, list.Count - count);
        }

        /// <summary>
        /// Built up as plugins register, starting from the engine's own modules.
        /// The catalogue itself decides what it will accept; refusals become
        /// problems here so a plugin author sees them next to everything else
        /// that went wrong.
        /// </summary>
        public ModuleCatalog Modules { get; private set; } = NodeCatalog.BuiltIn;

        /// <summary>Starts as the engine's own presets; plugins append to it.</summary>
        private readonly List<PatchPreset> presets = [.. Flyback.Core.Graph.Presets.All];

        public IReadOnlyList<PatchPreset> Presets => presets;

        public void AddPresets(IReadOnlyList<PatchPreset> offered)
        {
            foreach (var preset in offered)
            {
                if (presets.Any(p => p.Name == preset.Name))
                {
                    problems.Add(new PluginProblem(
                        Source.Id,
                        $"preset '{preset.Name}' is already offered and was ignored."));
                    continue;
                }

                // A plugin's preset arrives with its Maths chains folded into
                // Expressions, the same as the engine's.
                presets.Add(Flyback.Core.Graph.Presets.Fused(preset));
            }
        }

        public void AddModules(ModuleProvider provider, IReadOnlyList<NodeDef> modules)
        {
            offered.AddRange(modules);

            var added = Modules.With(provider, modules);

            Modules = added.Catalog;

            foreach (var rejection in added.Rejected)
                problems.Add(new PluginProblem(Source.Id, rejection));
        }

        public void AddAudioOutput(IAudioOutput output)
        {
            if (audioOutputs.Any(o => o.Id == output.Id))
            {
                problems.Add(new PluginProblem(
                    Source.Id,
                    $"audio output '{output.Id}' is already registered and was ignored."));
                return;
            }

            audioOutputs.Add(output);
            providers[output] = Source;
        }

        public void AddPatchAssistant(IPatchAssistant assistant)
        {
            if (assistants.Any(a => a.Id == assistant.Id))
            {
                problems.Add(new PluginProblem(
                    Source.Id,
                    $"assistant '{assistant.Id}' is already registered and was ignored."));
                return;
            }

            assistants.Add(assistant);
            providers[assistant] = Source;
        }

        public void AddSecretStore(ISecretStore store)
        {
            if (secretStores.Any(s => s.Id == store.Id))
            {
                problems.Add(new PluginProblem(
                    Source.Id,
                    $"secret store '{store.Id}' is already registered and was ignored."));
                return;
            }

            secretStores.Add(store);
            providers[store] = Source;
        }

        public void AddMidiInput(IMidiInput input)
        {
            if (midiInputs.Any(i => i.Id == input.Id))
            {
                problems.Add(new PluginProblem(
                    Source.Id,
                    $"MIDI input '{input.Id}' is already registered and was ignored."));
                return;
            }

            midiInputs.Add(input);
            providers[input] = Source;
        }
    }
}
