using System.Text.Json.Nodes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;

namespace Flyback.Plugins.Easy;

/// <summary>
/// A drum machine's voice in one module that plays in time with nothing wired: a
/// sound picked by name, playing the rhythm that suits it at a tempo, or struck by
/// a trigger.
/// </summary>
/// <remarks>
/// On a rhythm it is stateless, so every Easy Drum at one tempo shares one grid and
/// the picture flashes with the hits the speakers play. On Trigger it keeps the time
/// since the last rise in a cell, which the screen does not have, so the screen gets
/// the trigger. Every input is clamped and each side ends in a clamp to -1..1.
/// </remarks>
internal static class DrumModule
{
    public const string TypeId = "flyback.easy.drummer";

    public const string StateKey = "drum";

    public const string SoundKey = "sound";
    public const string RhythmKey = "rhythm";

    public const int InPort = 0;
    public const int BpmPort = 1;
    public const int SwingPort = 2;
    public const int TriggerPort = 3;
    public const int VelocityPort = 4;
    public const int TunePort = 5;
    public const int DecayPort = 6;
    public const int TonePort = 7;
    public const int DrivePort = 8;
    public const int PanPort = 9;

    public const int LeftPort = 0;
    public const int RightPort = 1;
    public const int EnvPort = 2;

    private const float SlowestBpm = 20f, FastestBpm = 300f;

    /// <summary>How far 'decay' stretches or shortens each sound's own fall, in octaves either way.</summary>
    private const float DecayOctaves = 2f;

    /// <summary>How long a trigger's hit is counted before it is only silence: past every fall.</summary>
    private const float LongestAge = 60f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Easy Drum", ModuleCategories.Oscillators,
        [
            new PortSpec("in", NormalledTo: NodeCatalog.Clock, Domain: true) { Standard = true },
            new PortSpec("bpm", PortKind.Scalar, 120f, 40f, 240f)
            {
                Help = "The tempo, in beats a minute. Drums at the same tempo play together, and a psy kick ends before the next sixteenth.",
            },
            new PortSpec("swing", PortKind.Scalar, 0f, 0f, 1f)
            {
                Help = "Plays every second sixteenth late, for a shuffle.",
            },
            new PortSpec("trigger", PortKind.Scalar, 0f, 0f, 1f)
            {
                Lenient = true,
                Help = "With the rhythm on Trigger, each rise is a hit: a MIDI In's trigger, a sequencer's or a Euclid's gate.",
            },
            new PortSpec("velocity", PortKind.Scalar, 1f, 0f, 1f) { Help = "How hard it is hit." },
            new PortSpec("tune", PortKind.Scalar, 0f, -12f, 12f, Display: PortDisplay.Integer)
            {
                Help = "Up or down in semitones.",
            },
            new PortSpec("decay", PortKind.Scalar, 0.5f, 0f, 1f)
            {
                Help = "How long it rings: 0 is a tight click, 1 a long boom.",
            },
            new PortSpec("tone", PortKind.Scalar, 0.5f, 0f, 1f) { Help = "Dark at 0, bright and snappy at 1." },
            new PortSpec("drive", PortKind.Scalar, 0f, 0f, 1f)
            {
                Help = "Grit: rounds the peaks off. It never makes it louder.",
            },
            new PortSpec("pan", PortKind.Scalar, 0f, -1f, 1f) { Help = "Left at -1, right at 1, the middle at 0." },
        ],
        [
            new PortSpec("left", PortKind.Scalar, 0f, -1f, 1f)
            {
                Help = "The drum, for the Output's 'left' or a Desk. Wired alone it is heard in both ears.",
            },
            new PortSpec("right", PortKind.Scalar, 0f, -1f, 1f)
            {
                Help = "The drum for the Output's 'right'. It differs from 'left' only when panned.",
            },
            new PortSpec("env", PortKind.Scalar, 0f, 0f, 1f)
            {
                Help = "Jumps to 1 on each hit and falls with it: wire it into the picture to flash on the beat.",
            },
        ],
        Emit,
        "A drum that plays in time with nothing wired: pick a sound and it plays the rhythm "
        + "that suits it, a kick on every beat, a snare on two and four, hats on the eighths. "
        + "Drums at one tempo play together. Set the rhythm to Trigger to play it from a MIDI "
        + "In or a sequencer. It can never leave -1..1.")
    {
        Words = "drum machine, beat",
        Extras =
        [
            new SettingsExtra(
                StateKey,
                [
                    new ExtraField.Choice(
                        SoundKey,
                        "sound",
                        [
                            new ChoiceOption(Kit.Kick, "Kick"),
                            new ChoiceOption(Kit.PsyKick, "Psy kick"),
                            new ChoiceOption(Kit.Snare, "Snare"),
                            new ChoiceOption(Kit.Clap, "Clap"),
                            new ChoiceOption(Kit.ClosedHat, "Closed hat"),
                            new ChoiceOption(Kit.OpenHat, "Open hat"),
                            new ChoiceOption(Kit.Tom, "Tom"),
                            new ChoiceOption(Kit.Rim, "Rim"),
                            new ChoiceOption(Kit.Cowbell, "Cowbell"),
                        ],
                        Kit.Kick) { Help = "Which drum it is." },
                    new ExtraField.Choice(
                        RhythmKey,
                        "rhythm",
                        [
                            new ChoiceOption(Rhythms.Auto, "Auto (suits the sound)"),
                            new ChoiceOption(Rhythms.Beats, "Every beat"),
                            new ChoiceOption(Rhythms.Backbeat, "Backbeat (2 and 4)"),
                            new ChoiceOption(Rhythms.Eighths, "Eighths"),
                            new ChoiceOption(Rhythms.Sixteenths, "Sixteenths"),
                            new ChoiceOption(Rhythms.Offbeats, "Offbeats"),
                            new ChoiceOption(Rhythms.Tresillo, "Tresillo"),
                            new ChoiceOption(Rhythms.Clave, "Clave"),
                            new ChoiceOption(Rhythms.Bar, "Once a bar"),
                            new ChoiceOption(Rhythms.Trigger, "Trigger (plays on 'trigger')"),
                        ],
                        Rhythms.Auto) { Help = "When it plays: a pattern at 'bpm', or each rise of 'trigger'." },
                ]),
        ],
        Skin = Art.Skin("drum"),
    };

    /// <summary>A node with the given settings chosen, and every other at its fallback.</summary>
    public static NodeInstance Configure(NodeInstance node, params (string Key, string Value)[] chosen)
    {
        var state = new JsonObject();
        foreach (var (key, value) in chosen) state[key] = value;

        node.SetState(StateKey, state);

        return node;
    }

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var settings = node.Extra<ExtraState>(StateKey);

        string Chosen(string key, string fallback) =>
            settings?.Chosen(key) is { Length: > 0 } chosen ? chosen : fallback;

        var zero = em.Constant(0f);
        var one = em.Constant(1f);

        Slot Unit(int port) => em.Ternary(OpCode.Clamp, node[port], zero, one);

        var sound = Chosen(SoundKey, Kit.Kick);
        var rhythm = Chosen(RhythmKey, Rhythms.Auto);
        if (rhythm == Rhythms.Auto) rhythm = Rhythms.For(sound);

        var bpm = em.Ternary(OpCode.Clamp, node[BpmPort], em.Constant(SlowestBpm), em.Constant(FastestBpm));

        var (age, started) = rhythm == Rhythms.Trigger
            ? Struck(em, node[TriggerPort])
            : Rhythms.Age(em, node[InPort], bpm, Unit(SwingPort), rhythm);

        var tune = em.Binary(
            OpCode.Pow,
            em.Constant(2f),
            em.Mul(em.Ternary(OpCode.Clamp, node[TunePort], em.Constant(-24f), em.Constant(24f)), 1f / 12f));
        var length = em.Binary(
            OpCode.Pow,
            em.Constant(2f),
            em.Mul(em.Add(Unit(DecayPort), -0.5f), 2f * DecayOctaves));

        var sixteenth = em.Binary(OpCode.Div, em.Constant(15f), bpm);

        var (drum, envelope) = Kit.Emit(em, sound, node[InPort], age, tune, length, Unit(TonePort), sixteenth);

        var hit = em.Mul(started, Unit(VelocityPort));
        var heard = Finish.Driven(em, em.Mul(drum, hit), Unit(DrivePort));
        var sides = Finish.Panned(em, heard, heard, node[PanPort]);

        return [sides[0], sides[1], em.Ternary(OpCode.Clamp, em.Mul(envelope, hit), zero, one)];
    }

    /// <summary>
    /// Seconds since 'trigger' last rose, and 1 once it has. Where there is nothing to
    /// remember, the trigger itself: a hit while it is up and silence while it is down.
    /// </summary>
    private static (Slot Age, Slot Started) Struck(Emitter em, Slot trigger)
    {
        var zero = em.Constant(0f);
        var one = em.Constant(1f);

        var step = em.Interval();
        var live = em.HasMemory();

        var ageCell = em.AllocateUnitSlot();
        var struckCell = em.AllocateUnitSlot();
        var upCell = em.AllocateUnitSlot();

        var open = em.Binary(OpCode.Step, em.Constant(0.5f), trigger);
        var rise = em.Mul(open, em.Sub(one, em.UnitRead(upCell)));

        var struck = em.Binary(OpCode.Max, em.UnitRead(struckCell), rise);
        var age = em.Mul(
            em.Sub(one, rise),
            em.Binary(OpCode.Min, em.Add(em.UnitRead(ageCell), step), em.Constant(LongestAge)));

        // A clock write, because an age runs past the ±16 a signal cell holds.
        em.ClockWrite(ageCell, age);
        em.UnitWrite(struckCell, struck);
        em.UnitWrite(upCell, open);

        var still = em.Mul(em.Sub(one, open), LongestAge);

        return (em.Ternary(OpCode.Mix, still, age, live), em.Ternary(OpCode.Mix, one, struck, live));
    }
}
