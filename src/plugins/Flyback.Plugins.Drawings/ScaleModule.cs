using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Drawings;

/// <summary>A point in 3D scaled about the center, as a whole and along each axis.</summary>
internal static class ScaleModule
{
    public const string TypeId = "flyback.drawings.scale3d";

    public const int ScalePort = 3;
    public const int SxPort = 4;
    public const int SyPort = 5;
    public const int SzPort = 6;

    public static NodeDef Definition { get; } = new(
        TypeId, "Scale 3D", DrawingsPlugin.Category,
        [
            ..Space3d.Point(),
            new PortSpec("scale", PortKind.Scalar, 1f, -4f, 4f) { Help = "Every axis at once." },
            new PortSpec("sx", PortKind.Scalar, 1f, -4f, 4f) { Help = "Across alone. Negative mirrors it." },
            new PortSpec("sy", PortKind.Scalar, 1f, -4f, 4f) { Help = "Up alone. Negative mirrors it." },
            new PortSpec("sz", PortKind.Scalar, 1f, -4f, 4f) { Help = "Depth alone. Negative mirrors it." },
        ],
        [.. Space3d.Point("Across, scaled.", "Up, scaled.", "Toward the viewer, scaled.")],
        (em, node) =>
        [
            em.Mul(node[Space3d.XPort], em.Mul(node[ScalePort], node[SxPort])),
            em.Mul(node[Space3d.YPort], em.Mul(node[ScalePort], node[SyPort])),
            em.Mul(node[Space3d.ZPort], em.Mul(node[ScalePort], node[SzPort])),
        ],
        "Grows or shrinks a point in 3D about the center, all at once with 'scale' or along one axis. "
        + "An envelope on 'scale' makes a model pulse.")
    {
        Words = "grow or shrink in 3D",
        Skin = Art.Skin("scale3d"),
    };
}
