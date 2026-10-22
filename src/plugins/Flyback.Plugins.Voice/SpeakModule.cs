using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Voice.Speech;

namespace Flyback.Plugins.Voice;

/// <summary>
/// A voice that says the words typed into it: a throat's buzz and a breath of
/// noise through five formant filters, the way a Klatt synthesizer or DECtalk
/// speaks.
/// </summary>
/// <remarks>
/// The words become tracks when the patch is built (<see cref="Utterance"/>), so
/// what plays is a glottal pulse, a noise, five of the engine's filters in a row
/// and one beside them for the hiss of an 's', all steered by eight Tables.
/// </remarks>
internal static class SpeakModule
{
    public const string TypeId = "flyback.voice.speak";

    /// <summary>How much of a period the vocal folds are open for.</summary>
    private const float Open = 0.6f;

    /// <summary>The two formants no vowel moves, which give a voice its body.</summary>
    private const float F4 = 3300f;
    private const float F5 = 3850f;

    /// <summary>The pitch the level is set at; a voice above it is turned down to match.</summary>
    private const float Low = 110f;

    /// <summary>What brings a sentence to about -18 LUFS at full level.</summary>
    private const float Gain = 0.25f;

    private const float Tau = 6.283185307179586f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Speak", ModuleCategories.Oscillators,
        [
            new PortSpec("in", NormalledTo: NodeCatalog.Clock, Domain: true) { Standard = true },
            new PortSpec("pitch", PortKind.Scalar, 110f, 40f, 500f)
            {
                Knee = 40f,
                Help = "The voice's pitch in hertz before the sentence moves it: about 110 a man, 200 a woman.",
            },
            new PortSpec("speed", PortKind.Scalar, 1f, 0.25f, 3f) { Help = "How fast it talks." },
            new PortSpec("level", PortKind.Scalar, 1f, 0f, 1f) { Help = "How loud, or an envelope." },
            new PortSpec("trigger", PortKind.Scalar, 0f, 0f, 1f)
            {
                Lenient = true,
                Help = "Each rise starts the words again from the top.",
            },
            new PortSpec("every", PortKind.Scalar, 4f, 0f, 30f)
            {
                Help = "Says the words again this many seconds after it started them; 0 says them once a trigger.",
            },
            new PortSpec("size", PortKind.Scalar, 1f, 0.6f, 1.6f)
            {
                Help = "The size of the throat: above 1 a smaller one, a child's or a woman's; below it a giant's.",
            },
        ],
        [new PortSpec("out") { Help = "The voice." }],
        Emit,
        "Speech from pure synthesis: type words into it and it says them, like an old talking "
        + "computer. Audio only.")
    {
        Words = "speech, talk, voice, text to speech, formant",
        Extras = [new SpeechExtra()],
        Sinks = ModuleSinks.Audio,
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Oscillators))
        {
            Glyph = "M4,5 L20,5 L20,15 L11,15 L6,19 L7,15 L4,15 Z M8,10 L8,10.5 M12,9 L12,11.5 M16,10 L16,10.5",
        },
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        if (node.Extra<Utterance>(SpeechExtra.StateKey) is not { } said) return [em.Constant(0f)];

        var zero = em.Constant(0f);
        var one = em.Constant(1f);
        var now = node[0];
        var size = node[6];

        // Where in the words it is: seconds since the last trigger, wrapped at 'every'.
        var startCell = em.AllocateUnitSlot();
        var triggerCell = em.AllocateUnitSlot();
        var open = em.Binary(OpCode.Step, em.Constant(0.5f), node[4]);
        var rose = em.Mul(open, em.Sub(one, em.UnitRead(triggerCell)));
        var start = em.Ternary(OpCode.Mix, em.UnitRead(startCell), now, rose);
        em.ClockWrite(startCell, start);
        em.UnitWrite(triggerCell, open);

        var since = em.Sub(now, start);
        var repeats = em.Binary(OpCode.Step, em.Constant(1e-3f), node[5]);
        var wrapped = em.Ternary(OpCode.Mix, since, em.Binary(OpCode.Mod, since, node[5]), repeats);
        var at = em.Mul(wrapped, node[2]);

        var voice = em.Table(at, said.Voice);
        var breath = em.Table(at, said.Breath);
        var hiss = em.Table(at, said.Hiss);

        // The glottal pulse after Klatt and Klatt (1990): the flow's slope, rising
        // while the folds open and snapping shut, then nothing while they are closed.
        var vibrato = em.Add(em.Mul(em.Unary(OpCode.Sin, em.Mul(now, Tau * 5.2f)), 0.006f), 1f);
        var f0 = em.Mul(em.Mul(node[1], em.Table(at, said.Pitch)), vibrato);
        var phase = em.Unary(OpCode.Fract, em.Phase(now, f0, zero));
        var x = em.Mul(phase, 1f / Open);
        var opened = em.Sub(one, em.Binary(OpCode.Step, em.Constant(Open), phase));
        var pulse = em.Mul(em.Sub(em.Mul(x, 2f), em.Mul(em.Mul(x, x), 3f)), opened);

        // A high voice puts each harmonic further up a formant's peak, so it is
        // turned down to say a sentence as loud at any pitch.
        var low = em.Constant(Low);
        pulse = em.Mul(pulse, em.Unary(OpCode.Sqrt, em.Binary(OpCode.Div, low, em.Binary(OpCode.Max, node[1], low))));

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

        return [em.Mul(em.Mul(em.Add(tract, teeth), Gain), node[3])];

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
