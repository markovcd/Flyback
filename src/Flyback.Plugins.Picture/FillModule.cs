using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins;

namespace Flyback.Plugins.Picture;

/// <summary>
/// Ink: a distance turned into something to look at, filled and outlined at once.
/// </summary>
/// <remarks>
/// The other half of the convention <see cref="Field"/> sets out. One module
/// rather than a fill knob on every shape, because a Combine of four forms is one
/// Fill and the decision would otherwise be taken four times.
/// <para>
/// Both outputs at once, being two readings of one number that a patch usually
/// wants both of. The outline is the fill of <c>|d| - width/2</c>, which is the
/// shape's edge treated as a shape in its own right.
/// </para>
/// <para>
/// 'softness' is in the same units as everything else rather than in pixels, and
/// has to be: nothing in the program knows how large the frame is. The edge stays
/// the same fraction of the picture rather than the same number of pixels, which
/// is what makes a still and a preview of it the same image.
/// </para>
/// </remarks>
internal static class FillModule
{
    public const string TypeId = "flyback.picture.fill";

    public static NodeDef Definition { get; } = new(
        TypeId, "Fill", ModuleCategories.Forms,
        [
            Field.Distance("distance") with { Help = "The shape to fill: any shape's 'distance', or a Combine's." },
            Field.Size("softness", 0.01f, 0.5f) with { Help = "How wide the edge is, on both outputs." },
            Field.Size("width", 0.02f, 1f) with { Help = "How wide the outline is." },
        ],
        [
            new PortSpec("fill", PortKind.Scalar, 0f, 0f, 1f)
            {
                Help = "1 inside, 0 outside, with an edge 'softness' wide.",
            },
            new PortSpec("outline", PortKind.Scalar, 0f, 0f, 1f)
            {
                Help = "The shape's edge instead, 'width' across.",
            },
        ],
        Emit,
        "Turns a distance into ink, filled and outlined at once. Both outputs run 0 to 1, for "
        + "a color's 'value' or a Mixer. Sizes are in picture units, so it looks the same at "
        + "any resolution.")
    {
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Forms))
        {
            Glyph = "M12,3 C16,9 19,13 19,16.5 A7,7 0 1 1 5,16.5 C5,13 8,9 12,3 Z",
        },
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var one = em.Constant(1f);
        var soft = em.Mul(node[1], 0.5f);
        var edge = em.Unary(OpCode.Neg, soft);

        // Written as one minus the ramp rather than as a ramp with its edges
        // swapped round. Reversed edges do work — both backends clamp the same
        // way — but they stop working at a softness of nothing, where the two
        // edges meet and the tie is broken by a comparison that then has the
        // shape inside out.
        var fill = em.Sub(one, em.Ternary(OpCode.Smoothstep, edge, soft, node[0]));

        var band = em.Sub(em.Unary(OpCode.Abs, node[0]), em.Mul(node[2], 0.5f));
        var outline = em.Sub(one, em.Ternary(OpCode.Smoothstep, edge, soft, band));

        return [fill, outline];
    }
}
