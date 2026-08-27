using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Shapes;

/// <summary>
/// A star of any number of points, sharp or blunt.
/// </summary>
/// <remarks>
/// The one shape here that is not obvious, and the construction is Iñigo
/// Quílez's. The angle is folded into a single wedge the way
/// <see cref="PolygonModule"/> folds it, and then folded again about the wedge's
/// own axis — that second fold is the <c>abs</c>, and it is what makes a star out
/// of a polygon, since the two sides of a point are mirror images. What is left is
/// one point of the star with its tip on the y axis, and the distance to it is the
/// distance to a single line segment: project onto the edge, hold the projection
/// inside the segment's own length, and measure what is left over. The sign comes
/// from which side of the wedge's axis the nearest point landed on.
/// <para>
/// Exact, unlike the polygon beside it, and that is the difference between
/// measuring to a plane and measuring to a segment with ends on it.
/// </para>
/// <para>
/// 'sharpness' is a knob over the shape's real parameter rather than the
/// parameter itself. What the construction wants is a second count — how many
/// points the <em>edges</em> would make if they were extended, which is somewhere
/// between two and the number of points there are. Two is the polygon with its
/// corners on the tips and the number of points is a needle, and neither of those
/// is a number anybody would think to turn a knob to. So the socket runs 0 to 1
/// across that span and the module works the count out, which also keeps it
/// meaningful when 'points' is a signal that moves under it.
/// </para>
/// </remarks>
internal static class StarModule
{
    public const string TypeId = "flyback.shapes.star";

    public static NodeDef Definition { get; } = new(
        TypeId, "Star", ShapesPlugin.Category,
        [
            ..Field.Position(),
            Field.Size("radius", 0.5f),
            new PortSpec("points", PortKind.Scalar, 5f, 2f, 16f),
            new PortSpec("sharpness", PortKind.Scalar, 0.45f, 0f, 1f),
        ],
        [Field.Distance("distance")],
        Emit,
        "A star, as a distance, with a point at the top like a Polygon's corner. 'radius' is "
        + "to the tips. 'sharpness' runs from a polygon with its corners on those tips, at 0, "
        + "to a needle at 1 — the five-pointed star it starts on is the one on a flag, and "
        + "sweeping the knob grows the points rather than spinning the shape. 'points' is "
        + "rounded down like a Polygon's 'sides', and two of them is a lens rather than a "
        + "star. Exact, so an outline round one is the width it says even at the tips.");

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var zero = em.Constant(0f);
        var two = em.Constant(2f);

        var points = em.Binary(OpCode.Max, em.Unary(OpCode.Floor, node[3]), two);

        // The edge count the construction wants, between two and the point count.
        // Clamped as well as mixed, because 'points' may be a signal and the two
        // sockets can disagree for an evaluation.
        var edges = em.Ternary(
            OpCode.Clamp, em.Ternary(OpCode.Mix, two, points, node[4]), two, points);

        var half = em.Binary(OpCode.Div, em.Constant(MathF.PI), points);
        var lean = em.Binary(OpCode.Div, em.Constant(MathF.PI), edges);

        var tipX = em.Unary(OpCode.Cos, half);
        var tipY = em.Unary(OpCode.Sin, half);
        var edgeX = em.Unary(OpCode.Cos, lean);
        var edgeY = em.Unary(OpCode.Sin, lean);

        // Folded twice: into the wedge one point owns, and then about that
        // wedge's own axis, which is the abs on the second component. Measured
        // from the y axis rather than from x, so a star stands up.
        var span = em.Mul(half, 2f);
        var bearing = em.Binary(OpCode.Atan2, node[0], node[1]);
        var wedge = em.Sub(
            em.Mul(em.Unary(OpCode.Fract, em.Binary(OpCode.Div, bearing, span)), span), half);

        var length = em.Binary(OpCode.Hypot, node[0], node[1]);

        // The folded point, taken relative to the tip: what is left is the
        // distance to one segment running from there down into the valley.
        var px = em.Sub(em.Mul(length, em.Unary(OpCode.Cos, wedge)), em.Mul(node[2], tipX));
        var py = em.Sub(
            em.Mul(length, em.Unary(OpCode.Abs, em.Unary(OpCode.Sin, wedge))),
            em.Mul(node[2], tipY));

        // How far along the edge the nearest point is, held inside the edge's own
        // length so that past the valley it is the valley that is measured to.
        var along = em.Ternary(
            OpCode.Clamp,
            em.Unary(OpCode.Neg, em.Add(em.Mul(px, edgeX), em.Mul(py, edgeY))),
            zero,
            em.Binary(OpCode.Div, em.Mul(node[2], tipY), edgeY));

        var offX = em.Add(px, em.Mul(edgeX, along));
        var offY = em.Add(py, em.Mul(edgeY, along));

        return
        [
            em.Mul(em.Binary(OpCode.Hypot, offX, offY), em.Unary(OpCode.Sign, offX)),
        ];
    }
}
