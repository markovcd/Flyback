using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Color;

/// <summary>
/// A picture held to a fixed number of levels a channel, which is what turns a
/// gradient into flat bands.
/// </summary>
/// <remarks>
/// Three ops, and here because they are three ops nobody finds. Rounding a signal
/// to steps is a Multiply, a Floor and a Divide, and every one of those is in the
/// catalogue already — but a patch reaching for them has to know that a Floor is
/// what a poster is made of, and has to get the ends right, which is the part
/// that is easy to do wrong: the obvious arithmetic never reaches white, because
/// the top step begins at one and there is nothing above it to reach.
/// <para>
/// So the levels are placed on the ends rather than between them. Four levels
/// means nought, a third, two thirds and one — the darkest is black and the
/// brightest is white, and a gradient posterised and then posterised again is
/// unchanged. Two levels is the useful extreme: every channel is off or on, which
/// is the eight-color picture a very old machine would have drawn.
/// </para>
/// <para>
/// Untyped in the same sense the maths modules are — it is written against a
/// color and the ops do not care, so each channel is stepped on its own and the
/// bands of the three cross each other. That crossing is what makes a posterised
/// picture look like a poster rather than like a contour map: three sets of bands
/// at different places give far more than three colors.
/// </para>
/// </remarks>
internal static class PosteriseModule
{
    public const string TypeId = "flyback.color.posterise";

    public static NodeDef Definition { get; } = new(
        TypeId, "Posterise", "Color",
        [
            new PortSpec("color", PortKind.Color),
            new PortSpec("levels", PortKind.Scalar, 4f, 2f, 32f),
        ],
        [new PortSpec("color", PortKind.Color)],
        Emit,
        "Holds each channel to a fixed number of levels, which turns a gradient into flat "
        + "bands. The levels reach both ends, so black stays black and white stays white and "
        + "2 is every channel off or on — the eight colors a very old machine had. 'levels' is "
        + "rounded down and never goes below two. Each channel is stepped on its own, so the "
        + "three sets of bands cross and there are many more than 'levels' colors in the "
        + "result. Sweep it from an oscillator to make a picture resolve.");

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        // Steps rather than levels: four levels is three steps between them, and
        // it is the steps that the arithmetic is in terms of.
        var steps = em.Binary(
            OpCode.Max, em.Add(em.Unary(OpCode.Floor, node[1]), -1f), em.Constant(1f));

        // Rounded to the nearest level rather than dropped to the one below, so
        // the ends are levels too: a half added before a floor is a round, and
        // the whole difference between a poster that reaches white and one that
        // does not is that half.
        var stepped = em.Unary(
            OpCode.Floor, em.Add(em.Mul(node[0], steps), 0.5f));

        return [em.Binary(OpCode.Div, stepped, steps)];
    }
}
