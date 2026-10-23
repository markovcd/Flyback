using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Speech;

/// <summary>
/// A voice that says or sings the words typed into it: a throat's buzz and a
/// breath of noise through five formant filters, the way a Klatt synthesizer or
/// DECtalk speaks.
/// </summary>
/// <remarks>
/// The words become tracks when the patch is built (<see cref="Utterance"/>), so
/// what plays is a glottal pulse, a noise, five of the engine's filters in a row
/// and one beside them for the hiss of an 's', all steered by Tables. Where in the
/// words it is depends only on the clock, but for a trigger, so the mouth and the
/// caption it drives are the same on the picture as at the speakers.
/// </remarks>
internal static class SpeakModule
{
    public const string TypeId = "flyback.speech.speak";

    /// <summary>How much of a period the vocal folds are open for.</summary>
    private const float Open = 0.6f;

    /// <summary>The two formants no vowel moves, which give a voice its body.</summary>
    private const float F4 = 3300f;
    private const float F5 = 3850f;

    /// <summary>The pitch the level is set at; a voice above it is turned down to match.</summary>
    private const float Low = 110f;

    /// <summary>What brings a sentence to about -18 LUFS at full level.</summary>
    private const float Gain = 0.25f;

    /// <summary>How long a sung note takes to slide up or down from the last.</summary>
    private const float Glide = 0.07f;

    /// <summary>How long the voice takes to fade out into a rest, or back in after one.</summary>
    private const float Fade = 0.02f;

    private const float Tau = 6.283185307179586f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Speak", ModuleCategories.Oscillators,
        [
            new PortSpec("in", NormalledTo: NodeCatalog.Clock, Domain: true) { Standard = true },
            new PortSpec("pitch", PortKind.Scalar, 110f, 40f, 500f)
            {
                Knee = 40f,
                Help = "The voice's pitch in hertz: about 110 a man, 200 a woman. In Sing, 110 sings the "
                    + "notes as written and 220 an octave up.",
            },
            new PortSpec("speed", PortKind.Scalar, 1f, 0.25f, 3f) { Help = "How fast it talks." },
            new PortSpec("level", PortKind.Scalar, 1f, 0f, 1f) { Help = "How loud, or an envelope." },
            new PortSpec("trigger", PortKind.Scalar, 0f, 0f, 1f)
            {
                Lenient = true,
                Help = "Each rise starts the words again from the top. Only heard: the picture keeps to the clock.",
            },
            new PortSpec("every", PortKind.Scalar, 4f, 0f, 30f)
            {
                Help = "Says the words again this many seconds after it started them; 0 says them once a trigger.",
            },
            new PortSpec("size", PortKind.Scalar, 1f, 0.6f, 1.6f)
            {
                Help = "The size of the throat: above 1 a smaller one, a child's or a woman's; below it a giant's.",
            },
            new PortSpec("bpm", PortKind.Scalar, 120f, 30f, 300f)
            {
                Help = "In Sing, the tempo the notes' beats are counted at. Share it with the music's Tempo.",
            },
        ],
        [
            new PortSpec("out") { Help = "The voice. Silent on the picture." },
            new PortSpec("open", PortKind.Scalar, 0f, 0f, 1f) { Help = "How far the mouth is open, for a Mouth." },
            new PortSpec("round", PortKind.Scalar, 0f, 0f, 1f) { Help = "How round the lips are, for a Mouth." },
            new PortSpec("line", PortKind.Scalar, 0f, 0f, 16f)
            {
                Help = "Which line of the words is being said, for a Text's 'line'.",
            },
            new PortSpec("reveal", PortKind.Scalar, 0f, 0f, 1f)
            {
                Help = "How much of that line has been said, for a Text's 'reveal'.",
            },
        ],
        Emit,
        "Speech from pure synthesis: type words into it and it says them, or sings them to notes, "
        + "like an old talking computer. Its mouth and its words can be drawn with a Mouth and a Text.")
    {
        Words = "speech, talk, sing, voice, text to speech, formant",
        Extras = [new SpeechExtra()],
        Skin = Art.Skin("speak"),
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        if (node.Extra<Saying>(SpeechExtra.StateKey) is not { } saying)
        {
            var nought = em.Constant(0f);
            return [nought, nought, nought, nought, nought];
        }

        var said = saying.Said;
        var zero = em.Constant(0f);
        var one = em.Constant(1f);
        var now = node[0];
        var size = node[6];

        var (at, f0, level) = saying.Tune is { } tune ? Sung(tune) : Spoken();

        var voice = em.Table(at, said.Voice);
        var breath = em.Table(at, said.Breath);
        var hiss = em.Table(at, said.Hiss);

        // The glottal pulse after Klatt and Klatt (1990): the flow's slope, rising
        // while the folds open and snapping shut, then nothing while they are closed.
        var phase = em.Unary(OpCode.Fract, em.Phase(now, f0, zero));
        var x = em.Mul(phase, 1f / Open);
        var opened = em.Sub(one, em.Binary(OpCode.Step, em.Constant(Open), phase));
        var pulse = em.Mul(em.Sub(em.Mul(x, 2f), em.Mul(em.Mul(x, x), 3f)), opened);

        // A high voice puts each harmonic further up a formant's peak, so it is
        // turned down to say a sentence as loud at any pitch.
        var low = em.Constant(Low);
        pulse = em.Mul(pulse, em.Unary(OpCode.Sqrt, em.Binary(OpCode.Div, low, em.Binary(OpCode.Max, f0, low))));

        var (noise, _) = NodeCatalog.WhiteAndPink(em, now, zero);

        // The throat and the breath go through the formants one after another.
        var tract = em.Add(em.Mul(pulse, voice), em.Mul(em.Mul(noise, breath), 1.5f));
        tract = Formant(tract, em.Mul(em.Table(at, said.F1), size), 90f);
        tract = Formant(tract, em.Mul(em.Table(at, said.F2), size), 110f);
        tract = Formant(tract, em.Mul(em.Table(at, said.F3), size), 170f);
        tract = Formant(tract, em.Mul(size, F4), 250f);
        tract = Formant(tract, em.Mul(size, F5), 300f);

        // The hiss of an 's' or a 't' is made at the teeth, past the formants.
        var hissHz = em.Mul(em.Table(at, said.HissHz), size);
        var hissWidth = Resonance(hissHz, em.Mul(hissHz, 0.5f));
        var teeth = NodeCatalog.FilterResponses(em, em.Mul(noise, hiss), hissHz, hissWidth)[1];
        teeth = NodeCatalog.FilterResponses(em, teeth, hissHz, hissWidth)[1];

        // The picture has no memory to filter with, and hears nothing.
        var heard = em.Mul(em.Mul(em.Mul(em.Mul(em.Add(tract, teeth), Gain), node[3]), level), em.HasMemory());

        return
        [
            heard,
            em.Mul(em.Table(at, said.Open), level),
            em.Table(at, said.Round),
            em.Unary(OpCode.Floor, em.Add(em.Table(at, said.Line), 0.5f)),
            em.Table(at, said.Reveal),
        ];

        // Seconds since the last trigger, wrapped at 'every', with the sentence's
        // own rise and fall on the pitch.
        (Slot At, Slot Hz, Slot Level) Spoken()
        {
            var startCell = em.AllocateUnitSlot();
            var triggerCell = em.AllocateUnitSlot();
            var rising = em.Binary(OpCode.Step, em.Constant(0.5f), node[4]);
            var rose = em.Mul(rising, em.Sub(one, em.UnitRead(triggerCell)));
            var start = em.Ternary(OpCode.Mix, em.UnitRead(startCell), now, rose);
            em.ClockWrite(startCell, start);
            em.UnitWrite(triggerCell, rising);

            var since = em.Sub(now, start);
            var repeats = em.Binary(OpCode.Step, em.Constant(1e-3f), node[5]);
            var wrapped = em.Ternary(OpCode.Mix, since, em.Binary(OpCode.Mod, since, node[5]), repeats);
            var spokenAt = em.Mul(wrapped, node[2]);

            var vibrato = em.Add(em.Mul(em.Unary(OpCode.Sin, em.Mul(now, Tau * 5.2f)), 0.006f), 1f);
            return (spokenAt, em.Mul(em.Mul(node[1], em.Table(spokenAt, said.Pitch)), vibrato), one);
        }

        // The slot the beat is in, a syllable or a rest, and how far into its note:
        // the consonants before its vowel are said from the note's start, and the
        // vowel is held.
        (Slot At, Slot Hz, Slot Level) Sung(Tune tune)
        {
            var bpm = em.Binary(OpCode.Max, node[7], em.Constant(1f));
            var beat = em.Binary(OpCode.Mod, em.Mul(now, em.Mul(bpm, 1f / 60f)), em.Constant(tune.Beats));

            var slot = zero;
            for (var n = 1; n < tune.Count; n++)
                slot = em.Add(slot, em.Binary(OpCode.Step, em.Constant(tune.Begins[n]), beat));

            var into = em.Binary(OpCode.Div, em.Mul(em.Sub(beat, em.Table(slot, tune.BeginAt)), 60f), bpm);
            var start = em.Table(slot, tune.Starts);
            var held = em.Sub(em.Table(slot, tune.Holds), start);
            var sungAt = em.Add(start, em.Binary(OpCode.Min, em.Mul(into, node[2]), held));

            var note = em.Table(slot, tune.Hz);
            var last = em.Binary(OpCode.Mod, em.Add(slot, tune.Count - 1f), em.Constant(tune.Count));
            var before = em.Table(last, tune.Hz);
            var slid = em.Ternary(OpCode.Mix, before, note, em.Ternary(OpCode.Clamp, em.Mul(into, 1f / Glide), zero, one));
            var written = em.Mul(slid, em.Mul(node[1], 1f / Low));

            // A note with no pitch written is sung at the knob.
            var hz = em.Ternary(OpCode.Mix, node[1], written, em.Binary(OpCode.Step, one, em.Binary(OpCode.Min, before, note)));

            // A singer's vibrato comes in once the note has settled.
            var depth = em.Add(em.Mul(em.Ternary(OpCode.Clamp, em.Mul(em.Add(into, -0.25f), 3f), zero, one), 0.012f), 0.003f);
            var vibrato = em.Add(em.Mul(em.Unary(OpCode.Sin, em.Mul(now, Tau * 5.5f)), depth), 1f);

            // A rest fades the voice out, and the note after it fades it back in.
            var level = em.Ternary(OpCode.Mix, em.Table(last, tune.Heard), em.Table(slot, tune.Heard),
                em.Ternary(OpCode.Clamp, em.Mul(into, 1f / Fade), zero, one));

            return (sungAt, em.Mul(hz, vibrato), level);
        }

        // A formant as a lowpass that rings at its corner: the cascade Klatt used, a
        // resonator that passes the bass unchanged and peaks by its frequency over its
        // width. A smaller throat widens every formant as it raises it, so 'size' keeps the level.
        Slot Formant(Slot input, Slot hz, float width) =>
            NodeCatalog.FilterResponses(em, input, hz, Resonance(hz, em.Mul(size, width)))[0];

        // The Filter's resonance for a bandwidth. Its damping is one over Q, which is
        // width over frequency, and runs from 2 at no resonance to 0.05 at full.
        Slot Resonance(Slot hz, Slot width) =>
            em.Mul(em.Sub(em.Constant(2f), em.Binary(OpCode.Div, width, hz)), 1f / 1.95f);
    }
}
