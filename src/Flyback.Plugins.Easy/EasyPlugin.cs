using Flyback.Core.Graph;
using Flyback.Plugins;
using Flyback.Plugins.Easy;

// Every module Register adds, so it can be listed before the plugin runs.
[assembly: FlybackModule(SynthModule.TypeId, "Easy Synth")]

namespace Flyback.Plugins.Easy;

/// <summary>
/// Modules that sound right before anything is wired and cannot be set to
/// anything that hurts: Easy Synth, a whole synth voice in one block.
/// </summary>
public sealed class EasyPlugin : IFlybackPlugin
{
    internal static ModuleProvider Provider { get; } = new("flyback.easy", "Easy");

    public PluginInfo Info { get; } = new(
        "flyback.easy",
        "Easy",
        "Easy Synth, a whole synth in one module that sounds good before anything is wired, "
        + "and has no setting that can make it clip.");

    public void Register(IPluginRegistry registry)
    {
        registry.AddModules(Provider, [SynthModule.Definition]);

        registry.AddPresets(
        [
            new PatchPreset(
                FirstNotesPreset.Name,
                FirstNotesPreset.Build,
                "A Note Sequencer playing an Easy Synth, its filter swaying, with rings flashing on each note.",
                PresetKind.Interplay),
        ]);
    }
}
