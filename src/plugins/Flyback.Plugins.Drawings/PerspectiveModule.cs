using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Drawings;

/// <summary>
/// A point in 3D seen from a camera on the z axis: across and up divided by how far
/// away it is, so what is nearer is larger.
/// </summary>
/// <remarks>
/// Scaled so a point at depth nought keeps its size, so adding perspective to a
/// model changes its depth and not its size on the screen. A point at or behind the
/// camera is held just in front of it rather than dividing by nought.
/// </remarks>
internal static class PerspectiveModule
{
    public const string TypeId = "flyback.drawings.perspective";

    public const int DistancePort = 3;

    /// <summary>The nearest a point is taken to be, as a share of the camera's distance.</summary>
    private const float Nearest = 0.05f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Perspective", DrawingsPlugin.Category,
        [
            ..Space3d.Point(),
            new PortSpec("distance", PortKind.Scalar, 3f, 1.1f, 20f)
            {
                Help = "How far the camera stands from the center. Near is dramatic, far is nearly flat.",
            },
        ],
        [
            new PortSpec("x", PortKind.Scalar, 0f, -1f, 1f) { Help = "Across, as the camera sees it: to the left speaker or a Beam's 'x'." },
            new PortSpec("y", PortKind.Scalar, 0f, -1f, 1f) { Help = "Up, as the camera sees it: to the right speaker or a Beam's 'y'." },
            new PortSpec("depth", PortKind.Scalar, 0f, -1f, 1f) { Help = "The point's z, unchanged, to dim or color what is far." },
        ],
        Emit,
        "Projects a point in 3D onto the screen, so what is nearer is larger. Put it last, after any "
        + "Rotate 3D, Translate 3D or Scale 3D, and wire its x and y to the speakers and a Beam.")
    {
        Words = "3D onto the screen",
        Skin = Art.Skin("perspective"),
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var distance = node[DistancePort];
        var away = em.Binary(OpCode.Max, em.Sub(distance, node[Space3d.ZPort]), em.Mul(distance, Nearest));
        var scale = em.Binary(OpCode.Div, distance, away);

        return [em.Mul(node[Space3d.XPort], scale), em.Mul(node[Space3d.YPort], scale), node[Space3d.ZPort]];
    }
}
