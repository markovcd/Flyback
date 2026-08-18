using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Secrets;

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

    /// <param name="problems">What went wrong on the way, one entry per plugin that could not be loaded or was refused.</param>
    /// <param name="assistants">
    /// Last and optional so that every call written before assistants existed
    /// still compiles — the same courtesy the registry interface extends to
    /// plugins.
    /// </param>
    /// <param name="plugins">Everything that loaded, whether or not it registered anything.</param>
    /// <param name="audioOutputs">The sound backends offered, in no particular order — priority is asked for later.</param>
    /// <param name="modules">The engine's catalogue with every plugin's modules added to it.</param>
    /// <param name="presets">Patches to start from: the engine's own first, then each plugin's.</param>
    /// <param name="secretStores">The places a key may be kept, or none where nothing can keep one.</param>
    internal PluginCatalog(
        IReadOnlyList<LoadedPlugin> plugins,
        IReadOnlyList<IAudioOutput> audioOutputs,
        ModuleCatalog modules,
        IReadOnlyList<PatchPreset> presets,
        IReadOnlyList<PluginProblem> problems,
        IReadOnlyList<IPatchAssistant>? assistants = null,
        IReadOnlyList<ISecretStore>? secretStores = null)
    {
        Plugins = plugins;
        AudioOutputs = audioOutputs;
        Modules = modules;
        Presets = presets;
        Problems = problems;
        Assistants = assistants ?? [];
        SecretStores = secretStores ?? [];
    }

    public IReadOnlyList<LoadedPlugin> Plugins { get; }

    public IReadOnlyList<IAudioOutput> AudioOutputs { get; }

    /// <summary>Everything installed that could author a patch.</summary>
    public IReadOnlyList<IPatchAssistant> Assistants { get; }

    /// <summary>Everywhere installed that the operating system will hold a secret.</summary>
    public IReadOnlyList<ISecretStore> SecretStores { get; }

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
    /// The assistant to offer: highest priority, ties broken on id so the choice
    /// is the same on every run. Null when none is installed.
    /// </summary>
    /// <remarks>
    /// Deliberately not filtered by whether it can actually run, unlike
    /// <see cref="PreferredAudioOutput"/>. That depends on a configuration this
    /// catalogue has never seen, and an assistant with no key yet is still the
    /// one to put in front of somebody — so the panel can say what is missing
    /// instead of saying nothing at all.
    /// </remarks>
    public IPatchAssistant? PreferredAssistant => Assistants
        .OrderByDescending(a => a.Priority)
        .ThenBy(a => a.Id, StringComparer.Ordinal)
        .FirstOrDefault();

    /// <summary>The assistant a setting names, or null when it is no longer installed.</summary>
    public IPatchAssistant? Assistant(string id) =>
        Assistants.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// Where a secret should be kept here: supported, highest priority, ties
    /// broken on id. Null when nothing installed can hold one — in which case a
    /// key is used for the session and then forgotten, which the panel says
    /// rather than appearing to have saved something.
    /// </summary>
    public ISecretStore? PreferredSecretStore => SecretStores
        .Where(Supported)
        .OrderByDescending(s => s.Priority)
        .ThenBy(s => s.Id, StringComparer.Ordinal)
        .FirstOrDefault();

    /// <summary>A store that throws while answering whether it works here has answered no.</summary>
    private static bool Supported(ISecretStore store)
    {
        try
        {
            return store.IsSupported;
        }
        catch
        {
            return false;
        }
    }

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
