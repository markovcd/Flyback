using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Audio;

namespace Flyback.Plugins;

/// <summary>
/// What a plugin says about itself. <paramref name="Id"/> is stable and
/// machine-readable; it is what a setting or a log line names.
/// </summary>
public sealed record PluginInfo(string Id, string Name, string Description = "");

/// <summary>
/// The entry point of a plugin assembly. One public parameterless class per
/// assembly implementing this is what the host looks for.
/// </summary>
/// <remarks>
/// A plugin is constructed on the UI thread during startup, so
/// <see cref="Register"/> must be cheap and must not touch a device. Deciding
/// whether a backend can actually run belongs on the thing being registered —
/// see <see cref="Audio.IAudioOutput.IsSupported"/>.
/// </remarks>
public interface IFlybackPlugin
{
    PluginInfo Info { get; }

    void Register(IPluginRegistry registry);
}

/// <summary>
/// What a plugin may contribute. New kinds of extension are new methods here;
/// existing plugins only ever call this interface, so adding one does not
/// invalidate them.
/// </summary>
public interface IPluginRegistry
{
    /// <summary>Offers a sound backend. Registering it does not open a device.</summary>
    void AddAudioOutput(IAudioOutput output);

    /// <summary>
    /// Adds modules to the catalogue. Every type id must begin with
    /// <c>provider.Id</c> and a dot; ones that do not are refused, because that
    /// prefix is what a saved patch relies on to know which plugin a module it
    /// contains came from.
    /// </summary>
    void AddModules(ModuleProvider provider, IReadOnlyList<NodeDef> modules);

    /// <summary>
    /// Adds patches to start from. A preset is built when it is chosen, not when
    /// it is registered, so it may freely use modules this plugin added — by
    /// then the catalogue is complete.
    /// </summary>
    void AddPresets(IReadOnlyList<PatchPreset> presets);

    /// <summary>
    /// Offers something that can author a patch. Registering it must not open a
    /// connection, must not need a credential to be present, and must not cost
    /// anything — the shell registers every assistant it finds and only reaches
    /// for one when somebody asks it a question.
    /// </summary>
    void AddPatchAssistant(IPatchAssistant assistant);
}
