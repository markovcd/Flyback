using Flyback.Core.Compile;

namespace Flyback.Plugins.Easy;

/// <summary>
/// The Easy Synth's envelope: the engine's ADSR, four straight lines and a latch
/// in two cells.
/// </summary>
/// <remarks>
/// The latch says whether the peak has been reached since the gate opened, so
/// the attack and the decay cannot chatter. Where there is nothing to remember,
/// on the screen, it is the gate.
/// </remarks>
internal static class Envelope
{
    private const float GateOpen = 0.5f;

    private const float ShortestStage = 1e-4f;

    /// <summary>The longest a stage may be asked to take, as a power of ten: a wire past it would overflow.</summary>
    private const float LongestStage = 2f;

    public static Slot Emit(Emitter em, Slot gate, Slot attack, Slot decay, Slot sustain, Slot release)
    {
        var step = em.Interval();
        var live = em.HasMemory();

        var zero = em.Constant(0f);
        var one = em.Constant(1f);

        var open = em.Binary(OpCode.Step, em.Constant(GateOpen), gate);

        var levelCell = em.AllocateUnitSlot();
        var peakCell = em.AllocateUnitSlot();

        var level = em.UnitRead(levelCell);

        var peaked = em.Mul(open, em.Binary(OpCode.Max, em.UnitRead(peakCell), em.Binary(OpCode.Step, one, level)));
        var rising = em.Mul(open, em.Sub(one, peaked));

        var toPeak = em.Binary(OpCode.Div, step, Duration(attack));
        var toSustain = em.Binary(OpCode.Div, em.Mul(em.Sub(one, sustain), step), Duration(decay));
        var toSilence = em.Binary(OpCode.Div, step, Duration(release));

        var attacking = em.Binary(OpCode.Min, em.Add(level, toPeak), one);
        var decaying = em.Binary(OpCode.Max, em.Sub(level, toSustain), sustain);
        var releasing = em.Binary(OpCode.Max, em.Sub(level, toSilence), zero);

        var next = em.Ternary(OpCode.Mix, releasing, em.Ternary(OpCode.Mix, decaying, attacking, rising), open);

        em.UnitWrite(levelCell, next);
        em.UnitWrite(peakCell, peaked);

        return em.Ternary(OpCode.Mix, em.Ternary(OpCode.Clamp, gate, zero, one), next, live);

        Slot Duration(Slot decades) => em.Binary(
            OpCode.Max,
            em.Binary(OpCode.Pow, em.Constant(10f), em.Binary(OpCode.Min, decades, em.Constant(LongestStage))),
            em.Constant(ShortestStage));
    }
}
