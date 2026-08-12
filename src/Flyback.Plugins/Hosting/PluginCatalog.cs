using Flyback.Core.Graph;
using Flyback.Plugins.Audio;

namespace Flyback.Plugins.Hosting;

/// <summary>A plugin that loaded, and where it came from.</summary>
public sealed record LoadedPlugin(PluginInfo Info, string AssemblyPath);

/// <summary>
/// Something that went wrong with one plugin. Collected rather than thrown: a
/// broken plugin must not stop the program starting, and the person who has to
/// fix it needs to be told which file it was.
/// </summary>
public sealed record PluginProblem(string Source, string Message)
{
    public override string ToString() => $"{Source}: {Message}";
}

/// <summary>Everything one scan of the plugin directory found.</summary>
public sealed class PluginCatalog
{
    public static PluginCatalog Empty { get; } =
        new([], [], NodeCatalog.BuiltIn, Flyback.Core.Graph.Presets.All, []);

    internal PluginCatalog(
        IReadOnlyList<LoadedPlugin> plugins,
        IReadOnlyList<IAudioOutput> audioOutputs,
        ModuleCatalog modules,
        IReadOnlyList<PatchPreset> presets,
        IReadOnlyList<PluginProblem> problems)
    {
        Plugins = plugins;
        AudioOutputs = audioOutputs;
        Modules = modules;
        Presets = presets;
        Problems = problems;
    }

    public IReadOnlyList<LoadedPlugin> Plugins { get; }

    public IReadOnlyList<IAudioOutput> AudioOutputs { get; }

    /// <summary>
    /// The engine's modules with every plugin's folded in. Install it before
    /// anything opens a patch — until then the program only knows the built-ins,
    /// and a patch needing a plugin would look broken when it is not.
    /// </summary>
    public ModuleCatalog Modules { get; }

    /// <summary>
    /// Patches to start from: the engine's own, then any a plugin offered. A
    /// preset builds when it is picked, so one that uses a plugin's modules can
    /// still throw if that plugin registered its presets but not its modules —
    /// the caller is expected to survive that.
    /// </summary>
    public IReadOnlyList<PatchPreset> Presets { get; }

    public IReadOnlyList<PluginProblem> Problems { get; }

    /// <summary>
    /// The backend to use here: supported, highest priority, ties broken on id
    /// so the choice is the same on every run. Null when nothing can play.
    /// </summary>
    public IAudioOutput? PreferredAudioOutput => AudioOutputs
        .Where(o => Supported(o))
        .OrderByDescending(o => o.Priority)
        .ThenBy(o => o.Id, StringComparer.Ordinal)
        .FirstOrDefault();

    /// <summary>
    /// A backend that throws while answering whether it is supported has
    /// answered no. Nothing a plugin does should be able to take the shell down.
    /// </summary>
    private static bool Supported(IAudioOutput output)
    {
        try
        {
            return output.IsSupported;
        }
        catch
        {
            return false;
        }
    }
}
