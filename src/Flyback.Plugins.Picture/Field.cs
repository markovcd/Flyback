using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// The convention every module in this plugin is written to, and the sockets
/// that carry it.
/// </summary>
/// <remarks>
/// A shape here is not a picture of itself. It is a <em>signed distance</em>: one
/// number per point, negative inside the form, zero on its edge, positive
/// outside, and measured in the same units the Coordinates module hands out — so
/// a circle of radius 0.5 reads -0.5 at the centre and 0.1 a tenth of the way
/// past its rim.
/// <para>
/// That choice is the whole plugin. A mask — 1 inside, 0 outside — would have
/// been the obvious thing to output and would have been a dead end: two masks
/// cannot be combined into anything but a fade, an outline cannot be recovered
/// from one, and nothing about it says how far away the edge is, which is the
/// number every soft edge is made of. A distance composes instead. Union is the
/// smaller of two distances and intersection the larger, which is to say the
/// Minimum and Maximum this catalogue has had all along; growing a shape is
/// subtracting from it; an outline is the distance to the edge with its sign
/// thrown away. <see cref="CombineModule"/> and <see cref="FillModule"/> are
/// those four sentences written out, and neither knows which shape it was given.
/// </para>
/// <para>
/// What comes out of a shape is therefore not something to look at until a
/// <see cref="FillModule"/> has turned it into ink. Patched straight into a
/// color it reads as a gradient centred on the form, which is a useful thing to
/// see while patching and is not the shape.
/// </para>
/// </remarks>
internal static class Field
{
    /// <summary>
    /// Where the shape is being asked about, normalled to Coordinates the way
    /// every other module that wants a position is (ADR-0050) — so a shape
    /// dropped on the canvas is already sitting in the middle of the picture,
    /// and moving it is a Translate rather than two wires.
    /// </summary>
    public static PortSpec[] Position() =>
    [
        new("x", NormalledTo: NodeCatalog.Across),
        new("y", NormalledTo: NodeCatalog.Down),
    ];

    /// <summary>
    /// A distance in, or out. The range is the editor's rather than the
    /// compiler's, and it is the picture's own extent: y runs -1 to 1, so
    /// nothing on screen is further than about two units from anything else.
    /// </summary>
    public static PortSpec Distance(string name) => new(name, PortKind.Scalar, 0f, -2f, 2f);

    /// <summary>A size, which is never usefully negative.</summary>
    public static PortSpec Size(string name, float value, float most = 2f) =>
        new(name, PortKind.Scalar, value, 0f, most);
}
