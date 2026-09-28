using Flyback.Core.Compile;

namespace Flyback.Plugins.Easy;

/// <summary>The Easy Synth's LFOs: a slow wave, -1 to 1, in one of five shapes.</summary>
internal static class Lfo
{
    public const string Sine = "sine";
    public const string Triangle = "triangle";
    public const string Square = "square";
    public const string Ramp = "ramp";
    public const string Random = "random";

    private const float Tau = 6.283185307179586f;

    /// <summary>
    /// The LFO at <paramref name="rate"/> across <paramref name="domain"/>. The
    /// <paramref name="row"/> keeps each LFO's random run apart from the other's.
    /// </summary>
    public static Slot Emit(Emitter em, Slot domain, Slot rate, string shape, float row)
    {
        var phase = em.Phase(domain, rate, em.Constant(0f));
        var cycle = em.Unary(OpCode.Fract, phase);

        return shape switch
        {
            Triangle => em.Add(em.Mul(em.Unary(OpCode.Abs, em.Add(cycle, -0.5f)), 4f), -1f),
            Square => em.Add(em.Mul(em.Binary(OpCode.Step, em.Constant(0.5f), cycle), 2f), -1f),
            Ramp => em.Add(em.Mul(cycle, 2f), -1f),

            // A new value each cycle, held until the next. Counted off the domain, since the
            // running phase wraps; a step can land early when the rate moves, which a step hides.
            Random => em.Add(
                em.Mul(
                    em.Ternary(OpCode.Noise3, em.Unary(OpCode.Floor, em.Mul(domain, rate)), em.Constant(row), em.Constant(0f)),
                    2f),
                -1f),

            _ => em.Unary(OpCode.Sin, em.Mul(cycle, Tau)),
        };
    }
}
