using Flyback.Core.Compile;

namespace Flyback.Plugins.Mastering;

/// <summary>What three of a state-variable filter's outputs are, read at once.</summary>
internal readonly record struct Svf(Slot Low, Slot Band, Slot High);

/// <summary>
/// The arithmetic every module here is built from: decibels both ways, a time
/// socket turned into a coefficient, and the one filter all the equalizing is
/// done with.
/// </summary>
internal static class Dsp
{
    /// <summary>The help on a stereo module's left input.</summary>
    public const string LeftIn = "The sound, or its left side when 'right' is patched too.";

    /// <summary>ln(10) / 20: decibels to nepers, so a level is one <c>Exp</c> away.</summary>
    public const float Nepers = 0.115129255f;

    /// <summary>
    /// The highest corner a coefficient may stand for, as a fraction of the
    /// evaluation rate — <c>tan</c> is infinite at a half. The Filter's clamp,
    /// for the Filter's reason.
    /// </summary>
    private const float Highest = 0.499f;

    /// <summary>
    /// The shortest time a time socket reaches, whatever is patched into it. A
    /// time of zero would be a division by it.
    /// </summary>
    private const float Shortest = 1e-5f;

    /// <summary>A level in decibels as a gain.</summary>
    public static Slot Linear(Emitter em, Slot decibels) =>
        em.Unary(OpCode.Exp, em.Mul(decibels, Nepers));

    /// <summary>A gain in decibels, reading anything quieter than <paramref name="floor"/> as that.</summary>
    public static Slot Decibels(Emitter em, Slot linear, float floor) =>
        em.Mul(
            em.Unary(OpCode.Log, em.Binary(OpCode.Max, linear, em.Constant(floor))),
            1f / Nepers);

    /// <summary>A Duration socket — a power of ten of seconds — in seconds.</summary>
    public static Slot Seconds(Emitter em, Slot decades) =>
        em.Binary(
            OpCode.Max,
            em.Binary(OpCode.Pow, em.Constant(10f), decades),
            em.Constant(Shortest));

    /// <summary>
    /// How much of the way a one-pole lag with time constant
    /// <paramref name="seconds"/> closes in one evaluation. <c>1 - e^(-step/tau)</c>
    /// rather than <c>step/tau</c>, so a long step closes the gap and no more.
    /// </summary>
    public static Slot Closing(Emitter em, Slot seconds) =>
        em.Sub(
            em.Constant(1f),
            em.Unary(OpCode.Exp, em.Unary(OpCode.Neg, em.Binary(OpCode.Div, em.Interval(), seconds))));

    /// <summary>
    /// <c>tan(pi f / rate)</c>, the prewarped corner a trapezoidal filter is
    /// tuned with, for a corner in hertz.
    /// </summary>
    public static Slot Warp(Emitter em, Slot hertz)
    {
        var period = em.Ternary(
            OpCode.Clamp, em.Mul(hertz, em.Interval()), em.Constant(0f), em.Constant(Highest));

        return em.Unary(OpCode.Tan, em.Mul(period, MathF.PI));
    }

    /// <summary>
    /// A two-integrator state-variable filter, the TPT topology the Filter uses,
    /// read at all three points.
    /// </summary>
    /// <remarks>
    /// Every shelf, bell and crossover here is these three outputs mixed, which is
    /// Andrew Simper's formulation: the same bilinear transform as a textbook
    /// biquad, and well conditioned at a corner a thousandth of the evaluation
    /// rate, where a biquad's coefficients lose most of their digits. At
    /// <paramref name="g"/> zero both integrators hold still and
    /// <see cref="Svf.High"/> is exactly the input.
    /// </remarks>
    /// <param name="g">The prewarped corner, from <see cref="Warp"/>.</param>
    /// <param name="k">Damping, one over Q.</param>
    public static Svf Filter(Emitter em, Slot dry, Slot g, Slot k)
    {
        var one = em.Constant(1f);

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

        return new Svf(v2, v1, em.Sub(em.Sub(dry, em.Mul(k, v1)), v2));
    }

    /// <summary>
    /// <paramref name="whenZero"/> where <paramref name="choice"/> is zero and
    /// <paramref name="whenOne"/> where it is one — a branch, for a choice that is
    /// only ever one of the two.
    /// </summary>
    public static Slot Pick(Emitter em, Slot whenZero, Slot whenOne, Slot choice) =>
        em.Ternary(OpCode.Mix, whenZero, whenOne, choice);

    /// <summary>One where <paramref name="x"/> is at least <paramref name="edge"/>, zero below it.</summary>
    public static Slot AtLeast(Emitter em, Slot x, Slot edge) => em.Binary(OpCode.Step, edge, x);
}
