using Flyback.Core.Compile;
using static Flyback.Core.Graph.Sockets;

namespace Flyback.Core.Graph;

/// <summary>The Counter: how many times a trigger has risen, coming round to 0 every 'length'.</summary>
internal static class CounterModule
{
    /// <summary>The type id of the Counter module.</summary>
    internal const string TypeId = "seq.counter";

    /// <summary>The longest round its knob turns to; a wire may ask for more.</summary>
    internal const float LongestKnob = 64f;

    /// <summary>
    /// What the count is divided by on its way into a cell, which is clamped to ±16,
    /// so it counts to 65536 before it sticks. A power of two, so it stays exact.
    /// </summary>
    internal const float Headroom = 4096f;

    internal static NodeDef Def() => new(
        TypeId, "Counter", ModuleCategories.Timing,
        [
            Num("trigger", 0f, 0f, 1f) with { Lenient = true, Help = "Each rise counts one: a drum's gate, a sequencer's gate, a Pulse." },
            new PortSpec("length", PortKind.Scalar, 4f, 0f, LongestKnob, Display: PortDisplay.Integer)
            {
                Help = "How many it counts before coming round to 0: 4 for the beats of a bar, 32 for a phrase. At 0 it never comes round.",
            },
            Num("reset", 0f, 0f, 1f) with { Lenient = true, Help = "Holds the count at 0 while it is open, so the next rise counts 1." },
        ],
        [
            new PortSpec("count", Display: PortDisplay.Integer)
            {
                Help = "How many rises since it last came round, from 0 to one short of 'length'.",
            },
            Num("phase", 0f, 0f, 1f) with { Help = "'count' over 'length': how far round it is, 0 to just under 1." },
            Num("wrap", 0f, 0f, 1f) with
            {
                Help = "Open while the trigger that brings it round to 0 is: every 'length'th trigger. "
                    + "Wire it into another Counter to count the rounds.",
            },
        ],
        Emit,
        "Counts the rises on 'trigger' and comes round to 0 every 'length' of them: beats in a "
        + "bar, kicks in a phrase, phrases in a song. 'wrap' passes every 'length'th trigger, so "
        + "Counters chain. Audio only: on the picture it reads 0, so carry 'count' there through a Bridge.")
    {
        Words = "count triggers, clock divider, beat counter",
        Sinks = ModuleSinks.Audio,
    };

    /// <summary>
    /// One cell holds the count, one the trigger as it was so a rise is an edge, and
    /// one whether the last rise came round, which 'wrap' opens with the trigger.
    /// </summary>
    /// <remarks>
    /// The count out on the rising evaluation is the new one, as a Sample &amp; Hold's
    /// is. Where <see cref="Emitter.HasMemory"/> says no (the picture, or the first
    /// sample) nothing rises, and a trigger already open then counts on the second.
    /// </remarks>
    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var one = em.Constant(1f);
        var live = em.HasMemory();

        var countCell = em.AllocateUnitSlot();
        var edgeCell = em.AllocateUnitSlot();
        var roundCell = em.AllocateUnitSlot();

        var up = em.Mul(em.Binary(OpCode.Step, em.Constant(EnvelopeModules.GateOpen), node[0]), live);
        var rise = em.Mul(up, em.Sub(one, em.UnitRead(edgeCell)));
        var reset = em.Binary(OpCode.Step, em.Constant(EnvelopeModules.GateOpen), node[2]);
        var kept = em.Sub(one, reset);

        var length = em.Binary(OpCode.Max, em.Unary(OpCode.Floor, node[1]), em.Constant(0f));
        var cycling = em.Binary(OpCode.Step, one, length);

        // Taken round every evaluation, not only on a rise, so a shortened length
        // brings the count inside it at once.
        var stepped = em.Add(em.Mul(em.UnitRead(countCell), Headroom), rise);
        var round = em.Binary(OpCode.Mod, stepped, em.Binary(OpCode.Max, length, one));
        var count = em.Mul(em.Ternary(OpCode.Mix, stepped, round, cycling), kept);

        var cameRound = em.Mul(cycling, em.Sub(one, em.Binary(OpCode.Step, em.Constant(0.5f), count)));
        var came = em.Mul(em.Ternary(OpCode.Mix, em.UnitRead(roundCell), cameRound, rise), kept);

        em.UnitWrite(countCell, em.Mul(count, 1f / Headroom));
        em.UnitWrite(edgeCell, up);
        em.UnitWrite(roundCell, came);

        var phase = em.Mul(em.Binary(OpCode.Div, count, em.Binary(OpCode.Max, length, one)), cycling);

        return [count, phase, em.Mul(came, up)];
    }
}
