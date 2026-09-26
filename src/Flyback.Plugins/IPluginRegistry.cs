using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Midi;
using Flyback.Plugins.Secrets;

namespace Flyback.Plugins;

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
    /// Adds modules to the catalog. Every type id must begin with
    /// <c>provider.Id</c> and a dot; ones that do not are refused, because that
    /// prefix is what a saved patch relies on to know which plugin a module it
    /// contains came from.
    /// </summary>
    void AddModules(ModuleProvider provider, IReadOnlyList<NodeDef> modules);

    /// <summary>
    /// Adds patches to start from. A preset is built when it is chosen, not when
    /// it is registered, so it may freely use modules this plugin added — by
    /// then the catalog is complete.
    /// </summary>
    void AddPresets(IReadOnlyList<PatchPreset> presets);

    /// <summary>
    /// Offers something that can author a patch. Registering it must not open a
    /// connection, must not need a credential to be present, and must not cost
    /// anything — the shell registers every assistant it finds and only reaches
    /// for one when somebody asks it a question.
    /// </summary>
    void AddPatchAssistant(IPatchAssistant assistant);

    /// <summary>
    /// Offers somewhere the operating system will hold a secret. Registering it
    /// must not read or write one.
    /// </summary>
    void AddSecretStore(ISecretStore store);

    /// <summary>
    /// Offers a way of hearing what is plugged in. Registering it must not open
    /// a device and must not enumerate one — the shell asks for the list when
    /// somebody opens a picker, which is a different moment and a later one.
    /// </summary>
    void AddMidiInput(IMidiInput input);
}