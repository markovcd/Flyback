using Flyback.Core.Graph;
using Flyback.Plugins;
using Flyback.Plugins.Effects;

// Every module Register adds, so it can be listed before the plugin runs.
[assembly: FlybackModule(EchoModule.TypeId, "Echo")]
[assembly: FlybackModule(ChorusModule.TypeId, "Chorus")]
[assembly: FlybackModule(FlangerModule.TypeId, "Flanger")]
[assembly: FlybackModule(PhaserModule.TypeId, "Phaser")]

namespace Flyback.Plugins.Effects;

/// <summary>
/// The effects built on a delay line: a tempo echo, chorus, flanger and phaser.
/// </summary>
/// <remarks>
/// Delay and Reverb, the plainer pair this plugin's own modules are built on, are
/// the engine's own, nothing about them being particular to an effect (ADR-0128).
/// </remarks>
public sealed class EffectsPlugin : IFlybackPlugin
{
    internal static ModuleProvider Provider { get; } = new("flyback.effects", "Effects");

    public PluginInfo Info { get; } = new(
        "flyback.effects",
        "Effects",
        "A tempo echo, chorus, flanger and phaser — built, like the engine's own Delay and "
        + "Reverb, on a delay line.");

    public void Register(IPluginRegistry registry)
    {
        registry.AddModules(
            Provider,
            [
                EchoModule.Definition,
                ChorusModule.Definition,
                FlangerModule.Definition,
                PhaserModule.Definition,
            ]);

        registry.AddPresets(
        [
            PresetText.Preset(typeof(EffectsPlugin).Assembly, "Echo chamber", "EchoChamber.fbks"),
            PresetText.Preset(typeof(EffectsPlugin).Assembly, "Moving parts", "MovingParts.fbks"),
            PresetText.Preset(typeof(EffectsPlugin).Assembly, "Played", "Played.fbks"),
            PresetText.Preset(typeof(EffectsPlugin).Assembly, "Beat you can see", "BeatYouCanSee.fbks", PresetKind.Interplay),
            PresetText.Preset(typeof(EffectsPlugin).Assembly, "Two echoes", "TwoEchoes.fbks", PresetKind.Interplay),
            PresetText.Preset(typeof(EffectsPlugin).Assembly, "Visualizer", "Visualizer.fbks", PresetKind.Idea),
            PresetText.Preset(typeof(EffectsPlugin).Assembly, "Wandering tune", "WanderingTune.fbks", PresetKind.Interplay),
            PresetText.Preset(typeof(EffectsPlugin).Assembly, "Acid", "Acid.fbks", PresetKind.Showcase),
            PresetText.Preset(typeof(EffectsPlugin).Assembly, "Mycelium", "Mycelium.fbks", PresetKind.Showcase) with
            {
                Files = PresetFiles.Embedded(typeof(EffectsPlugin).Assembly, "Mycelium"),
            },
            PresetText.Preset(typeof(EffectsPlugin).Assembly, "Bronze", "Bronze.fbks", PresetKind.Showcase),
            PresetText.Preset(typeof(EffectsPlugin).Assembly, "Outrun", "Outrun.fbks", PresetKind.Showcase),
            PresetText.Preset(typeof(EffectsPlugin).Assembly, "Irrational", "Irrational.fbks", PresetKind.Showcase),
            PresetText.Preset(typeof(EffectsPlugin).Assembly, "Phase", "Phase.fbks", PresetKind.Showcase),
            PresetText.Preset(typeof(EffectsPlugin).Assembly, "Fracture", "Fracture.fbks", PresetKind.Showcase),
            PresetText.Preset(typeof(EffectsPlugin).Assembly, "Slow weather", "SlowWeather.fbks", PresetKind.Showcase),
            PresetText.Preset(typeof(EffectsPlugin).Assembly, "No Sense Dub", "NoSenseDub.fbks", PresetKind.Showcase) with
            {
                Files = PresetFiles.Embedded(typeof(EffectsPlugin).Assembly, "NoSenseDub"),
            },
            PresetText.Preset(typeof(EffectsPlugin).Assembly, "Overworld", "Overworld.fbks", PresetKind.Showcase),
            PresetText.Preset(typeof(EffectsPlugin).Assembly, "Flyback Theme", "FlybackTheme.fbks", PresetKind.Showcase),
        ]);
    }
}
