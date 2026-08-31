using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Effects;

/// <summary>
/// One voice heard as several. A delay short enough that the ear takes it for
/// the same sound rather than an echo, swept slowly so its pitch is never quite
/// steady, and mixed back against the dry — which is what a room full of players
/// does to a note none of them can hold perfectly still.
/// </summary>
/// <remarks>
/// Two lines rather than one, swept in opposite directions and handed out
/// separately. That is the whole of what makes a chorus wide: the two channels
/// are detuned away from each other rather than together, so the image opens
/// out instead of wobbling in place. Patch both, or take <c>out</c> alone and
/// have the mono version for nothing.
/// <para>
/// The Supersaw is the same idea reached from the other end — voices detuned
/// against each other, one of them the original — and the difference is where
/// the copies come from. That one makes them; this one remembers them.
/// </para>
/// </remarks>
internal static class ChorusModule
{
    public const string TypeId = "flyback.effects.chorus";

    /// <summary>
    /// Where the delay sits when the sweep is at the middle, and how far either
    /// side of it a full depth reaches. Short enough throughout that what comes
    /// back is heard as thickening rather than as a repeat, and never near zero,
    /// where two nearly-aligned copies would comb rather than chorus.
    /// </summary>
    private const float Centre = 0.014f;

    private const float Swing = 0.008f;

    private const float Longest = Centre + Swing;

    public static NodeDef Definition { get; } = new(
        TypeId, "Chorus", ModuleCategories.TimeEffects,
        [
            Sweep.Input,
            Sweep.Rate(0.8f, 8f),
            Sweep.Depth(0.5f),
            Sweep.Mix(0.5f),
        ],
        [new PortSpec("out"), new PortSpec("wide"), Sweep.Motion],
        Emit,
        "One voice heard as several: a short delay swept slowly under the dry signal, so the "
        + "copy is never quite in tune with the original. 'out' and 'wide' are swept in "
        + "opposite directions — patch both for stereo, or use 'out' alone. 'lfo' is the "
        + "sweep itself, and is the one output that works on the picture. Audio only "
        + "otherwise: with nothing to remember it is a wire.");

    private static Slot[] Emit(Emitter em, EmitContext inputs)
    {
        var dry = inputs[0];
        var lfo = Sweep.Of(em, inputs[1]);

        var swing = em.Mul(em.Ternary(OpCode.Clamp, inputs[2], em.Constant(0f), em.Constant(1f)), Swing);
        var offset = em.Mul(lfo, swing);

        var near = em.Add(em.Constant(Centre), offset);
        var far = em.Sub(em.Constant(Centre), offset);

        // No feedback on either line. A chorus with feedback in it is a flanger,
        // and there is one of those next door.
        var silent = em.Constant(0f);

        return
        [
            em.Ternary(OpCode.Mix, dry, em.DelayLine(OpCode.Delay, dry, silent, near, Longest), inputs[3]),
            em.Ternary(OpCode.Mix, dry, em.DelayLine(OpCode.Delay, dry, silent, far, Longest), inputs[3]),
            lfo,
        ];
    }
}
