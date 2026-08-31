using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Effects;

/// <summary>
/// Notches instead of a comb. A flanger cancels at every multiple of one
/// frequency because a delay does the same thing to all of them; a phaser
/// cancels at a handful of places that are not related to each other at all,
/// because what it delays is phase rather than time.
/// </summary>
/// <remarks>
/// Four allpass stages, each one leaving every frequency at the same level and
/// turning the phase of the high ones further than the low. Added back to the
/// dry signal, the places where the two have ended up half a cycle apart cancel:
/// two notches for four stages, and sweeping the stages moves both.
/// <para>
/// This is the module that would have needed an opcode before ADR-0041. Every
/// stage is one one-evaluation cell, and the arrangement is the same TPT that
/// the Timbre plugin's Filter uses, read as an allpass instead of a lowpass —
/// twice the lowpass minus the input, which is what a lowpass and its own
/// complement come to. The Delay and Allpass opcodes are no use here: those
/// carry a buffer, and what this needs is one sample of phase per stage.
/// </para>
/// </remarks>
internal static class PhaserModule
{
    public const string TypeId = "flyback.effects.phaser";

    /// <summary>
    /// Four, which is two notches. Six is the other common answer and is lusher;
    /// eight starts to sound like a filter sweep rather than a phaser. Like the
    /// Supersaw's seven voices this is fixed rather than a knob, because an emit
    /// function writes straight-line ops and never sees a knob's value.
    /// </summary>
    private const int Stages = 4;

    /// <summary>Where the notches sweep between. Swept in octaves, since that is how the ear hears the distance.</summary>
    private const float Lowest = 180f;

    private const float Highest = 2400f;

    /// <summary>
    /// The most of the evaluation rate the coefficient may stand for. Nyquist is
    /// a half and the tangent is infinite there; nothing this module asks for
    /// comes near it, and the clamp is what keeps that true when the rate is a
    /// picture's rather than a sample's.
    /// </summary>
    private const float Ceiling = 0.499f;

    /// <summary>
    /// Feedback round the whole chain, held below one. The chain has unity gain
    /// at every frequency, so anything at or above one would circulate for ever
    /// rather than decaying — and unlike a bad number in a register, that does
    /// not go away when the knob comes back down.
    /// </summary>
    private const float Most = 0.9f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Phaser", ModuleCategories.TimeEffects,
        [
            Sweep.Input,
            Sweep.Rate(0.4f, 5f),
            Sweep.Depth(0.7f),
            new PortSpec("feedback", PortKind.Scalar, 0.4f, 0f, Most),
            Sweep.Mix(0.5f),
        ],
        [new PortSpec("out"), Sweep.Motion],
        Emit,
        "Two notches swept through the sound, from four allpass stages added back to the dry "
        + "signal. Where a flanger's notches are a comb — every multiple of one frequency — "
        + "these are not related to each other, which is why it sweeps rather than whooshes. "
        + "'feedback' sharpens them. 'lfo' is the sweep, and works on the picture. Audio only "
        + "otherwise: with nothing to remember it is exactly a wire.");

    private static Slot[] Emit(Emitter em, EmitContext inputs)
    {
        var dry = inputs[0];
        var lfo = Sweep.Of(em, inputs[1]);

        var zero = em.Constant(0f);
        var one = em.Constant(1f);

        var step = em.Interval();
        var live = em.HasMemory();

        // The sweep, in octaves rather than in hertz: half a cycle of the LFO
        // covers the same musical distance at the bottom of the range as at the
        // top, which a linear sweep would not.
        var depth = em.Ternary(OpCode.Clamp, inputs[2], zero, one);
        var across = em.Add(em.Mul(em.Mul(lfo, depth), 0.5f), 0.5f);
        var corner = em.Mul(em.Binary(OpCode.Pow, em.Constant(Highest / Lowest), across), Lowest);

        var period = em.Ternary(OpCode.Clamp, em.Mul(corner, step), zero, em.Constant(Ceiling));
        var g = em.Unary(OpCode.Tan, em.Mul(period, MathF.PI));
        var gain = em.Binary(OpCode.Div, g, em.Add(g, one));

        // The loop round the whole chain, one evaluation deep. It has to be: a
        // signal cannot reach its own input within a single evaluation, which is
        // the same rule the Unit Delay module exists to make patchable by hand.
        var round = em.AllocateUnitSlot();
        var feedback = em.Ternary(OpCode.Clamp, inputs[3], em.Constant(-Most), em.Constant(Most));
        var signal = em.Add(dry, em.Mul(em.UnitRead(round), feedback));

        for (var stage = 0; stage < Stages; stage++)
        {
            var cell = em.AllocateUnitSlot();
            var held = em.UnitRead(cell);

            var through = em.Mul(em.Sub(signal, held), gain);
            var low = em.Add(through, held);

            em.UnitWrite(cell, em.Add(low, through));

            // A lowpass and its own complement, one taken from the other: the
            // level is untouched at every frequency and only the phase moves.
            signal = em.Sub(em.Mul(low, 2f), signal);
        }

        em.UnitWrite(round, signal);

        // With no memory the stages are wired straight through and the notches
        // are nowhere, so the honest thing is to stand aside entirely rather than
        // color the picture with the arithmetic that happens to be left.
        return [em.Ternary(OpCode.Mix, dry, signal, em.Mul(inputs[4], live)), lfo];
    }
}
