using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// A color taken apart into hue, saturation and value — the HSV module run
/// backwards.
/// </summary>
/// <remarks>
/// The catalogue could build a color out of a hue and could never read one back
/// out, which is an asymmetry rather than an omission: every other conversion in
/// the machine goes both ways, and Split is RGB's own inverse. What the missing
/// half costs is anything that depends on the color a patch already has —
/// rotating a hue, keying on one, holding a saturation while everything else
/// moves, or feeding a Feedback loop's own color back into where it goes next.
/// All of those are this module and then the one that already existed.
/// <para>
/// It is written without a branch, because the register machine has none. Which
/// of the three channels is the largest decides which of three expressions the
/// hue comes from, and that choice is made by multiplying each expression by
/// whether it won: <see cref="OpCode.Step"/> against the maximum gives a one for
/// the channel that reached it, and the ties are broken by taking red first, then
/// green — which is the same order the textbook conditional would have taken them
/// in.
/// </para>
/// <para>
/// The two divisions are by the chroma and by the value, and both are nought for
/// a grey. Neither is guarded here because <see cref="OpCode.Div"/> is guarded
/// everywhere: a division by nothing is nothing, so a grey comes back with no
/// saturation and a hue of nought, which is what a grey means. That is the
/// engine's own arithmetic doing the work a special case would otherwise do
/// ([0013](0013-guard-arithmetic-instead-of-propagating-nan.md)).
/// </para>
/// </remarks>
internal static class HsvModule
{
    public const string TypeId = "flyback.picture.hsv";

    /// <summary>Sixths of the wheel, which is how the hue falls out before it is normalised.</summary>
    private const float Sectors = 6f;

    public static NodeDef Definition { get; } = new(
        TypeId, "To HSV", ModuleCategories.Color,
        [new PortSpec("color", PortKind.Color)],
        [
            new PortSpec("hue", PortKind.Scalar, 0f, 0f, 1f),
            new PortSpec("saturation", PortKind.Scalar, 0f, 0f, 1f),
            new PortSpec("value", PortKind.Scalar, 0f, 0f, 1f),
        ],
        Emit,
        "Pulls a color apart into hue, saturation and value — the HSV module backwards, and "
        + "the half of it the catalogue was missing. It is what anything depending on the "
        + "color a patch already has needs: rotate a hue by adding to this and building the "
        + "color again, key on one by thresholding it, or take the saturation out of a "
        + "picture without touching what color it was. All three come out 0 to 1. A grey has "
        + "no hue to report and says nought, which is red — threshold the saturation if that "
        + "matters.");

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var one = em.Constant(1f);

        var color = node[0];
        var red = Slot.Scalar(color.Base);
        var green = Slot.Scalar(color.Base + 1);
        var blue = Slot.Scalar(color.Base + 2);

        var value = em.Binary(OpCode.Max, red, em.Binary(OpCode.Max, green, blue));
        var least = em.Binary(OpCode.Min, red, em.Binary(OpCode.Min, green, blue));

        var chroma = em.Sub(value, least);

        // Which channel reached the maximum, as a one and two noughts. Red is
        // taken first and green second, so a grey — where all three reach it —
        // answers with red's expression, which is nought over nought and
        // therefore nought.
        var isRed = em.Binary(OpCode.Step, value, red);
        var isGreen = em.Mul(em.Binary(OpCode.Step, value, green), em.Sub(one, isRed));
        var isBlue = em.Sub(one, em.Add(isRed, isGreen));

        // The three sixths-of-a-wheel expressions, one of which survives the
        // multiply above. Each is the difference between the other two channels
        // over the chroma, offset by where that channel's sector begins.
        var fromRed = em.Binary(OpCode.Div, em.Sub(green, blue), chroma);
        var fromGreen = em.Add(em.Binary(OpCode.Div, em.Sub(blue, red), chroma), 2f);
        var fromBlue = em.Add(em.Binary(OpCode.Div, em.Sub(red, green), chroma), 4f);

        var sectors = em.Add(
            em.Add(em.Mul(isRed, fromRed), em.Mul(isGreen, fromGreen)),
            em.Mul(isBlue, fromBlue));

        // Fract rather than a wrap by hand: red's expression is negative below
        // the axis, and both backends already agree that the fraction of a
        // negative number is the positive one.
        var hue = em.Unary(OpCode.Fract, em.Mul(sectors, 1f / Sectors));

        return [hue, em.Binary(OpCode.Div, chroma, value), value];
    }
}
