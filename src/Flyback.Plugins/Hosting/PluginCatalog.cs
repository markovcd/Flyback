using Flyback.Core.Graph;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Midi;

namespace Flyback.Plugins.Hosting;

/// <summary>Everything one scan of the plugin directory found.</summary>
internal sealed class PluginCatalog
{
    public static PluginCatalog Empty { get; } =
        new([], [], NodeCatalog.BuiltIn, Engine.Graph.Presets.All, []);

    /// <param name="problems">What went wrong on the way, one entry per plugin that could not be loaded or was refused.</param>
    /// <param name="plugins">Everything that loaded, whether or not it registered anything.</param>
    /// <param name="audioOutputs">The sound backends offered, in no particular order — priority is asked for later.</param>
    /// <param name="modules">The engine's catalog with every plugin's modules added to it.</param>
    /// <param name="presets">Patches to start from, the engine's and each plugin's, in any order.</param>
    /// <param name="midiInputs">The ways of hearing what is plugged in, or none where there is no such way.</param>
    /// <param name="providers">Who registered each of the above, for <see cref="Provider"/>.</param>
    internal PluginCatalog(
        IReadOnlyList<LoadedPlugin> plugins,
        IReadOnlyList<IAudioOutput> audioOutputs,
        ModuleCatalog modules,
        IReadOnlyList<PatchPreset> presets,
        IReadOnlyList<PluginProblem> problems,
        IReadOnlyList<IMidiInput>? midiInputs = null,
        IReadOnlyDictionary<object, PluginInfo>? providers = null,
        IReadOnlyList<IAudioInput>? audioInputs = null)
    {
        // Copied under reference equality: two backends are the same one only if
        // they are the same object, whatever their ids or their Equals say.
        this.providers = providers is null
            ? new Dictionary<object, PluginInfo>(ReferenceEqualityComparer.Instance)
            : new Dictionary<object, PluginInfo>(providers, ReferenceEqualityComparer.Instance);

        Plugins = plugins;
        AudioOutputs = audioOutputs;
        Modules = modules;
        Presets = [.. PresetOrder.Of(presets)];
        Problems = problems;
        MidiInputs = midiInputs ?? [];
        AudioInputs = audioInputs ?? [];
    }

    private readonly Dictionary<object, PluginInfo> providers;

    /// <summary>
    /// Puts the modules in the catalog every patch is read against, which every host
    /// does once before it opens one, and hands this back.
    /// </summary>
    public PluginCatalog Install()
    {
        NodeCatalog.Install(Modules);

        return this;
    }

    public IReadOnlyList<LoadedPlugin> Plugins { get; }

    public IReadOnlyList<IAudioOutput> AudioOutputs { get; }

    /// <summary>Every way installed of hearing what is plugged in.</summary>
    public IReadOnlyList<IMidiInput> MidiInputs { get; }

    /// <summary>Every way installed of hearing a microphone or a line.</summary>
    public IReadOnlyList<IAudioInput> AudioInputs { get; }

    /// <summary>
    /// The engine's modules with every plugin's folded in. Install it before
    /// anything opens a patch — until then the program only knows the built-ins,
    /// and a patch needing a plugin would look broken when it is not.
    /// </summary>
    public ModuleCatalog Modules { get; }

    /// <summary>
    /// Patches to start from, the engine's and any a plugin offered, in <see cref="PresetOrder"/>. A
    /// preset builds when it is picked, so one that uses a plugin's modules can
    /// still throw if that plugin registered its presets but not its modules —
    /// the caller is expected to survive that.
    /// </summary>
    public IReadOnlyList<PatchPreset> Presets { get; }

    public IReadOnlyList<PluginProblem> Problems { get; }

    /// <summary>
    /// The plugin that registered a backend, or
    /// null for anything this catalog did not see registered.
    /// </summary>
    /// <remarks>
    /// What the settings window names on the tab that configures a backend: which
    /// device plays is the plugin's answer, not Flyback's, and on a machine with
    /// two sound plugins installed there is otherwise nothing saying which one is
    /// being set.
    /// </remarks>
    public PluginInfo? Provider(object registered)
    {
        ArgumentNullException.ThrowIfNull(registered);

        return providers.GetValueOrDefault(registered);
    }

    /// <summary>
    /// The backend to use here: supported, highest priority, ties broken on id
    /// so the choice is the same on every run. Null when nothing can play.
    /// </summary>
    public IAudioOutput? PreferredAudioOutput => PlayableAudioOutputs is [var first, ..] ? first : null;

    /// <summary>Every backend that can play here, the preferred one first.</summary>
    public IReadOnlyList<IAudioOutput> PlayableAudioOutputs => AudioOutputs
        .Where(o => Supported(o))
        .OrderByDescending(o => o.Priority)
        .ThenBy(o => o.Id, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// The backend a setting names while it can play here, and otherwise the preferred one.
    /// Empty names none.
    /// </summary>
    public IAudioOutput? AudioOutput(string chosen) =>
        PlayableAudioOutputs.FirstOrDefault(o => string.Equals(o.Id, chosen, StringComparison.Ordinal)) ?? PreferredAudioOutput;

    /// <summary>
    /// The way of hearing a microphone to use here: supported, highest priority, ties broken
    /// on id. Null where nothing can listen, and a Line In is then silent.
    /// </summary>
    public IAudioInput? PreferredAudioInput => AudioInputs
        .Where(Supported)
        .OrderByDescending(i => i.Priority)
        .ThenBy(i => i.Id, StringComparer.Ordinal)
        .FirstOrDefault();

    /// <summary>A store that throws while answering whether it works here has answered no.</summary>
    /// <summary>
    /// How to hear what is plugged in here: supported, highest priority, ties broken on
    /// id. <see cref="NoMidiInput"/> where nothing installed can listen, in which case
    /// the only instrument is the computer's own keyboard.
    /// </summary>
    /// <remarks>
    /// One rather than all, the way a sound backend is chosen: two backends on one
    /// machine would be two ways to the same socket, and a picker offering every
    /// keyboard twice is worse than one offering it once.
    /// </remarks>
    public IMidiInput PreferredMidiInput => MidiInputs
        .Where(Supported)
        .OrderByDescending(i => i.Priority)
        .ThenBy(i => i.Id, StringComparer.Ordinal)
        .FirstOrDefault() ?? NoMidiInput.Instance;

    /// <summary>A backend that throws while answering whether it works here has answered no.</summary>
    private static bool Supported(IAudioInput input)
    {
        try
        {
            return input.IsSupported;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>A backend that throws while answering whether it works here has answered no.</summary>
    private static bool Supported(IMidiInput input)
    {
        try
        {
            return input.IsSupported;
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
