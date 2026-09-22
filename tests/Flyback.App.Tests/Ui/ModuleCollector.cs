using Flyback.Core.Graph;
using Flyback.Plugins;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Midi;
using Flyback.Plugins.Secrets;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The modules a plugin offers, read straight off its <c>Register</c> rather
/// than through <c>PluginHost</c>'s folder scan. Everything else it registers is
/// dropped.
/// </summary>
internal sealed class ModuleCollector : IPluginRegistry
{
    public List<NodeDef> Modules { get; } = [];

    public ModuleProvider? Provider { get; private set; }

    public static ModuleCollector Of(IFlybackPlugin plugin)
    {
        var collector = new ModuleCollector();

        plugin.Register(collector);

        return collector;
    }

    public void AddModules(ModuleProvider provider, IReadOnlyList<NodeDef> modules)
    {
        Provider = provider;
        Modules.AddRange(modules);
    }

    public void AddAudioOutput(IAudioOutput output) { }

    public void AddPresets(IReadOnlyList<PatchPreset> presets) { }

    public void AddPatchAssistant(IPatchAssistant assistant) { }

    public void AddSecretStore(ISecretStore store) { }

    public void AddMidiInput(IMidiInput input) { }
}
