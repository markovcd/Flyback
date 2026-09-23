using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins;

namespace Flyback.Plugins.Voice;

/// <summary>
/// A wavefolder: the thing that puts harmonics in, and the opposite half of what
/// the engine's own Filter does — a filter can only subtract from what an
/// oscillator produces, where this manufactures new partials out of a sine.
/// </summary>
/// <remarks>
/// A triangle wave read at the signal rather than at a phase: below full scale the
/// triangle's rising edge is a straight line of slope one, so the module is a wire
/// there, and past it the signal walks onto the next edge and comes back down. Pure,
/// and the only module in this plugin that is — the folds the ear hears as a brighter
/// tone are the bands the eye sees in a gradient, from one knob.
/// </remarks>
internal static class FoldModule
{
    public const string TypeId = "flyback.voice.fold";

    public static NodeDef Definition { get; } = new(
        TypeId, "Fold", ModuleCategories.Shaping,
        [
            new PortSpec("in", PortKind.Any, 0f, -1f, 1f),
            new PortSpec("drive", PortKind.Scalar, 1f, 0f, 8f),
            new PortSpec("bias", PortKind.Scalar, 0f, -2f, 2f),
        ],
        [new PortSpec("out", PortKind.Any)],
        Emit,
        "Folds a signal back where it runs past full scale, adding harmonics. 'drive' 1 is a "
        + "wire; turned up, a sine grows a spectrum. 'bias' shifts it first, so the folds go "
        + "asymmetric and even harmonics appear. Untyped: it folds a color too, and on the "
        + "screen turns a gradient into bands.")
    {
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Shaping))
        {
            Glyph = "M3,6 L21,6 M3,18 L21,18 M4,18 L8,6 L12,18 L16,6 L20,18",
        },
    };

    private static Slot[] Emit(Emitter em, EmitContext inputs)
    {
        var driven = em.Add(em.Mul(inputs[0], inputs[1]), inputs[2]);

        // A triangle of period 4 and amplitude 1, positioned so that its rising
        // edge passes through the origin at slope one. Everything the module does
        // is in the choice of that position: |x| <= 1 is the edge itself, and
        // beyond it the wave turns round rather than growing.
        var phase = em.Unary(OpCode.Fract, em.Add(em.Mul(driven, 0.25f), 0.75f));
        var folded = em.Unary(OpCode.Abs, em.Add(phase, -0.5f));

        return [em.Add(em.Mul(folded, 4f), -1f)];
    }
}
