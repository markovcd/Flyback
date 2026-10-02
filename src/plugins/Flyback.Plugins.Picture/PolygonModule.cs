using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// A regular polygon, from a triangle up.
/// </summary>
/// <remarks>
/// Folding the bearing into one wedge finds the nearest edge, so any side count
/// costs the same fifteen ops and can be a signal. The fold is <c>fract</c>, not
/// <c>mod</c>, which disagrees across backends on negatives. A corner is always on
/// top, as in <see cref="StarModule"/>. The distance undershoots past a corner, so
/// large dilations round off; 'sides' is floored since a fractional count leaves
/// a seam.
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
