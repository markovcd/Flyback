using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// The three adjustments every picture wants after it is drawn: how colorful,
/// how contrasty, how dark in the middle.
/// </summary>
/// <remarks>
/// The catalogue's Gain is a multiply and an add, which is brightness and a kind
/// of contrast, and it is the only thing here that could be done to a finished
/// picture. What it cannot do is anything that treats the three channels as a
/// color rather than as three signals: taking the color out of one is not a
/// multiply, and neither is deepening its shadows without moving its highlights.
/// <para>
/// Saturation is a mix between the picture and its own brightness — not the
/// average of the channels but the weighted one the eye uses, since green is most
/// of what brightness means and blue is almost none of it. Past one it keeps
/// going the same way, which oversaturates rather than clipping, and at nought it
/// is a proper greyscale rather than a washed-out one.
/// </para>
/// <para>
/// Contrast is about the middle grey rather than about black, which is the whole
/// difference between contrast and gain: multiplying a picture makes it brighter
/// <em>and</em> harder, and this leans on it without moving where the middle is.
/// Gamma is the exponent, so above one deepens everything below the middle and
/// leaves white alone — the knob to reach for when a picture is nearly right and
/// too pale.
/// </para>
/// <para>
/// All three are neutral at one, and in that state this module is exactly a wire.
/// The order is fixed and is the order a grading desk uses: color, then
/// contrast, then gamma. Three of these in a row is the same as one with the
/// numbers multiplied out, which is why there is only one.
/// </para>
/// </remarks>
internal static class GradeModule
{
    public const string TypeId = "flyback.picture.grade";

    /// <summary>
    /// What the eye takes brightness to be, which is mostly green and hardly any
    /// blue. The same weights the engine uses where it has to make one number out
    /// of a color.
    /// </summary>
    private const float Red = 0.2126f;

    private const float Green = 0.7152f;

    private const float Blue = 0.0722f;

    /// <summary>Where contrast pivots: the middle of the range, so white and black stay put.</summary>
    private const float Middle = 0.5f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Grade", ModuleCategories.Color,
        [
            new PortSpec("color", PortKind.Color),
            new PortSpec("saturation", PortKind.Scalar, 1f, 0f, 3f),
            new PortSpec("contrast", PortKind.Scalar, 1f, 0f, 4f),
            new PortSpec("gamma", PortKind.Scalar, 1f, 0.1f, 4f),
        ],
        [new PortSpec("color", PortKind.Color)],
        Emit,
        "Color, contrast and gamma, in the order a grading desk has them, and a wire when all "
        + "three are 1. 'saturation' mixes towards the picture's own brightness — 0 is a proper "
        + "greyscale and past 1 keeps going. 'contrast' leans on the middle grey rather than on "
        + "black, which is what makes it contrast rather than gain: white and black stay where "
        + "they are. 'gamma' above 1 deepens everything under the middle and leaves the "
        + "highlights alone, which is the knob for a picture that is nearly right and too pale. "
        + "Every one of them is a socket, so a patch can grade itself as it moves.");

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var color = node[0];

        var brightness = em.Add(
            em.Add(
                em.Mul(Slot.Scalar(color.Base), Red),
                em.Mul(Slot.Scalar(color.Base + 1), Green)),
            em.Mul(Slot.Scalar(color.Base + 2), Blue));

        // One scalar against three channels, which the register machine already
        // does: a narrow slot is read at the same register for each of them
        // ([0007](0007-register-slots-with-scalar-broadcast.md)).
        var saturated = em.Ternary(OpCode.Mix, brightness, color, node[1]);

        var contrasted = em.Add(
            em.Mul(em.Add(saturated, -Middle), node[2]), em.Constant(Middle));

        // Held above nought before the power, because a negative base with a
        // fractional exponent has no answer and would come back as the black the
        // guard turns everything unrepresentable into — which is a hole in the
        // picture rather than a dark part of it.
        var floored = em.Binary(OpCode.Max, contrasted, em.Constant(0f));

        return [em.Binary(OpCode.Pow, floored, node[3])];
    }
}
