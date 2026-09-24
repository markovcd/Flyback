using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    public const string FilterTypeId = "audio.filter";

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

    /// <summary>
    /// A resonant filter: the thing that takes harmonics away, and the gesture every
    /// subtractive synth is built around. Two integrators in the topology Zavalishin
    /// calls TPT — the same pair read at three points, which is why all three
    /// responses come out at once.
    /// </summary>
    /// <remarks>
    /// Stateful, so like <c>Delay</c> and <c>Reverb</c> it does its job for the
    /// speakers and something simpler for the screen. What is different is that
    /// nothing here needed a new opcode: the integrators are a pair of
    /// one-evaluation cells, which <see cref="Emitter.AllocateUnitSlot()"/> already
    /// hands out. Two things follow and neither is the filter's own — working out what
    /// rate it runs at, and what it means on a path with no rate at all — which
    /// <see cref="Emitter.Interval"/> and <see cref="Emitter.HasMemory"/> answer once
    /// per program. See ADR-0041 and ADR-0042.
    /// </remarks>
    private static NodeDef Filter() => new(
        FilterTypeId, "Filter", ModuleCategories.Shaping,
        [
            new PortSpec("in", PortKind.Scalar, 0f, -1f, 1f) { Help = "The sound to filter." },
            new PortSpec("cutoff", PortKind.Scalar, 800f, 20f, 12_000f)
            {
                Knee = 20f,
                Help = "The corner, in hertz. Meant to be swept by an oscillator or an envelope.",
            },
            new PortSpec("resonance", PortKind.Scalar, 0.2f, 0f, 1f) { Standard = true },
        ],
        [
            new PortSpec("low") { Help = "What is under the cutoff. On the picture, 'in' unchanged." },
            new PortSpec("band") { Help = "What is around the cutoff. Silent on the picture." },
            new PortSpec("high") { Help = "What is over the cutoff. Silent on the picture." },
        ],
        (em, i) => FilterResponses(em, i[0], i[1], i[2]),
        "A resonant filter, all three responses at once. Audio only: the picture gets 'in' "
        + "through 'low' and nothing through the other two.");

    /// <summary>
    /// Low, band and high of <paramref name="dry"/>, for a module with a filter
    /// inside it — see the Voice plugin's Hiss.
    /// </summary>
    public static Slot[] FilterResponses(Emitter em, Slot dry, Slot cutoff, Slot resonance)
    {
        // Nothing in a module is told the sample rate, and neither of these is a
        // socket: the interval is measured off the renderer's own clock and the
        // flag says whether there is a memory behind this program at all. Both
        // belong to the emitter rather than to the filter, because both answer
        // the same for everything in one program — ADR-0042.
        var step = em.Interval();
        var live = em.HasMemory();

        var zero = em.Constant(0f);
        var one = em.Constant(1f);

        // Cutoff as a fraction of the evaluation rate, which is what the
        // coefficient is actually a function of. Clamping here rather than on the
        // socket is deliberate: cutoff is a signal, and what a sweep reaches for
        // matters more than what the knob is set to.
        var period = em.Ternary(OpCode.Clamp, em.Mul(cutoff, step), zero, em.Constant(Highest));
        var g = em.Unary(OpCode.Tan, em.Mul(period, MathF.PI));

        var ringing = em.Ternary(OpCode.Clamp, resonance, zero, one);
        var k = em.Add(em.Mul(ringing, -(Damped - Ringing)), Damped);

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
