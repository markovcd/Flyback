using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// A color taken apart into hue, saturation and value — the HSV module run
/// backwards.
/// </summary>
/// <remarks>
/// The catalog could build a color out of a hue and never read one back, which
/// costs anything depending on the color a patch already has: rotating a hue,
/// keying on one, or feeding a Feedback loop's own color back in.
/// <para>
/// Written without a branch, because the register machine has none. Which channel
/// is largest decides which of three expressions the hue comes from, and that
/// choice is a multiply by whether it won: <see cref="OpCode.Step"/> against the
/// maximum gives a one for the channel that reached it, ties going to red then
/// green.
/// </para>
/// <para>
/// The two divisions are by the chroma and the value, both nought for a gray.
/// Neither is guarded here because <see cref="OpCode.Div"/> is guarded everywhere
/// (ADR-0013), so a gray comes back with no saturation and a hue of nought.
/// </para>
/// </remarks>
internal static class HsvModule
{
    public const string TypeId = "flyback.picture.hsv";

    /// <summary>Sixths of the wheel, which is how the hue falls out before it is normalized.</summary>
    private const float Sectors = 6f;

    public static NodeDef Definition { get; } = new(
        TypeId, "To HSV", ModuleCategories.Color,
        [new PortSpec("color", PortKind.Color) { Help = "The color to read the three from." }],
        [
            new PortSpec("hue", PortKind.Scalar, 0f, 0f, 1f)
            {
                Help = "A gray's is 0, which is red: threshold 'saturation' if that matters.",
            },
            new PortSpec("saturation", PortKind.Scalar, 0f, 0f, 1f) { Help = "How much color: 0 is gray." },
            new PortSpec("value", PortKind.Scalar, 0f, 0f, 1f) { Help = "Brightness: 0 is black." },
        ],
        Emit,
        "A color pulled apart into hue, saturation and value, 0 to 1 each: HSV backwards. "
        + "Rotate a hue by adding to it and rebuilding, key on one by thresholding, or "
        + "desaturate without changing the color.");

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
        // taken first and green second, so a gray — where all three reach it —
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
