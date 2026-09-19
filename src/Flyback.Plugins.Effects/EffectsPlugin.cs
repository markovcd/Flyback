using Flyback.Core.Graph;

namespace Flyback.Plugins.Effects;

/// <summary>
/// The effects built on a delay line: repeats, repeats in time, a room, and the
/// three sweeps.
/// </summary>
public sealed class EffectsPlugin : IFlybackPlugin
{
    internal static ModuleProvider Provider { get; } = new("flyback.effects", "Effects");

    public PluginInfo Info { get; } = new(
        "flyback.effects",
        "Effects",
        "Delay, a tempo echo, reverb, chorus, flanger and phaser — everything built on a delay line.");

    public void Register(IPluginRegistry registry)
    {
        registry.AddModules(
            Provider,
            [
                DelayModule.Definition,
                EchoModule.Definition,
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
                PlayedPreset.Name,
                PlayedPreset.Build,
                "The one preset you have to play: each key plucks a string, with a touch of reverb.",
                PresetKind.Idea),
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
                "A whole psybient track: ninety-six bars in six sections off one sequencer, a "
                + "picture grown from the same signals, and one voice that is the picture heard.",
                PresetKind.Showcase),
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
                + "down, and a picture steered by where it was bright a frame ago.",
                PresetKind.Showcase),
            new PatchPreset(
                DubPreset.Name,
                DubPreset.Build,
                "Dub techno to perform: a kick, hats and a sub that run on their own, four keys of "
                + "chord to hold over them, and eight panel knobs — filter, echo, room, drums, bass — "
                + "that move the rings on the screen as they move the sound.",
                PresetKind.Showcase),
            new PatchPreset(
                OverworldPreset.Name,
                OverworldPreset.Build,
                "A whole chiptune track on a console's four voices — two pulses, a stepped triangle "
                + "and crunching noise — with a key change, mastered through a kick-keyed compressor, "
                + "under a side-scroller drawn a pixel at a time.",
                PresetKind.Showcase),
        ]);
    }
}
