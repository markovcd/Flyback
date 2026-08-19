using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Modulation;

/// <summary>
/// The same delay as a chorus, an order of magnitude shorter and fed back on
/// itself. Down at a few milliseconds a copy no longer thickens a sound — it
/// cancels parts of it, at every frequency whose period the delay is half of.
/// Sweeping the delay drags that whole comb of notches up and down the spectrum,
/// which is the jet-engine sound and is what the module is for.
/// </summary>
/// <remarks>
/// Feedback is what sharpens the notches from dips into slots, and it is signed
/// here rather than positive. Negative feedback inverts the comb — the notches
/// land where the peaks were — and the two sound different enough that offering
/// only one of them would be leaving half the module out.
/// </remarks>
internal static class FlangerModule
{
    public const string TypeId = "flyback.mod.flanger";

    /// <summary>
    /// Short, and deliberately never reaching zero: the shortest delay a line can
    /// express is one evaluation (ADR-0027), so through-zero flanging is not
    /// something this can do however the numbers are arranged.
    /// </summary>
    private const float Centre = 0.0027f;

    private const float Swing = 0.0024f;

    private const float Longest = Centre + Swing;

    public static NodeDef Definition { get; } = new(
        TypeId, "Flanger", "Modulation",
        [
            Sweep.Input,
            Sweep.Rate(0.3f, 5f),
            Sweep.Depth(0.8f),
            new PortSpec("feedback", PortKind.Scalar, 0.5f, -0.95f, 0.95f),
            Sweep.Mix(0.5f),
        ],
        [new PortSpec("out"), Sweep.Motion],
        Emit,
        "A chorus an order of magnitude shorter, where the copy cancels the original instead "
        + "of thickening it. Sweeping drags a comb of notches through the sound. 'feedback' "
        + "sharpens them, and going negative moves them to where the peaks were — the two "
        + "signs are two different effects. 'lfo' is the sweep, and works on the picture. "
        + "Audio only otherwise: with nothing to remember it is a wire.");

    private static Slot[] Emit(Emitter em, EmitContext inputs)
    {
        var dry = inputs[0];
        var lfo = Sweep.Of(em, inputs[1]);

        var swing = em.Mul(em.Ternary(OpCode.Clamp, inputs[2], em.Constant(0f), em.Constant(1f)), Swing);
        var time = em.Add(em.Constant(Centre), em.Mul(lfo, swing));

        var wet = em.DelayLine(OpCode.Delay, dry, inputs[3], time, Longest);

        return [em.Ternary(OpCode.Mix, dry, wet, inputs[4]), lfo];
    }
}
