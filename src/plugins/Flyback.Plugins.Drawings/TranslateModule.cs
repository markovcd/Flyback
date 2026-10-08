using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Drawings;

/// <summary>A point in 3D moved by an offset along each axis.</summary>
internal static class TranslateModule
{
    public const string TypeId = "flyback.drawings.translate3d";

    public const int DxPort = 3;
    public const int DyPort = 4;
    public const int DzPort = 5;

    public static NodeDef Definition { get; } = new(
        TypeId, "Translate 3D", DrawingsPlugin.Category,
        [
            ..Space3d.Point(),
            new PortSpec("dx", PortKind.Scalar, 0f, -2f, 2f) { Help = "How far it moves across." },
            new PortSpec("dy", PortKind.Scalar, 0f, -2f, 2f) { Help = "How far it moves up." },
            new PortSpec("dz", PortKind.Scalar, 0f, -2f, 2f) { Help = "How far it moves toward the viewer." },
        ],
        [..Space3d.Point("Across, moved.", "Up, moved.", "Toward the viewer, moved.")],
        (em, node) =>
        [
            em.Add(node[Space3d.XPort], node[DxPort]),
            em.Add(node[Space3d.YPort], node[DyPort]),
            em.Add(node[Space3d.ZPort], node[DzPort]),
        ],
        "Moves a point in 3D. Before a Perspective, a positive 'dz' brings a model closer and a negative one "
        + "sends it away.")
    {
        Skin = Art.Skin("translate3d"),
    };
}
