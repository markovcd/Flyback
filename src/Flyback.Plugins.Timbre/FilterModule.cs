using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Timbre;

/// <summary>
/// A resonant filter: the thing that takes harmonics away, and the one gesture
/// every subtractive synth is built around. Two integrators in the topology
/// Zavalishin calls TPT — the same pair read at three points, which is why all
/// three responses come out at once rather than one at a time behind a switch.
/// </summary>
/// <remarks>
/// It is stateful, so like <c>Delay</c> and <c>Reverb</c> it does its job for the
/// speakers and something simpler for the screen. What makes it different from
/// those two is that nothing here needed a new opcode: the integrators are a pair
/// of one-evaluation cells, which <see cref="Emitter.AllocateUnitSlot"/> already
/// hands out for the cycles a patch draws by hand. A module that wants a memory
/// of exactly one sample can take one without asking the engine for anything.
/// <para>
/// Two consequences follow from that, and both are decided in <see cref="Emit"/>:
/// the filter has to work out its own sample rate, and it has to say what it
/// means on a path that has no rate at all. See ADR-0041.
/// </para>
/// </remarks>
internal static class FilterModule
{
    public const string TypeId = "flyback.timbre.filter";

    /// <summary>
    /// The highest cutoff the coefficient is allowed to stand for, as a fraction
    /// of the evaluation rate. Nyquist is a half and <c>tan</c> is infinite
    /// there, so the clamp is what keeps a swept cutoff from running off the end
    /// of its own arithmetic. At the audio path's oversampled rate this is about
    /// 96 kHz, which is far above anything a knob can ask for.
    /// </summary>
    private const float Highest = 0.499f;

    /// <summary>
    /// Damping at no resonance and at full — the <c>k</c> of the topology, which
    /// is one over Q. Two is heavily damped and the corner is a gentle bend; the
    /// low end is a peak sharp enough to ring on a step, and stays above zero
    /// because at zero the filter is an oscillator rather than a filter.
    /// </summary>
    private const float Damped = 2f;

    private const float Ringing = 0.05f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Filter", "Filter",
        [
            new PortSpec("in", PortKind.Scalar, 0f, -1f, 1f),
            new PortSpec("cutoff", PortKind.Scalar, 800f, 20f, 12_000f),
            new PortSpec("resonance", PortKind.Scalar, 0.2f, 0f, 1f),
        ],
        [new PortSpec("low"), new PortSpec("band"), new PortSpec("high")],
        Emit,
        "A resonant filter, all three responses at once. 'cutoff' is in hertz and is meant to "
        + "be swept — patch an oscillator or an envelope into it, which is the sound this "
        + "module exists for. 'resonance' peaks the corner and will ring on a sharp edge. "
        + "Audio only: a picture is one evaluation with nothing before it, so 'low' passes "
        + "straight through and the other two are silent.");

    private static Slot[] Emit(Emitter em, EmitContext inputs)
    {
        var dry = inputs[0];

        // Nothing in a module is told the sample rate — an oscillator gets its
        // timebase from how far its domain moved (ADR-0030), and this does the
        // same thing by hand. One cell holds the clock as it was last evaluation,
        // so the difference is the interval the filter is running at: 1/192000
        // on the audio path, whatever the renderer happens to be doing elsewhere.
        var now = em.Load(OpCode.LoadT);
        var clock = em.AllocateUnitSlot();
        var step = em.Sub(now, em.UnitRead(clock));
        em.UnitWrite(clock, now);

        // Whether there is any memory behind this program at all: a cell written
        // one and read back as one from the second evaluation onwards, and read
        // as zero for ever where the renderer passes no state. It is the only
        // honest way to ask the question — an emit function runs once, at compile
        // time, long before anything knows which sink is about to run it.
        var alive = em.AllocateUnitSlot();
        var live = em.UnitRead(alive);
        em.UnitWrite(alive, em.Constant(1f));

        var zero = em.Constant(0f);
        var one = em.Constant(1f);

        // Cutoff as a fraction of the evaluation rate, which is what the
        // coefficient is actually a function of. Clamping here rather than on the
        // socket is deliberate: cutoff is a signal, and what a sweep reaches for
        // matters more than what the knob is set to.
        var period = em.Ternary(OpCode.Clamp, em.Mul(inputs[1], step), zero, em.Constant(Highest));
        var g = em.Unary(OpCode.Tan, em.Mul(period, MathF.PI));

        var resonance = em.Ternary(OpCode.Clamp, inputs[2], zero, one);
        var k = em.Add(em.Mul(resonance, -(Damped - Ringing)), Damped);

        // The three coefficients the topology resolves its implicit loop with.
        // Solving that loop rather than iterating it is the whole of what TPT
        // buys: the filter is stable at any cutoff, including the ones a sweep
        // passes through on its way somewhere else.
        var a1 = em.Binary(OpCode.Div, one, em.Add(one, em.Mul(g, em.Add(g, k))));
        var a2 = em.Mul(g, a1);
        var a3 = em.Mul(g, a2);

        var first = em.AllocateUnitSlot();
        var second = em.AllocateUnitSlot();
        var ic1 = em.UnitRead(first);
        var ic2 = em.UnitRead(second);

        var v3 = em.Sub(dry, ic2);
        var v1 = em.Add(em.Mul(a1, ic1), em.Mul(a2, v3));
        var v2 = em.Add(em.Add(ic2, em.Mul(a2, ic1)), em.Mul(a3, v3));

        em.UnitWrite(first, em.Sub(em.Mul(v1, 2f), ic1));
        em.UnitWrite(second, em.Sub(em.Mul(v2, 2f), ic2));

        var high = em.Sub(em.Sub(dry, em.Mul(k, v1)), v2);

        // What the module means where there is nothing to remember. A picture is
        // one evaluation per pixel, so what the filter sees is a signal that
        // never moves — and the response of these three outputs to a signal that
        // never moves is exactly this: everything through the lowpass, nothing
        // through the other two. The picture a patch drew before the filter was
        // put in it is the picture it draws after.
        return
        [
            em.Ternary(OpCode.Mix, dry, v2, live),
            em.Mul(v1, live),
            em.Mul(high, live),
        ];
    }
}
