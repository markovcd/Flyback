using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// A picture held to a fixed number of levels a channel, which is what turns a
/// gradient into flat bands.
/// </summary>
/// <remarks>
/// Three ops, and here because they are three ops nobody finds: a patch reaching
/// for a Multiply, a Floor and a Divide has to know a Floor is what a poster is
/// made of, and has to get the ends right — the obvious arithmetic never reaches
/// white, because the top step begins at one.
/// <para>
/// So the levels are placed on the ends: four means nought, a third, two thirds
/// and one, and a gradient posterised twice is unchanged. Untyped like the maths
/// modules, so each channel is stepped on its own and the bands of the three cross
/// — which is what makes a posterised picture look like a poster rather than a
/// contour map.
/// </para>
/// </remarks>
internal static class PosteriseModule
{
    public const string TypeId = "flyback.picture.posterise";

    public static NodeDef Definition { get; } = new(
        TypeId, "Posterise", ModuleCategories.Color,
        [
            new PortSpec("color", PortKind.Color),
            new PortSpec("levels", PortKind.Scalar, 4f, 2f, 32f, Display: PortDisplay.Integer)
            {
                Help = "Steps for each channel, black and white included, so 2 is the eight colors "
                    + "of a very old machine. Rounded down, never below two.",
            },
        ],
        [new PortSpec("color", PortKind.Color)],
        Emit,
        "Holds each channel to a few steps, turning a gradient into flat bands. The channels "
        + "step apart, so the result has more than 'levels' colors. Sweep 'levels' from an "
        + "oscillator to make a picture resolve.");

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
