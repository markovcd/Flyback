using Flyback.Core.Graph;
using Flyback.Plugins;
using Flyback.Plugins.Easy;

// Every module Register adds, so it can be listed before the plugin runs.
[assembly: FlybackModule(SynthModule.TypeId, "Easy Synth")]
[assembly: FlybackModule(DrumModule.TypeId, "Easy Drum")]

namespace Flyback.Plugins.Easy;

/// <summary>
/// Modules that sound right before anything is wired and cannot be set to
/// anything that hurts: Easy Synth, a whole synth voice in one block, and Easy
/// Drum, a drum machine's voice that plays in time on its own.
/// </summary>
public sealed class EasyPlugin : IFlybackPlugin
{
    internal static ModuleProvider Provider { get; } = new("flyback.easy", "Easy");

    public PluginInfo Info { get; } = new(
        "flyback.easy",
        "Easy",
        "Easy Synth, a whole synth in one module, and Easy Drum, a drum that plays in time on "
        + "its own: both sound good before anything is wired, and neither can clip.");

    public void Register(IPluginRegistry registry)
    {
        registry.AddModules(Provider, [SynthModule.Definition, DrumModule.Definition]);

        registry.AddPresets(
        [
            PresetText.Preset(typeof(EasyPlugin).Assembly, "First notes", "FirstNotes.fbks", PresetKind.Interplay),
            PresetText.Preset(typeof(EasyPlugin).Assembly, "First beat", "FirstBeat.fbks", PresetKind.Interplay),
            PresetText.Preset(typeof(EasyPlugin).Assembly, "Warehouse", "Warehouse.fbks", PresetKind.Showcase),
        ]);
    }
}
