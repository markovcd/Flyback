using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Voice;

/// <summary>
/// An envelope read off a position rather than started by a trigger: what is left
/// of the current beat, raised to a power.
/// </summary>
/// <remarks>
/// Stateless, which is the whole reason it is not a <see cref="DecayModule"/>. A
/// Decay keeps its level in a cell, so the screen sees the trigger and never the
/// fall; this is arithmetic on the domain, so a drum and the ring it pushes are one
/// number on both sinks, and it cannot drift off the grid because it is the grid.
/// The price is that the fall is a fixed share of the stroke rather than a time.
/// </remarks>
internal static class StrokeModule
{
    public const string TypeId = "flyback.voice.stroke";

    public static NodeDef Definition { get; } = new(
        TypeId, "Stroke", ModuleCategories.Timing,
        [
            new PortSpec("in", NormalledTo: NodeCatalog.Clock, Domain: true) { Standard = true },
            new PortSpec("rate", PortKind.Scalar, 1f, 0f, 32f) { Help = "Strokes for each unit of 'in'." },
            new PortSpec("offset", PortKind.Scalar, 0f, 0f, 1f)
            {
                Lenient = true,
                Help = "Slides the hits by a share of a stroke.",
            },
            new PortSpec("curve", PortKind.Scalar, 3f, 0.1f, 16f) { Help = "The fall's shape: 1 is straight, 3 a pluck, 8 a click." },
        ],
        [
            new PortSpec("out", PortKind.Scalar, 0f, 0f, 1f)
            {
                Help = "Jumps to 1 at each stroke and falls to 0 by its end.",
            },
            new PortSpec("phase", PortKind.Scalar, 0f, 0f, 1f) { Help = "How far through the stroke, 0 to 1." },
        ],
        Emit,
        "A drum hit without a trigger. 'in' times 'rate' counts strokes: a count of beats in "
        + "and a 'rate' of 4 is every sixteenth, and 'rate' 0.5 with 'offset' 0.5 is beats two "
        + "and four. The same on the picture as in the speakers.")
    {
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Timing))
        {
            Glyph = "M3,18 L10,18 L13,4 L16,18 L21,18",
        },
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var phase = em.Unary(OpCode.Fract, em.Add(em.Mul(node[0], node[1]), node[2]));
        var left = em.Sub(em.Constant(1f), phase);

        return [em.Binary(OpCode.Pow, left, node[3]), phase];
    }
}
