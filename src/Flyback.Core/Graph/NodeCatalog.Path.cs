using Flyback.Core.Compile;
using Flyback.Core.Graph.Extras;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    /// <summary>The module that plays a drawing as a path, for a Beam to draw.</summary>
    internal const string PathTypeId = "path";

    /// <summary>
    /// An SVG, an OBJ or a PNG's outlines as one closed path, gone round once a cycle:
    /// 'x' to the left speaker and 'y' to the right draws it on a Beam.
    /// </summary>
    /// <remarks>
    /// An oscillator whose wave is three tables. The phase accumulates as a Sine's
    /// does (ADR-0030), so a Note or a sequencer plays the drawing as a melody.
    /// </remarks>
    private static NodeDef PathModule() =>
        new NodeDef(
            PathTypeId, "Path", ModuleCategories.Sources,
            [
                Domain("in"),
                Freq with { Default = 80f, Help = "How many times a second it goes round the drawing: the pitch." },
                Phase,
                Num("amp", 1f, 0f, 2f) with { Help = "The drawing's size: 1 fills -1 to 1." },
            ],
            [
                Num("x", 0f, -1f, 1f) with { Help = "Across: the left channel of oscilloscope music." },
                Num("y", 0f, -1f, 1f) with { Help = "Up: the right channel of oscilloscope music." },
                Num("z", 0f, -1f, 1f) with { Help = "Toward the viewer, for a model to be turned and projected. Nought for a flat drawing." },
            ],
            (em, node) =>
            {
                if (node.Shape is not { } shape) return [em.Constant(0f), em.Constant(0f), em.Constant(0f)];

                var position = em.Unary(OpCode.Fract, em.Phase(node[0], node[1], node[2]));

                return
                [
                    em.Mul(em.Table(position, shape.X), node[3]),
                    em.Mul(em.Table(position, shape.Y), node[3]),
                    em.Mul(em.Table(position, shape.Z), node[3]),
                ];
            },
            "Plays an SVG, an OBJ model or a PNG's outlines as a path, once round each cycle at 'freq', "
            + "every stroke at the same speed. Wire 'x' to the left speaker and 'y' to the right and a Beam "
            + "draws it. The file path is stored with the patch, so moving or renaming it breaks it.")
        {
            Extras = [new ShapeExtra()],
        };
}
