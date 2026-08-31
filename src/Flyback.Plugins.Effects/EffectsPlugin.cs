using Flyback.Core.Graph;

namespace Flyback.Plugins.Effects;

/// <summary>
/// The five effects built on a delay line: repeats, a room, and the three
/// sweeps.
/// </summary>
public sealed class EffectsPlugin : IFlybackPlugin
{
    internal static ModuleProvider Provider { get; } = new("flyback.effects", "Effects");

    public PluginInfo Info { get; } = new(
        "flyback.effects",
        "Effects",
        "Delay, reverb, chorus, flanger and phaser — everything built on a delay line.");

    public void Register(IPluginRegistry registry)
    {
        registry.AddModules(
            Provider,
            [
                DelayModule.Definition,
                ReverbModule.Definition,
                ChorusModule.Definition,
                FlangerModule.Definition,
                PhaserModule.Definition,
            ]);

        registry.AddPresets(
        [
            new PatchPreset(
                SpacePreset.Name,
                SpacePreset.Build,
                "A plucked tone through the delay into the reverb: repeats first, then the room.",
                PresetKind.Idea),
            new PatchPreset(
                ModulationPreset.Name,
                ModulationPreset.Build,
                "Flanger, phaser and chorus in the order a pedalboard would have them.",
                PresetKind.Idea),
            new PatchPreset(
                SlowWeatherPreset.Name,
                SlowWeatherPreset.Build,
                "A generative patch with no clock in it, played into the two effects.",
                PresetKind.Showcase),
        ]);
    }
}
