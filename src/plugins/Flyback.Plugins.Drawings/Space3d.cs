using Flyback.Core.Graph;

namespace Flyback.Plugins.Drawings;

/// <summary>The sockets every 3D module shares: a point in, a point out.</summary>
internal static class Space3d
{
    public const int XPort = 0;
    public const int YPort = 1;
    public const int ZPort = 2;

    /// <summary>A point's three sockets, in or out.</summary>
    public static PortSpec[] Point(string across = "Across.", string up = "Up.", string toward = "Toward the viewer.") =>
    [
        new("x", PortKind.Scalar, 0f, -1f, 1f) { Help = across },
        new("y", PortKind.Scalar, 0f, -1f, 1f) { Help = up },
        new("z", PortKind.Scalar, 0f, -1f, 1f) { Help = toward },
    ];
}
