using System.Reflection;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Secrets;

namespace Flyback.Plugins.Hosting;

/// <summary>
/// Finds and loads plugins. One scan, at startup — there is no reload, because
/// an assembly the audio thread is calling into cannot be unloaded safely and
/// pretending otherwise would be worse than restarting.
/// </summary>
/// <remarks>
/// The layout is one folder per plugin under <c>plugins/</c>, each holding the
/// plugin assembly, its <c>.deps.json</c>, and its private dependencies:
/// <code>
/// Flyback.exe
/// plugins/
///   Wasapi/
///     Flyback.Plugins.Wasapi.dll
///     Flyback.Plugins.Wasapi.deps.json
///     NAudio.dll …
/// </code>
/// Nothing here throws. A plugin that is missing, broken, built against another
/// runtime or simply hostile is a line in <see cref="PluginCatalog.Problems"/>,
/// not a program that will not start.
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

        // Ordered so that two runs on the same machine see the same plugins in
        // the same order, and therefore break priority ties the same way.
        foreach (var folder in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
            LoadFolder(folder, plugins, registry, problems);

        return new PluginCatalog(
            plugins,
            registry.AudioOutputs,
            registry.Modules,
            registry.Presets,
            problems,
            registry.Assistants,
            registry.SecretStores);
    }

    private static void LoadFolder(
        string folder,
        List<LoadedPlugin> plugins,
        Registry registry,
        List<PluginProblem> problems)
    {
        var entries = EntryAssemblies(folder);
        if (entries.Count == 0) return;

        // One context for the whole folder: its dependencies are shared by
        // everything in it, and isolation is wanted *between* plugins.
        var context = new PluginLoadContext(entries[0]);

        foreach (var entry in entries)
        {
            var assembly = TryLoad(context, entry, problems);
            if (assembly is null) continue;

            foreach (var type in PluginTypes(assembly, problems))
                Instantiate(type, entry, plugins, registry, problems);
        }
    }

    /// <summary>
    /// A <c>.deps.json</c> beside an assembly is what <c>EnableDynamicLoading</c>
    /// produces, so it identifies the plugin among its own dependencies without
    /// a manifest to keep in step. A folder without one is scanned whole, which
    /// covers a plugin that is a single file.
    /// </summary>
    /// <remarks>
    /// A copy of a host-owned assembly is skipped rather than treated as a
    /// candidate. It is an easy thing to ship by accident, and its dependency
    /// file would otherwise be picked as the folder's — leaving the real
    /// plugin's own dependencies unresolvable.
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
            plugin.Register(registry);

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

        /// <summary>Whoever is registering right now, for blaming in messages.</summary>
        public PluginInfo Source { get; set; } = new("", "");

        public IReadOnlyList<IAudioOutput> AudioOutputs => audioOutputs;

        public IReadOnlyList<IPatchAssistant> Assistants => assistants;

        public IReadOnlyList<ISecretStore> SecretStores => secretStores;

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

                presets.Add(preset);
            }
        }

        public void AddModules(ModuleProvider provider, IReadOnlyList<NodeDef> modules)
        {
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
        }
    }
}
