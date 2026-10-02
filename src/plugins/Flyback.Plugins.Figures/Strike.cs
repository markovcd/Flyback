using Flyback.Core.Compile;

namespace Flyback.Plugins.Figures;

/// <summary>How long ago something was struck, and how hard.</summary>
/// <param name="Age">Seconds since the strike, nought on the evaluation it lands.</param>
/// <param name="Level">The velocity latched at the strike, and nought before there was one.</param>
internal readonly record struct Struck(Slot Age, Slot Level);

/// <summary>
/// A strike remembered in planes, so a module can ring out on the speakers and
/// on the screen from the same age.
/// </summary>
/// <remarks>
/// A plane is written clamped to ±16 like any cell, so the clock cannot be
/// stored as it is. What is stored is the clock modulo sixteen, and the age is
/// the floored difference, which lands in [0, 16) whichever side of the wrap the
/// two fell. Past fifteen seconds the held level is dropped to nought, so a
/// wrapped age is silence rather than a second strike, and a plane that has
/// never been written reads as never struck.
/// </remarks>
internal static class Strike
{
    /// <summary>The clock repeats here, and a ring is over by then.</summary>
    public const float Wrap = 16f;

    /// <summary>Where the held level is let go, a second before the clock wraps.</summary>
    private const float Over = Wrap - 1f;

    /// <summary>The clock as a plane can hold it.</summary>
    public static Slot Now(Emitter em) =>
        em.Binary(OpCode.Mod, em.Load(OpCode.LoadT), em.Constant(Wrap));

    /// <summary>
    /// Claims three planes and keeps the strike in them: the wrapped clock it
    /// landed on, the trigger as it was, and the level it landed with.
    /// </summary>
    public static Struck Of(Emitter em, Slot trigger, Slot velocity)
    {
        var struck = em.AllocatePlaneSlot();
        var before = em.AllocatePlaneSlot();
        var held = em.AllocatePlaneSlot();

        var half = em.Constant(0.5f);
        var one = em.Constant(1f);
        var nought = em.Constant(0f);

        var now = Now(em);
        var was = em.PlaneRead(struck);
        var previous = em.PlaneRead(before);
        var level = em.PlaneRead(held);

        // A rise only where there was nothing to rise from.
        var high = em.Binary(OpCode.Step, half, trigger);
        var edge = em.Mul(high, em.Sub(one, em.Binary(OpCode.Step, half, previous)));

        var age = em.Binary(OpCode.Mod, em.Sub(now, was), em.Constant(Wrap));

        var kept = em.Mul(level, em.Binary(OpCode.Step, age, em.Constant(Over)));
        var landed = em.Ternary(OpCode.Mix, kept, velocity, edge);

        em.PlaneWrite(struck, em.Ternary(OpCode.Mix, was, now, edge));
        em.PlaneWrite(before, high);
        em.PlaneWrite(held, landed);

        return new Struck(em.Ternary(OpCode.Mix, age, nought, edge), landed);
    }
}
