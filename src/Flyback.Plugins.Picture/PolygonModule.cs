using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// A regular polygon, from a triangle up.
/// </summary>
/// <remarks>
/// A convex polygon is the intersection of as many half-planes as it has edges,
/// and the distance to it is the distance to the nearest of those planes. Written
/// that way it would be one Max per side and a different program for every count.
/// It is written the other way about instead: the plane is found by folding the
/// bearing rather than by trying them all, so the module is the same fifteen ops
/// whether it is drawing a triangle or a sixteen-sided one, and the count can be
/// a signal.
/// <para>
/// The fold is a <c>fract</c> rather than a <c>mod</c>: both wrap, and only the
/// first has one meaning for a negative input on both backends. What comes out is
/// the bearing to the nearest edge's midpoint, and <c>cos</c> of it projects the
/// point onto that edge's normal — which is the distance to the plane, and the
/// distance to the polygon.
/// </para>
/// <para>
/// The bearing is taken from straight up and the wedges are hung either side of
/// it, so there is always a corner at the top — a triangle points up, and so does
/// everything else. It is one number in the fold and it is worth spending: the
/// alternative puts a flat at the top of odd counts and a corner at the top of
/// even ones, which reads as a bug in the module rather than as a property of
/// polygons. <see cref="StarModule"/> is folded the same way for the same reason,
/// so a star and a polygon of the same count point the same way.
/// </para>
/// <para>
/// Exact inside, and outside everywhere the nearest thing is an edge. Beyond a
/// corner it reads the distance to one of the two edge planes rather than to the
/// corner itself, which is a little less than the truth — invisible in a fill, and
/// worth knowing about before dilating one of these by a large amount.
/// </para>
/// <para>
/// 'sides' is floored. A polygon of five and a half sides has a seam in it where
/// the fold does not close, and unlike the Kaleidoscope — which folds the plane
/// and does not care whether the wedge comes back to where it started — this is a
/// module whose whole job is to be a closed form. So the knob steps, and a signal
/// patched into it steps too.
/// </para>
/// </remarks>
internal static class PolygonModule
{
    public const string TypeId = "flyback.picture.polygon";

    public static NodeDef Definition { get; } = new(
        TypeId, "Polygon", ModuleCategories.Forms,
        [
            ..Field.Position(),
            Field.Size("radius", 0.5f),
            new PortSpec("sides", PortKind.Scalar, 5f, 3f, 16f),
        ],
        [Field.Distance("distance")],
        Emit,
        "A regular polygon, as a distance. 'radius' is to the corners rather than to the "
        + "flats, so a polygon and a Circle of the same radius touch, and there is always a "
        + "corner at the top — a triangle points up. 'sides' is rounded down and never goes "
        + "below three: an in-between count is a shape that does not close, so the knob steps "
        + "between whole polygons rather than sliding through broken ones. It costs the same "
        + "however many sides it is asked for.");

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var sides = em.Binary(OpCode.Max, em.Unary(OpCode.Floor, node[3]), em.Constant(3f));
        var segment = em.Binary(OpCode.Div, em.Constant(MathF.Tau), sides);
        var half = em.Mul(segment, 0.5f);

        // The bearing of this point from straight up, wrapped into the wedge
        // belonging to the nearest edge and measured from the middle of it. The
        // wedges are hung so that their boundaries — the corners — fall on the
        // bearing itself, which is what puts a corner at the top.
        var bearing = em.Binary(OpCode.Atan2, node[0], node[1]);
        var wedge = em.Sub(
            em.Mul(em.Unary(OpCode.Fract, em.Binary(OpCode.Div, bearing, segment)), segment),
            half);

        // How far the edge itself stands from the centre: the radius is to the
        // corners, and the flats are nearer by the cosine of half a wedge.
        var flat = em.Mul(node[2], em.Unary(OpCode.Cos, half));

        var reach = em.Mul(
            em.Unary(OpCode.Cos, wedge), em.Binary(OpCode.Hypot, node[0], node[1]));

        return [em.Sub(reach, flat)];
    }
}
