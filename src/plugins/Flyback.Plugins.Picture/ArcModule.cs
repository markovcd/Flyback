using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// A band round part of a circle, opening from the top: a dial, a gauge, a ring.
/// </summary>
/// <remarks>
/// Iñigo Quílez's construction, mirrored about the y axis so that one test decides
/// which part of the shape is nearest: a point inside the swept wedge is measured to
/// the rim, and one outside it to the nearer end, which is a round cap. Exact, and
/// the same length of program at any sweep, so a signal on 'sweep' fills it like a
/// meter.
/// </remarks>
internal static class ArcModule
{
    public const string TypeId = "flyback.picture.arc";

    public static NodeDef Definition { get; } = new(
        TypeId, "Arc", ModuleCategories.Forms,
        [
            ..Field.Position(),
            Field.Size("radius", 0.5f) with { Help = "To the middle of the band." },
            new PortSpec("sweep", PortKind.Scalar, 0.75f, 0f, 1f)
            {
                Help = "How much of the circle it covers: 0 is a dot at the top, 1 a whole ring.",
            },
            Field.Size("width", 0.1f, 1f) with { Help = "Across the band." },
        ],
        [Field.Shape()],
        Emit,
        "Part of a ring, as a distance, centered on the top and opening both ways, with round "
        + "ends. Exact, so a sweep driven by a signal fills it like a dial.")
    {
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Forms))
        {
            Glyph = "M6.3,17.7 A8,8 0 1 1 17.7,17.7",
        },
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var zero = em.Constant(0f);
        var one = em.Constant(1f);

        // Half the opening, either side of straight up.
        var half = em.Mul(em.Ternary(OpCode.Clamp, node[3], zero, one), MathF.PI);
        // Held just above nought, where the axis would otherwise tie: the column
        // above a whole ring belongs to its rim, and the one below a dot to its cap.
        var endX = em.Binary(OpCode.Max, em.Unary(OpCode.Sin, half), em.Constant(1e-6f));
        var endY = em.Unary(OpCode.Cos, half);

        var x = em.Unary(OpCode.Abs, node[0]);
        var y = node[1];

        // Within the swept bearing is nearer the rim than the cap.
        var within = em.Binary(OpCode.Step, em.Mul(endY, x), em.Mul(endX, y));

        var rim = em.Unary(
            OpCode.Abs, em.Sub(em.Binary(OpCode.Hypot, x, y), node[2]));
        var cap = em.Binary(
            OpCode.Hypot,
            em.Sub(x, em.Mul(endX, node[2])),
            em.Sub(y, em.Mul(endY, node[2])));

        return [em.Sub(em.Ternary(OpCode.Mix, cap, rim, within), em.Mul(node[4], 0.5f))];
    }
}
