using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    public const string StringTypeId = "osc.string";

    /// <summary>The lowest pitch, which sizes the delay line.</summary>
    private const float LowestString = 20f;

    /// <summary>ln(1000): a ring has decayed by 60 dB after its 'decay' time.</summary>
    private const float Sixty = 6.9078f;

    /// <summary>White noise's lattice points per second (2²²), well above the oversampled audio rate.</summary>
    private const float StringGrain = 4_194_304f;

    private static IEnumerable<NodeDef> Strings()
    {
        yield return new NodeDef(
            StringTypeId, "String", ModuleCategories.Oscillators,
            [
                Num("in", 0f, -1f, 1f),
                Num("trigger", 0f, 0f, 1f),
                Num("freq", 220f, LowestString, 2_000f) with { Knee = LowestString },
                Seconds("decay", 0.3f),
                Num("brightness", 0.5f, 0f, 1f),
            ],
            [Num("out", 0f, -1f, 1f)],
            EmitString,
            "A plucked string. Each rise of 'trigger' plucks it with a burst of noise one "
            + "period long, and it rings at 'freq' in hertz for about 'decay' before it is gone. "
            + "'brightness' at 0 is a soft, dark pluck that loses its top quickly; at 1 it stays "
            + "bright and metallic. 'in' excites it continuously, so noise into it bows the string "
            + "and a drum into it sets it ringing in sympathy. Audio only: on the picture 'in' "
            + "passes through.")
        {
            Sinks = ModuleSinks.Audio,
        };
    }

    /// <summary>
    /// Karplus–Strong: a delay line one period long, fed back through a two-tap average.
    /// </summary>
    /// <remarks>
    /// The filter's taps are the line's last two outputs, kept in cells rather than fed
    /// back by the Delay op, whose feedback is clamped below what a long ring needs. The
    /// read and the two cells add 2 + s evaluations to the loop, so the line is that much
    /// shorter than a period.
    /// </remarks>
    private static Slot[] EmitString(Emitter em, EmitContext node)
    {
        var zero = em.Constant(0f);
        var one = em.Constant(1f);

        var step = em.Interval();
        var live = em.HasMemory();

        var freq = em.Binary(OpCode.Max, node[2], em.Constant(LowestString));
        var period = em.Binary(OpCode.Div, one, freq);

        // The loop gain that makes the fundamental fall 60 dB in 'decay'.
        var decay = em.Binary(OpCode.Max, em.Binary(OpCode.Pow, em.Constant(10f), node[3]), em.Constant(1e-3f));
        var gain = em.Binary(
            OpCode.Min,
            em.Unary(OpCode.Exp, em.Binary(OpCode.Div, em.Constant(-Sixty), em.Mul(decay, freq))),
            em.Constant(0.9999f));

        // s is the older tap's share: 0.5 is the full average, 0 no damping beyond the gain.
        var older = em.Mul(em.Sub(one, em.Ternary(OpCode.Clamp, node[4], zero, one)), 0.5f);

        // The pluck: a burst of white noise for one period after the trigger rises.
        var triggerCell = em.AllocateUnitSlot();
        var remainingCell = em.AllocateUnitSlot();

        var open = em.Binary(OpCode.Step, em.Constant(0.5f), node[1]);
        var struck = em.Mul(open, em.Sub(one, em.UnitRead(triggerCell)));
        var remaining = em.Ternary(OpCode.Mix, em.UnitRead(remainingCell), period, struck);
        // Strictly more than half a step left: the first evaluation's step is nought, and
        // "at least nought" would pluck a string nobody touched.
        var bursting = em.Sub(one, em.Binary(OpCode.Step, remaining, em.Mul(step, 0.5f)));

        em.UnitWrite(triggerCell, open);
        em.UnitWrite(remainingCell, em.Binary(OpCode.Max, em.Sub(remaining, step), zero));

        var now = em.Load(OpCode.LoadT);
        var hash = em.Ternary(
            OpCode.Noise3,
            em.Unary(OpCode.Floor, now),
            em.Unary(OpCode.Floor, em.Mul(em.Unary(OpCode.Fract, now), StringGrain)),
            zero);
        var burst = em.Mul(em.Add(em.Mul(hash, 2f), -1f), bursting);

        // The loop.
        var lastCell = em.AllocateUnitSlot();
        var beforeCell = em.AllocateUnitSlot();
        var last = em.UnitRead(lastCell);
        var before = em.UnitRead(beforeCell);

        var damped = em.Add(em.Mul(last, em.Sub(one, older)), em.Mul(before, older));
        var written = em.Add(em.Add(node[0], burst), em.Mul(damped, gain));

        var length = em.Binary(OpCode.Max, em.Sub(period, em.Mul(step, em.Add(older, 2f))), zero);
        var heard = em.DelayLine(OpCode.Delay, written, zero, length, 1f / LowestString);

        em.UnitWrite(lastCell, heard);
        em.UnitWrite(beforeCell, last);

        return [em.Ternary(OpCode.Mix, node[0], written, live)];
    }
}
