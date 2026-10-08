using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Drawings;

/// <summary>
/// An SVG, an OBJ or a PNG's outlines as one closed path, gone round once a cycle:
/// 'x' to the left speaker and 'y' to the right draws it on a Beam.
/// </summary>
/// <remarks>
/// An oscillator whose wave is three tables. The phase accumulates as a Sine's
/// does (ADR-0030), so a Note or a sequencer plays the drawing as a melody.
/// </remarks>
internal static class PathModule
{
    public const string TypeId = "flyback.drawings.path";

    public const int InPort = 0;
    public const int FreqPort = 1;
    public const int PhasePort = 2;
    public const int AmpPort = 3;

    public const int XPort = 0;
    public const int YPort = 1;
    public const int ZPort = 2;

    public static NodeDef Definition { get; } = new(
        TypeId, "Path", DrawingsPlugin.Category,
        [
            new PortSpec("in", NormalledTo: NodeCatalog.Clock, Domain: true) { Standard = true },
            new PortSpec("freq", PortKind.Scalar, 80f, 0f, 20_000f)
            {
                Knee = 0.02f,
                Help = "How many times a second it goes round the drawing: the pitch.",
            },
            new PortSpec("phase", PortKind.Scalar, 0f, 0f, 1f) { Lenient = true, Standard = true },
            new PortSpec("amp", PortKind.Scalar, 1f, 0f, 2f) { Help = "The drawing's size: 1 fills -1 to 1." },
        ],
        [
            new PortSpec("x", PortKind.Scalar, 0f, -1f, 1f) { Help = "Across: the left channel of oscilloscope music." },
            new PortSpec("y", PortKind.Scalar, 0f, -1f, 1f) { Help = "Up: the right channel of oscilloscope music." },
            new PortSpec("z", PortKind.Scalar, 0f, -1f, 1f)
            {
                Help = "Toward the viewer, for a model to be turned and projected. Nought for a flat drawing.",
            },
        ],
        Emit,
        "Plays an SVG, an OBJ model or a PNG's outlines as a path, once round each cycle at 'freq', "
        + "every stroke at the same speed. Wire 'x' to the left speaker and 'y' to the right and a Beam "
        + "draws it. The file path is stored with the patch, so moving or renaming it breaks it.")
    {
        Extras = [new DrawingExtra()],
        Skin = Art.Skin("path"),
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        if (node.Extra<LoadedShape>(DrawingExtra.Name) is not { } shape)
            return [em.Constant(0f), em.Constant(0f), em.Constant(0f)];

        var position = em.Unary(OpCode.Fract, em.Phase(node[InPort], node[FreqPort], node[PhasePort]));

        return
        [
            em.Mul(em.Table(position, shape.X), node[AmpPort]),
            em.Mul(em.Table(position, shape.Y), node[AmpPort]),
            em.Mul(em.Table(position, shape.Z), node[AmpPort]),
        ];
    }
}
