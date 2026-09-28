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
            new PatchPreset(
                SpacePreset.Name,
                SpacePreset.Build,
                "A plucked tone through the delay into the reverb: repeats first, then the room."),
            new PatchPreset(
                ModulationPreset.Name,
                ModulationPreset.Build,
                "Flanger, phaser and chorus in the order a pedalboard would have them."),
            new PatchPreset(
                PlayedPreset.Name,
                PlayedPreset.Build,
                "The one preset you have to play: each key plucks a string, with a touch of reverb."),
            new PatchPreset(
                SeenBeatPreset.Name,
                SeenBeatPreset.Build,
                SeenBeatPreset.Description,
                PresetKind.Interplay),
            new PatchPreset(
                TwoEchoesPreset.Name,
                TwoEchoesPreset.Build,
                TwoEchoesPreset.Description,
                PresetKind.Interplay),
            new PatchPreset(
                AcidPreset.Name,
                AcidPreset.Build,
                "A whole acid techno track: a hundred and twenty-eight bars of a 303 line in two "
                + "themes, with accents and slides through a four-pole filter, over a galloping "
                + "bass — three builds, a breakdown, and a second 303 that answers the first.",
                PresetKind.Showcase),
            new PatchPreset(
                MyceliumPreset.Name,
                MyceliumPreset.Build,
                "A whole psybient track: ninety-six bars in six sections, a "
                + "picture grown from the same signals, one voice that is the picture heard, and "
                + "the Caterpillar asking who you are.",
                PresetKind.Showcase)
            {
                Files = PresetFiles.Embedded(typeof(MyceliumPreset).Assembly, MyceliumPreset.Name),
            },
            new PatchPreset(
                BronzePreset.Name,
                BronzePreset.Build,
                "A gamelan: sixteen gong cycles on a tempo that breathes, every part figured out "
                + "of one melody in a five-note scale, and a mandala with a ring for each tier.",
                PresetKind.Showcase),
            new PatchPreset(
                OutrunPreset.Name,
                OutrunPreset.Build,
                "A whole synthwave track: four chords, gated pads and a gated-reverb snare, under a "
                + "drawn scene — a slatted sun, a ridge, and a grid that arrives a line to the beat.",
                PresetKind.Showcase),
            new PatchPreset(
                IrrationalPreset.Name,
                IrrationalPreset.Build,
                "Two pulses whose rates are a ratio of the square root of two: they pass close, "
                + "flare, and never once land together — a piece with no loop, because it cannot "
                + "have one.",
                PresetKind.Showcase),
            new PatchPreset(
                PhasePreset.Name,
                PhasePreset.Build,
                "Phase music, after Steve Reich: two players on one pattern, the second pulling ahead "
                + "through all twelve canons, and a picture of two dials that is the diagram of it.",
                PresetKind.Showcase),
            new PatchPreset(
                FracturePreset.Name,
                FracturePreset.Build,
                "Drum and bass at a hundred and seventy: a synthesized break chopped by bending the "
                + "clock it reads, a Reese bass, and a picture cut into strips by the same list.",
                PresetKind.Showcase),
            new PatchPreset(
                SlowWeatherPreset.Name,
                SlowWeatherPreset.Build,
                "A generative ambient patch with no clock in it and five loops: a drone that bends "
                + "its own phase, an echo that darkens every time round, voices that push each other "
                + "down, and a picture steered by where it was bright a frame ago. Six panel knobs — "
                + "echo, chime decay, reverb, warp, color and trails spin — ride on top of it.",
                PresetKind.Showcase),
            new PatchPreset(
                NoSenseDubPreset.Name,
                NoSenseDubPreset.Build,
                "Roots dub in A minor to perform: a one drop, a bass line and a skank that drop in and "
                + "out and are thrown into the echo, a drop to silence and steppers at twice the tempo, "
                + "a drawbar organ to play over them a note at a time, and six panel "
                + "knobs — filter, pluck, decay, echo, room — that move the rings on the screen as they "
                + "move the sound. The voice is Nesnad's, from Wikimedia Commons, under CC BY-SA 3.0.",
                PresetKind.Showcase)
            {
                Files = PresetFiles.Embedded(typeof(NoSenseDubPreset).Assembly, NoSenseDubPreset.Clips),
            },
            new PatchPreset(
                OverworldPreset.Name,
                OverworldPreset.Build,
                "A whole chiptune track on a console's four voices — two pulses, a stepped triangle "
                + "and crunching noise — with a key change, the music ducked under the kick, "
                + "under a side-scroller drawn a pixel at a time.",
                PresetKind.Showcase),
        ]);
    }
}
