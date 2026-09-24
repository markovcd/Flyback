using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    public const string DelayTypeId = "audio.delay";

    /// <summary>
    /// The longest delay the line will hold, and therefore what the buffer costs:
    /// two seconds at 192 kHz — the audio path's oversampled rate — is about
    /// 1.5 MB. Fixed at compile time, because a buffer cannot be resized from the
    /// audio thread even though the delay itself is a signal and may be swept.
    /// </summary>
    private const float Longest = 2f;

    /// <summary>
    /// An echo: the signal heard again a moment later, fed back on itself so it
    /// repeats and fades.
    /// </summary>
    private static NodeDef Delay() => new(
        DelayTypeId, "Delay", ModuleCategories.TimeEffects,
        [
            new PortSpec("in", PortKind.Scalar, 0f, -1f, 1f),
            new PortSpec("time", PortKind.Scalar, 0.25f, 0.001f, Longest)
            {
                Knee = 0.001f,
                Help = "Seconds. Can be swept: the line interpolates, so it glides rather than steps.",
            },
            new PortSpec("feedback", PortKind.Scalar, 0.45f, 0f, 0.95f) { Help = "How much comes back round for the next repeat." },
            new PortSpec("mix", PortKind.Scalar, 0.4f, 0f, 1f) { Help = "A crossfade: 0 is a wire, 1 only the echoes." },
        ],
        [new PortSpec("out")],
        (em, inputs) => [DelayEchoed(em, inputs[0], inputs[1], inputs[2], inputs[3])],
        "An echo. Audio only: with no picture to remember, it passes straight through.");

    /// <summary>One Delay's worth of ops, for a module with a Delay inside it — see the Effects plugin's Echo.</summary>
    public static Slot DelayEchoed(Emitter em, Slot dry, Slot time, Slot feedback, Slot mix)
    {
        var echo = em.DelayLine(OpCode.Delay, dry, feedback, time, Longest);

        // Mix is a straight crossfade, so at 0 the module is exactly a wire and
        // at 1 the dry signal is gone entirely.
        return em.Ternary(OpCode.Mix, dry, echo, mix);
    }
}
