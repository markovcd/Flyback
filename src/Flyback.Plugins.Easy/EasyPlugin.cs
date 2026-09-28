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
            new PatchPreset(
                FirstNotesPreset.Name,
                FirstNotesPreset.Build,
                "A Note Sequencer playing an Easy Synth, its filter swaying, with rings flashing on each note.",
                PresetKind.Interplay),
            new PatchPreset(
                FirstBeatPreset.Name,
                FirstBeatPreset.Build,
                "Four Easy Drums on Auto, a kick, a snare and two hats, each playing the rhythm that "
                + "suits it, with the picture pulsing on the kick.",
                PresetKind.Interplay),
            new PatchPreset(
                WarehousePreset.Name,
                WarehousePreset.Build,
                "Acid house on the Easy modules: a squelching bass line with accents over a drum "
                + "machine at 124 bpm, a supersaw stab on top, and the Acid knob on the filter.",
                PresetKind.Showcase),
        ]);
    }
}
