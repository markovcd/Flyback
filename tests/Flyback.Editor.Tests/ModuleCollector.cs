using Flyback.Assist.Keys;
using Flyback.Core.Graph;
using Flyback.Plugins;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Midi;

namespace Flyback.Editor.Tests;

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


    public void AddMidiInput(IMidiInput input) { }

    public void AddAudioInput(IAudioInput input) { }

}
