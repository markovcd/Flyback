using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins;

namespace Flyback.Plugins.Picture;

/// <summary>
/// A regular polygon, from a triangle up.
/// </summary>
/// <remarks>
/// A convex polygon is the intersection of as many half-planes as it has edges,
/// which written out would be one Max per side and a different program for every
/// count. Written the other way about — the plane found by folding the bearing —
/// the module is the same fifteen ops whether it draws a triangle or a
/// sixteen-sided one, and the count can be a signal.
/// <para>
/// The fold is a <c>fract</c> rather than a <c>mod</c>: both wrap, and only the
/// first has one meaning for a negative input on both backends. What comes out is
/// the bearing to the nearest edge's midpoint, and <c>cos</c> of it is the
/// distance to that edge's plane.
/// </para>
/// <para>
/// The bearing is taken from straight up and the wedges hung either side, so there
/// is always a corner at the top; the alternative puts a flat at the top of odd
/// counts and a corner at the top of even ones. <see cref="StarModule"/> is folded
/// the same way, so a star and a polygon of the same count point the same way.
/// </para>
/// <para>
/// Exact inside, and outside everywhere the nearest thing is an edge; beyond a
/// corner it reads a little less than the truth, which is invisible in a fill and
/// worth knowing before dilating one by a large amount. 'sides' is floored,
/// because a polygon of five and a half sides has a seam where the fold does not
/// close.
/// </para>
/// </remarks>
internal static class PolygonModule
{
    public const string TypeId = "flyback.picture.polygon";

    public static NodeDef Definition { get; } = new(
        TypeId, "Polygon", ModuleCategories.Forms,
        [
            ..Field.Position(),
            Field.Size("radius", 0.5f) with { Help = "To the corners, so it touches a Circle of the same radius." },
            new PortSpec("sides", PortKind.Scalar, 5f, 3f, 16f, Display: PortDisplay.Integer)
            {
                Help = "Rounded down and never below three, so the knob steps between whole polygons.",
            },
        ],
        [Field.Shape()],
        Emit,
        "A regular polygon, as a distance, with a corner at the top. Costs the same at any "
        + "count.")
    {
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Forms))
        {
            Glyph = "M12,3 L20.5,9.2 L17.3,19.3 L6.7,19.3 L3.5,9.2 Z",
        },
    };

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

        // How far the edge itself stands from the center: the radius is to the
        // corners, and the flats are nearer by the cosine of half a wedge.
        var flat = em.Mul(node[2], em.Unary(OpCode.Cos, half));

        var reach = em.Mul(
            em.Unary(OpCode.Cos, wedge), em.Binary(OpCode.Hypot, node[0], node[1]));

        return [em.Sub(reach, flat)];
    }
}
