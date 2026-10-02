using Flyback.Core.Compile;

namespace Flyback.Plugins.Easy;

/// <summary>The last stage every Easy module ends in: drive, pan, and a clamp to full scale.</summary>
internal static class Finish
{
    /// <summary>How hard a full 'drive' pushes into the curve.</summary>
    private const float HardestDrive = 9f;

    /// <summary>
    /// The Drive module's curve, divided back out to full scale and faded in with
    /// <paramref name="drive"/>, so 0 is clean and 1 is never louder.
    /// </summary>
    public static Slot Driven(Emitter em, Slot dry, Slot drive)
    {
        var push = em.Add(em.Mul(drive, HardestDrive), 1f);
        var pushed = em.Mul(dry, push);
        var curve = em.Binary(OpCode.Div, pushed, em.Add(em.Unary(OpCode.Abs, pushed), 1f));
        var ceiling = em.Binary(OpCode.Div, push, em.Add(push, 1f));

        return em.Ternary(OpCode.Mix, dry, em.Binary(OpCode.Div, curve, ceiling), drive);
    }

    /// <summary>Both sides panned by <paramref name="pan"/>, -1 to 1, and each held to -1..1.</summary>
    public static Slot[] Panned(Emitter em, Slot left, Slot right, Slot pan)
    {
        var one = em.Constant(1f);
        var side = em.Ternary(OpCode.Clamp, pan, em.Constant(-1f), one);

        return
        [
            Rail(em, em.Mul(left, em.Binary(OpCode.Min, em.Sub(one, side), one))),
            Rail(em, em.Mul(right, em.Binary(OpCode.Min, em.Add(side, 1f), one))),
        ];
    }

    private static Slot Rail(Emitter em, Slot signal) =>
        em.Ternary(OpCode.Clamp, signal, em.Constant(-1f), em.Constant(1f));
}
