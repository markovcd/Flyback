using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins;

namespace Flyback.Plugins.Picture;

/// <summary>
/// A star of any number of points, sharp or blunt.
/// </summary>
/// <remarks>
/// Iñigo Quílez's construction. The angle is folded into a single wedge the way
/// <see cref="PolygonModule"/> folds it and then folded again about the wedge's
/// own axis — that second fold is the <c>abs</c>, and it is what makes a star out
/// of a polygon. What is left is one point with its tip on the y axis, and the
/// distance to it is the distance to a line segment. Exact, unlike the polygon
/// beside it, which is the difference between measuring to a plane and to a
/// segment with ends on it.
/// <para>
/// 'sharpness' is a knob over the shape's real parameter: the construction wants a
/// second count, where two is a polygon with its corners on the tips and the
/// number of points is a needle. The socket runs 0 to 1 across that span, which
/// also keeps it meaningful when 'points' is a signal.
/// </para>
/// </remarks>
internal static class StarModule
{
    public const string TypeId = "flyback.picture.star";

    public static NodeDef Definition { get; } = new(
        TypeId, "Star", ModuleCategories.Forms,
        [
            ..Field.Position(),
            Field.Size("radius", 0.5f) with { Help = "To the tips." },
            new PortSpec("points", PortKind.Scalar, 5f, 2f, 16f, Display: PortDisplay.Integer)
            {
                Help = "Rounded down like a Polygon's 'sides'. 2 is a lens.",
            },
            new PortSpec("sharpness", PortKind.Scalar, 0.45f, 0f, 1f)
            {
                Help = "From a polygon at 0 to needles at 1, growing the points.",
            },
        ],
        [Field.Shape()],
        Emit,
        "A star, as a distance, with a point at the top. Exact, so an outline is its stated "
        + "width even at the tips.")
    {
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Forms))
        {
            Glyph = "M12,2.5 L14.7,9.4 L22,10 L16.4,14.8 L18.2,22 L12,17.9 L5.8,22 L7.6,14.8 "
                + "L2,10 L9.3,9.4 Z",
        },
    };

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
