using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Shapes;

/// <summary>
/// Ink: a distance turned into something to look at, filled and outlined at
/// once.
/// </summary>
/// <remarks>
/// The other half of the convention <see cref="Field"/> sets out. Everything
/// upstream of here is a measurement, and this is the one module that decides
/// what a measurement looks like — which is why there is one of it rather than a
/// fill knob on every shape: a Combine of four forms is one Fill, and putting the
/// decision on each of the four would have meant taking it four times and getting
/// it slightly different each time.
/// <para>
/// Both outputs at once, for the reason the Filter hands out three responses at
/// once: they are two readings of one number and a patch usually wants both — a
/// solid form with its own edge picked out is two wires from here and would
/// otherwise be two Fills fed from the same distance, differing only in a knob.
/// The outline is the fill of <c>|d| - width/2</c>, which is the shape's edge
/// treated as a shape in its own right: the set of points a certain distance from
/// the boundary, on either side of it.
/// </para>
/// <para>
/// 'softness' is in the same units as everything else here rather than in pixels,
/// and it has to be: nothing in the program knows how large the frame is, and the
/// same patch is drawn at preview size, at export size and into a movie. A
/// softness of a hundredth is about three pixels tall on a 540-line preview and
/// six on a 1080-line render — the edge stays the same fraction of the picture
/// rather than the same number of pixels, which is what makes a still and a
/// preview of it the same image. At zero the edge is a hard step, and will
/// stair-step exactly as any hard threshold does.
/// </para>
/// </remarks>
internal static class FillModule
{
    public const string TypeId = "flyback.shapes.fill";

    public static NodeDef Definition { get; } = new(
        TypeId, "Fill", ShapesPlugin.Category,
        [
            Field.Distance("distance"),
            Field.Size("softness", 0.01f, 0.5f),
            Field.Size("width", 0.02f, 1f),
        ],
        [
            new PortSpec("fill", PortKind.Scalar, 0f, 0f, 1f),
            new PortSpec("outline", PortKind.Scalar, 0f, 0f, 1f),
        ],
        Emit,
        "Turns a distance into ink: 1 inside the shape, 0 outside, and a soft edge of "
        + "'softness' between them. 'outline' is the same shape's edge instead, 'width' "
        + "across and centred on it, so a form and its own outline are two wires from one "
        + "module. Both are 0..1, which is what a color's 'value' wants and what a Mixer "
        + "blends. Sizes are in the picture's own units rather than in pixels, so a patch "
        + "looks the same at any size it is rendered at.");

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
