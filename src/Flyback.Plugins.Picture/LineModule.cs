using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// The distance to a line segment with round ends — a stroke from one point to another.
/// </summary>
/// <remarks>
/// Project the point onto the segment, clamp to its ends, and measure to what is left.
/// A segment of no length divides by nothing, which the guarded Div reads as nought,
/// so it becomes a dot at the first end.
/// </remarks>
internal static class LineModule
{
    public const string TypeId = "flyback.picture.line";

    public static NodeDef Definition { get; } = new(
        TypeId, "Line", ModuleCategories.Forms,
        [
            ..Field.Position(),
            Field.Distance("x1") with { Default = -0.5f },
            Field.Distance("y1") with { Default = -0.3f },
            Field.Distance("x2") with { Default = 0.5f },
            Field.Distance("y2") with { Default = 0.3f },
            Field.Size("width", 0.02f, 0.5f),
        ],
        [Field.Distance("distance"), new PortSpec("along", PortKind.Scalar, 0f, 0f, 1f)],
        Emit,
        "A straight stroke from (x1, y1) to (x2, y2), as a distance, with round ends. 'width' "
        + "is how far it reaches either side of the line, as a Box's sizes are half-sizes. "
        + "Patch it into a Fill to see it. 'along' runs from 0 at the first end to 1 at the "
        + "second, for a stroke that fades, changes color, or breaks into dashes through a "
        + "Square. Drive the ends from oscillators and it moves.");

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var zero = em.Constant(0f);

        var px = em.Sub(node[0], node[2]);
        var py = em.Sub(node[1], node[3]);
        var bx = em.Sub(node[4], node[2]);
        var by = em.Sub(node[5], node[3]);

        var dot = em.Add(em.Mul(px, bx), em.Mul(py, by));
        var length = em.Add(em.Mul(bx, bx), em.Mul(by, by));
        var along = em.Ternary(OpCode.Clamp, em.Binary(OpCode.Div, dot, length), zero, em.Constant(1f));

        var distance = em.Binary(
            OpCode.Hypot,
            em.Sub(px, em.Mul(bx, along)),
            em.Sub(py, em.Mul(by, along)));

        return [em.Sub(distance, node[6]), along];
    }
}
