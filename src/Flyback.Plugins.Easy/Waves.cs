using Flyback.Core.Compile;

namespace Flyback.Plugins.Easy;

/// <summary>The shapes the Easy Synth's oscillator can take, each swinging -1 to 1 with no offset.</summary>
internal static class Waves
{
    public const string Sine = "sine";
    public const string Triangle = "triangle";
    public const string Saw = "saw";
    public const string Square = "square";
    public const string Pulse = "pulse";
    public const string Supersaw = "supersaw";
    public const string Organ = "organ";

    private const float Tau = 6.283185307179586f;

    /// <summary>How much of each cycle a pulse is high.</summary>
    private const float PulseWidth = 0.25f;

    /// <summary>Where the supersaw's five saws sit, as a fraction of the pitch, and where each starts.</summary>
    private static readonly float[] Spread = [-0.018f, -0.007f, 0f, 0.008f, 0.017f];

    private static readonly float[] Starts = [0.13f, 0.61f, 0f, 0.37f, 0.84f];

    /// <summary>The supersaw's weights in each ear: the same voices, loud in one and quiet in the other.</summary>
    private static readonly float[] LeftWeights = [1f, 0.5f, 1f, 1f, 0.5f];

    private static readonly float[] RightWeights = [0.5f, 1f, 1f, 0.5f, 1f];

    /// <summary>The organ's first four harmonics.</summary>
    private static readonly float[] Drawbars = [1f, 0.5f, 0.33f, 0.25f];

    /// <summary>The wave at <paramref name="hz"/>, and whether its two ears differ.</summary>
    public static (Slot Left, Slot Right, bool Stereo) Emit(Emitter em, Slot domain, Slot hz, string wave)
    {
        if (wave == Supersaw)
        {
            var voices = new Slot[Spread.Length];
            for (var v = 0; v < voices.Length; v++)
            {
                var pitch = Spread[v] == 0f ? hz : em.Mul(hz, 1f + Spread[v]);
                voices[v] = SawOf(em, em.Phase(domain, pitch, em.Constant(Starts[v])));
            }

            return (Weighted(em, voices, LeftWeights), Weighted(em, voices, RightWeights), true);
        }

        var phase = em.Phase(domain, hz, em.Constant(0f));

        var mono = wave switch
        {
            Sine => SineOf(em, phase),
            Triangle => em.Add(em.Mul(em.Unary(OpCode.Abs, em.Add(em.Unary(OpCode.Fract, phase), -0.5f)), 4f), -1f),
            Square => SquareOf(em, phase),
            Pulse => PulseOf(em, phase),
            Organ => OrganOf(em, phase),
            _ => SawOf(em, phase),
        };

        return (mono, mono, false);
    }

    /// <summary>A square on <paramref name="phase"/>: low for the first half of each cycle, high for the second.</summary>
    public static Slot SquareOf(Emitter em, Slot phase) =>
        em.Add(em.Mul(em.Binary(OpCode.Step, em.Constant(0.5f), em.Unary(OpCode.Fract, phase)), 2f), -1f);

    private static Slot SineOf(Emitter em, Slot phase) => em.Unary(OpCode.Sin, em.Mul(phase, Tau));

    private static Slot SawOf(Emitter em, Slot phase) => em.Add(em.Mul(em.Unary(OpCode.Fract, phase), 2f), -1f);

    /// <summary>High for a quarter of each cycle, shifted so it averages nought and scaled so it peaks at 1.</summary>
    private static Slot PulseOf(Emitter em, Slot phase)
    {
        var high = em.Binary(OpCode.Step, em.Constant(1f - PulseWidth), em.Unary(OpCode.Fract, phase));

        return em.Mul(em.Add(high, -PulseWidth), 1f / (1f - PulseWidth));
    }

    private static Slot OrganOf(Emitter em, Slot phase)
    {
        var cycle = em.Unary(OpCode.Fract, phase);
        var sum = em.Constant(0f);
        var total = 0f;

        for (var h = 0; h < Drawbars.Length; h++)
        {
            sum = em.Add(sum, em.Mul(em.Unary(OpCode.Sin, em.Mul(cycle, Tau * (h + 1))), Drawbars[h]));
            total += Drawbars[h];
        }

        return em.Mul(sum, 1f / total);
    }

    private static Slot Weighted(Emitter em, Slot[] voices, float[] weights)
    {
        var sum = em.Constant(0f);
        var total = 0f;

        for (var v = 0; v < voices.Length; v++)
        {
            sum = em.Add(sum, em.Mul(voices[v], weights[v]));
            total += weights[v];
        }

        return em.Mul(sum, 1f / total);
    }
}
