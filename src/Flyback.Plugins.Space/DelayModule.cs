using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Space;

/// <summary>
/// An echo: the signal heard again a moment later, fed back on itself so it
/// repeats and fades.
/// </summary>
internal static class DelayModule
{
    public const string TypeId = "flyback.space.delay";

    /// <summary>
    /// The longest delay the line will hold, and therefore what the buffer costs:
    /// two seconds at 192 kHz — the audio path's oversampled rate — is about
    /// 1.5 MB. Fixed at compile time, because a buffer cannot be resized from the
    /// audio thread even though the delay itself is a signal and may be swept.
    /// </summary>
    private const float Longest = 2f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Delay", "Space",
        [
            new PortSpec("in", PortKind.Scalar, 0f, -1f, 1f),
            new PortSpec("time", PortKind.Scalar, 0.25f, 0.001f, Longest),
            new PortSpec("feedback", PortKind.Scalar, 0.45f, 0f, 0.95f),
            new PortSpec("mix", PortKind.Scalar, 0.4f, 0f, 1f),
        ],
        [new PortSpec("out")],
        Emit,
        "An echo. 'time' is in seconds and can be swept — the line interpolates, so it glides "
        + "rather than steps. 'feedback' is how much comes back round for the next repeat. "
        + "Audio only: with no picture to remember, it passes straight through.");

    private static Slot[] Emit(Emitter em, Slot[] inputs)
    {
        var dry = inputs[0];

        var echo = em.DelayLine(OpCode.Delay, dry, inputs[2], inputs[1], Longest);

        // Mix is a straight crossfade, so at 0 the module is exactly a wire and
        // at 1 the dry signal is gone entirely.
        return [em.Ternary(OpCode.Mix, dry, echo, inputs[3])];
    }
}
