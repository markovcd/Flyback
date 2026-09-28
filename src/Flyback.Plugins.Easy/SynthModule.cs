using System.Text.Json.Nodes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;

namespace Flyback.Plugins.Easy;

/// <summary>
/// A whole synth voice in one module that sounds good with nothing wired: an
/// oscillator picked by name, a sub and noise under it, an envelope, a filter
/// that follows the note, two LFOs, glide, drive and pan.
/// </summary>
/// <remarks>
/// Every knob is clamped before it is used and the output is held inside -1..1,
/// so no setting and no wire can make it louder than full scale. Pitch is a note
/// number, so a MIDI In or a Note Sequencer wires straight in. The wave, the filter
/// and where each LFO goes are settings because they decide which ops are emitted
/// (ADR-0097). The envelope, the glide and the filter keep cells, so the screen gets
/// the gate, the note and the unfiltered wave.
/// </remarks>
internal static class SynthModule
{
    public const string TypeId = "flyback.easy.synth";

    public const string StateKey = "synth";

    public const string WaveKey = "wave";
    public const string FilterKey = "filter";
    public const string Lfo1ShapeKey = "lfo1_shape";
    public const string Lfo1TargetKey = "lfo1_to";
    public const string Lfo2ShapeKey = "lfo2_shape";
    public const string Lfo2TargetKey = "lfo2_to";

    public const int InPort = 0;
    public const int PitchPort = 1;
    public const int GatePort = 2;
    public const int VelocityPort = 3;
    public const int OctavePort = 4;
    public const int GlidePort = 5;
    public const int SubPort = 6;
    public const int NoisePort = 7;
    public const int AttackPort = 8;
    public const int DecayPort = 9;
    public const int SustainPort = 10;
    public const int ReleasePort = 11;
    public const int BrightPort = 12;
    public const int ResonancePort = 13;
    public const int SweepPort = 14;
    public const int Lfo1RatePort = 15;
    public const int Lfo1DepthPort = 16;
    public const int Lfo2RatePort = 17;
    public const int Lfo2DepthPort = 18;
    public const int DrivePort = 19;
    public const int PanPort = 20;

    public const int LeftPort = 0;
    public const int RightPort = 1;
    public const int EnvPort = 2;
    public const int LfoPort = 3;

    public const string Pitch = "pitch";
    public const string Filter = "filter";
    public const string Volume = "volume";
    public const string Pan = "pan";

    public const string Low = "low";
    public const string Band = "band";
    public const string High = "high";
    public const string Off = "off";

    /// <summary>A low pass's corner at 'bright' 0 and how far 1 opens it, in octaves over the note.</summary>
    private const float DarkestOctaves = 0.5f, BrightOctaves = 8f;

    /// <summary>A high pass's corner at 'bright' 0, in octaves over the note.</summary>
    private const float HighPassOctaves = 4.5f;

    /// <summary>How far a full 'sweep' opens the filter at the envelope's peak, in octaves.</summary>
    private const float SweepOctaves = 5f;

    /// <summary>A full LFO's swing: semitones on the pitch, before the depth is squared, and octaves on the filter.</summary>
    private const float VibratoSemitones = 12f, WahOctaves = 3f;

    /// <summary>Resonance at a full knob, short of where the filter screams.</summary>
    private const float MostResonance = 0.9f;

    private const float LowestCutoff = 20f, HighestCutoff = 16_000f;

    /// <summary>How hard a full 'drive' pushes into the curve.</summary>
    private const float HardestDrive = 9f;

    private const float LongestGlide = 2f;

    private const float FastestLfo = 50f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Easy Synth", ModuleCategories.Oscillators,
        [
            new PortSpec("in", NormalledTo: NodeCatalog.Clock, Domain: true) { Standard = true },
            new PortSpec("pitch", PortKind.Scalar, 57f, 0f, 127f, Display: PortDisplay.Note)
            {
                Help = "The note to play: 60 is middle C. A MIDI In's or a Note Sequencer's note wires straight in.",
            },
            new PortSpec("gate", PortKind.Scalar, 1f, 0f, 1f)
            {
                Lenient = true,
                Help = "Up, the note sounds; down, it fades over 'release'. Unwired it stays up and drones.",
            },
            new PortSpec("velocity", PortKind.Scalar, 1f, 0f, 1f)
            {
                Help = "How hard the note is played: louder, and brighter when 'sweep' is up.",
            },
            new PortSpec("octave", PortKind.Scalar, 0f, -3f, 3f, Display: PortDisplay.Integer)
            {
                Help = "Moves the note up or down by whole octaves.",
            },
            new PortSpec("glide", PortKind.Scalar, 0f, 0f, LongestGlide)
            {
                Help = "Seconds to slide from one note to the next. 0 jumps.",
            },
            new PortSpec("sub", PortKind.Scalar, 0f, 0f, 1f) { Help = "A square an octave down, for weight." },
            new PortSpec("noise", PortKind.Scalar, 0f, 0f, 1f) { Help = "Hiss mixed in, for breath or grit." },
            new PortSpec("attack", PortKind.Scalar, -2.5f, -4f, 1.5f, Display: PortDisplay.Duration) { Standard = true },
            new PortSpec("decay", PortKind.Scalar, -0.6f, -4f, 1.5f, Display: PortDisplay.Duration)
            {
                Help = "How long the fall from the peak to 'sustain' takes.",
            },
            new PortSpec("sustain", PortKind.Scalar, 0.6f, 0f, 1f) { Help = "The level held while the gate stays up." },
            new PortSpec("release", PortKind.Scalar, -0.6f, -4f, 1.5f, Display: PortDisplay.Duration)
            {
                Help = "How long the fade to silence takes once the gate drops.",
            },
            new PortSpec("bright", PortKind.Scalar, 0.5f, 0f, 1f)
            {
                Help = "How much the filter lets through: 0 is muffled, 1 wide open. It follows the note, so every key is as bright.",
            },
            new PortSpec("resonance", PortKind.Scalar, 0.2f, 0f, 1f) { Standard = true },
            new PortSpec("sweep", PortKind.Scalar, 0.4f, 0f, 1f)
            {
                Help = "How far each note's envelope opens the filter: the pluck, the wow.",
            },
            new PortSpec("lfo1 rate", PortKind.Scalar, 5f, 0f, 20f) { Knee = 0.05f, Help = "LFO 1's speed, in wobbles a second." },
            new PortSpec("lfo1 depth", PortKind.Scalar, 0f, 0f, 1f) { Help = "How much LFO 1 moves what it is set to." },
            new PortSpec("lfo2 rate", PortKind.Scalar, 0.3f, 0f, 20f) { Knee = 0.05f, Help = "LFO 2's speed, in wobbles a second." },
            new PortSpec("lfo2 depth", PortKind.Scalar, 0f, 0f, 1f) { Help = "How much LFO 2 moves what it is set to." },
            new PortSpec("drive", PortKind.Scalar, 0f, 0f, 1f)
            {
                Help = "Warmth: rounds the peaks off. It never makes it louder.",
            },
            new PortSpec("pan", PortKind.Scalar, 0f, -1f, 1f) { Help = "Left at -1, right at 1, the middle at 0." },
        ],
        [
            new PortSpec("left", PortKind.Scalar, 0f, -1f, 1f)
            {
                Help = "The sound, for the Output's 'left'. Wired alone it is heard in both ears.",
            },
            new PortSpec("right", PortKind.Scalar, 0f, -1f, 1f)
            {
                Help = "The sound for the Output's 'right'. It differs from 'left' when panned or on Supersaw.",
            },
            new PortSpec("env", PortKind.Scalar, 0f, 0f, 1f)
            {
                Help = "The envelope, 0 to 1: wire it into the picture to flash with each note.",
            },
            new PortSpec("lfo") { Standard = true },
        ],
        Emit,
        "A synth that sounds good with nothing wired: pick a wave, then wire a MIDI In's pitch, "
        + "gate and velocity, or a Note Sequencer, and 'left' and 'right' to the Output. It has an "
        + "envelope, a filter that follows the note, two LFOs, a sub, noise, glide, drive and pan, "
        + "and can never leave -1..1.")
    {
        Extras =
        [
            new SettingsExtra(
                StateKey,
                [
                    new ExtraField.Choice(
                        WaveKey,
                        "wave",
                        [
                            new ChoiceOption(Waves.Sine, "Sine"),
                            new ChoiceOption(Waves.Triangle, "Triangle"),
                            new ChoiceOption(Waves.Saw, "Saw"),
                            new ChoiceOption(Waves.Square, "Square"),
                            new ChoiceOption(Waves.Pulse, "Pulse"),
                            new ChoiceOption(Waves.Supersaw, "Supersaw"),
                            new ChoiceOption(Waves.Organ, "Organ"),
                        ],
                        Waves.Saw) { Help = "The oscillator's shape: sine is pure, saw is bright, supersaw is huge." },
                    new ExtraField.Choice(
                        FilterKey,
                        "filter",
                        [
                            new ChoiceOption(Low, "Low pass"),
                            new ChoiceOption(Band, "Band pass"),
                            new ChoiceOption(High, "High pass"),
                            new ChoiceOption(Off, "Off"),
                        ],
                        Low) { Help = "What the filter keeps: the lows, a band, the highs, or everything." },
                    Shape(Lfo1ShapeKey, "lfo1 shape", Lfo.Sine),
                    Target(Lfo1TargetKey, "lfo1 goes to", Pitch),
                    Shape(Lfo2ShapeKey, "lfo2 shape", Lfo.Triangle),
                    Target(Lfo2TargetKey, "lfo2 goes to", Filter),
                ]),
        ],
        Skin = Art.Skin("synth"),
    };

    private static ExtraField.Choice Shape(string key, string label, string fallback) => new(
        key,
        label,
        [
            new ChoiceOption(Lfo.Sine, "Sine"),
            new ChoiceOption(Lfo.Triangle, "Triangle"),
            new ChoiceOption(Lfo.Square, "Square"),
            new ChoiceOption(Lfo.Ramp, "Ramp"),
            new ChoiceOption(Lfo.Random, "Random"),
        ],
        fallback) { Help = "How the LFO moves: smoothly, in steps, or to a new random place each cycle." };

    private static ExtraField.Choice Target(string key, string label, string fallback) => new(
        key,
        label,
        [
            new ChoiceOption(Pitch, "Pitch (vibrato)"),
            new ChoiceOption(Filter, "Filter (wah)"),
            new ChoiceOption(Volume, "Volume (tremolo)"),
            new ChoiceOption(Pan, "Pan (auto-pan)"),
        ],
        fallback) { Help = "What the LFO wobbles." };

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

        var domain = node[InPort];
        var velocity = Unit(VelocityPort);

        var lfo1 = Lfo.Emit(em, domain, Rate(Lfo1RatePort), Chosen(Lfo1ShapeKey, Lfo.Sine), 1f);
        var lfo2 = Lfo.Emit(em, domain, Rate(Lfo2RatePort), Chosen(Lfo2ShapeKey, Lfo.Triangle), 2f);

        // What each destination is moved by: the sum of whichever LFOs are sent there.
        var sent = new Dictionary<string, Slot>(StringComparer.Ordinal);
        Send(Chosen(Lfo1TargetKey, Pitch), lfo1, Unit(Lfo1DepthPort));
        Send(Chosen(Lfo2TargetKey, Filter), lfo2, Unit(Lfo2DepthPort));

        Slot Moved(string target) => sent.TryGetValue(target, out var by) ? by : zero;

        // The note, in semitones, glided and bent before it becomes hertz.
        var octave = em.Unary(OpCode.Floor, em.Add(em.Ternary(OpCode.Clamp, node[OctavePort], em.Constant(-4f), em.Constant(4f)), 0.5f));
        var note = em.Add(em.Ternary(OpCode.Clamp, node[PitchPort], zero, em.Constant(127f)), em.Mul(octave, 12f));
        note = Glide(em, note, em.Ternary(OpCode.Clamp, node[GlidePort], zero, em.Constant(LongestGlide)));
        note = em.Add(note, em.Mul(Moved(Pitch), VibratoSemitones));

        // Kept on the keyboard, so no octave or vibrato reaches past hearing.
        note = em.Ternary(OpCode.Clamp, note, zero, em.Constant(127f));

        var hz = em.Mul(em.Binary(OpCode.Pow, em.Constant(2f), em.Mul(em.Add(note, -69f), 1f / 12f)), 440f);

        var (left, right, stereo) = Waves.Emit(em, domain, hz, Chosen(WaveKey, Waves.Saw));

        // Sub and noise are shared out with the wave rather than added to it, so they thicken it and never make it louder.
        var sub = Unit(SubPort);
        var noise = Unit(NoisePort);
        var under = em.Add(
            em.Mul(Waves.SquareOf(em, em.Phase(domain, em.Mul(hz, 0.5f), zero)), sub),
            em.Mul(NodeCatalog.WhiteAndPink(em, domain, zero).White, noise));
        var share = em.Binary(OpCode.Div, one, em.Add(em.Add(sub, noise), 1f));

        left = em.Mul(em.Add(left, under), share);
        right = stereo ? em.Mul(em.Add(right, under), share) : left;

        var env = Envelope.Emit(em, node[GatePort], node[AttackPort], node[DecayPort], Unit(SustainPort), node[ReleasePort]);

        var filter = Chosen(FilterKey, Low);
        if (filter != Off)
        {
            // How open the filter is, in octaves: more lets more through whichever response is kept.
            var open = em.Add(
                em.Mul(Unit(BrightPort), BrightOctaves),
                em.Add(
                    em.Mul(em.Mul(em.Mul(env, velocity), Unit(SweepPort)), SweepOctaves),
                    em.Mul(Moved(Filter), WahOctaves)));

            // A low pass opens upward from the note, a high pass downward onto it, and a band climbs from it.
            var octaves = filter switch
            {
                High => em.Sub(em.Constant(HighPassOctaves), open),
                Band => em.Mul(open, 0.5f),
                _ => em.Add(open, DarkestOctaves),
            };
            var cutoff = em.Ternary(
                OpCode.Clamp,
                em.Mul(hz, em.Binary(OpCode.Pow, em.Constant(2f), octaves)),
                em.Constant(LowestCutoff),
                em.Constant(HighestCutoff));

            var ringing = Unit(ResonancePort);
            var resonance = em.Mul(ringing, MostResonance);

            // The band is scaled to peak at full whatever the resonance; the others are turned down as it rises.
            var level = filter == Band
                ? em.Add(em.Mul(resonance, -1.95f), 2f)
                : em.Binary(OpCode.Div, one, em.Add(em.Mul(ringing, 1.5f), 1f));

            left = Filtered(left);
            right = stereo ? Filtered(right) : left;

            Slot Filtered(Slot dry)
            {
                var responses = NodeCatalog.FilterResponses(em, dry, cutoff, resonance);

                var kept = filter switch
                {
                    Band => responses[1],
                    High => responses[2],
                    _ => responses[0],
                };

                return em.Mul(kept, level);
            }
        }

        var tremolo = em.Binary(OpCode.Max, em.Sub(one, Moved(Volume)), zero);
        var gain = em.Mul(em.Mul(env, velocity), tremolo);
        var drive = Unit(DrivePort);

        left = Driven(em.Mul(left, gain));
        right = stereo ? Driven(em.Mul(right, gain)) : left;

        var pan = em.Ternary(OpCode.Clamp, em.Add(node[PanPort], Moved(Pan)), em.Constant(-1f), one);

        return
        [
            Rail(em.Mul(left, em.Binary(OpCode.Min, em.Sub(one, pan), one))),
            Rail(em.Mul(right, em.Binary(OpCode.Min, em.Add(pan, 1f), one))),
            env,
            lfo1,
        ];

        Slot Rate(int port) => em.Ternary(OpCode.Clamp, node[port], zero, em.Constant(FastestLfo));

        void Send(string target, Slot lfo, Slot depth)
        {
            // The pitch's depth is squared, so the bottom of the knob is a vibrato and the top
            // a siren. The volume only ever dips from full, by as much as the depth.
            var by = target switch
            {
                Pitch => em.Mul(lfo, em.Mul(depth, depth)),
                Volume => em.Mul(em.Mul(em.Sub(one, lfo), depth), 0.5f),
                _ => em.Mul(lfo, depth),
            };

            sent[target] = sent.TryGetValue(target, out var already) ? em.Add(already, by) : by;
        }

        // Faded in with the knob, so 0 is clean; the curve is the Drive module's, divided back out to full scale.
        Slot Driven(Slot dry)
        {
            var push = em.Add(em.Mul(drive, HardestDrive), 1f);
            var pushed = em.Mul(dry, push);
            var curve = em.Binary(OpCode.Div, pushed, em.Add(em.Unary(OpCode.Abs, pushed), 1f));
            var ceiling = em.Binary(OpCode.Div, push, em.Add(push, 1f));

            return em.Ternary(OpCode.Mix, dry, em.Binary(OpCode.Div, curve, ceiling), drive);
        }

        Slot Rail(Slot signal) => em.Ternary(OpCode.Clamp, signal, em.Constant(-1f), one);
    }

    /// <summary>
    /// The note, catching up with where it is sent: exponential, so a glide takes the
    /// same time whatever the interval. A wire where there is nothing to remember.
    /// </summary>
    private static Slot Glide(Emitter em, Slot target, Slot seconds)
    {
        var step = em.Interval();
        var live = em.HasMemory();

        var cell = em.AllocateUnitSlot();
        var held = em.UnitRead(cell);

        // Within a hundredth of the way there after 'seconds'.
        var tau = em.Mul(em.Binary(OpCode.Max, seconds, em.Constant(1e-4f)), 1f / 4.6f);
        var closed = em.Sub(em.Constant(1f), em.Unary(OpCode.Exp, em.Unary(OpCode.Neg, em.Binary(OpCode.Div, step, tau))));

        var next = em.Ternary(OpCode.Mix, target, em.Ternary(OpCode.Mix, held, target, closed), live);

        // A clock write, because a note number is past the ±16 a signal cell holds.
        em.ClockWrite(cell, next);

        return next;
    }
}
