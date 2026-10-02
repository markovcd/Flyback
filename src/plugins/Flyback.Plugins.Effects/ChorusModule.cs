using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Effects;

/// <summary>
/// One voice heard as several. A delay short enough that the ear takes it for the same
/// sound rather than an echo, swept slowly so its pitch is never quite steady, and
/// mixed back against the dry.
/// </summary>
/// <remarks>
/// Two lines rather than one, swept in opposite directions and handed out separately,
/// which is what makes a chorus wide: the two channels are detuned away from each
/// other rather than together. The Supersaw is the same idea from the other end — that
/// one makes the copies, this one remembers them.
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
    private const float Center = 0.014f;

    private const float Swing = 0.008f;

    private const float Longest = Center + Swing;

    public static NodeDef Definition { get; } = new(
        TypeId, "Chorus", ModuleCategories.TimeEffects,
        [
            Sweep.Input,
            Sweep.Rate(0.8f, 8f),
            Sweep.Depth(0.5f),
            Sweep.Mix(0.5f),
        ],
        [
            new PortSpec("out") { Help = "One side, or the whole of it in mono." },
            new PortSpec("wide") { Help = "Swept opposite to 'out', for the other side." },
            Sweep.Motion,
        ],
        Emit,
        "One voice heard as several: a short delay swept slowly under the dry signal. 'out' "
        + "and 'wide' are a stereo pair. Audio only but for 'lfo': on the picture it is a wire.")
    {
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.TimeEffects))
        {
            Glyph = "M2,9 C4,6 6,6 8,9 C10,12 12,12 14,9 C16,6 18,6 20,9 "
                + "M2,15 C4,12 6,12 8,15 C10,18 12,18 14,15 C16,12 18,12 20,15",
        },
    };

    private static Slot[] Emit(Emitter em, EmitContext inputs)
    {
        var dry = inputs[0];
        var lfo = Sweep.Of(em, inputs[1]);

        var swing = em.Mul(em.Ternary(OpCode.Clamp, inputs[2], em.Constant(0f), em.Constant(1f)), Swing);
        var offset = em.Mul(lfo, swing);

        var near = em.Add(em.Constant(Center), offset);
        var far = em.Sub(em.Constant(Center), offset);

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
