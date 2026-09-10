using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// The convention every module in this plugin is written to, and the sockets that
/// carry it.
/// </summary>
/// <remarks>
/// A shape here is a signed distance: one number per point, negative inside the
/// form, zero on its edge, positive outside, in the units Coordinates hands out.
/// <para>
/// That choice is the whole plugin. A mask would have been a dead end — two of
/// them cannot be combined into anything but a fade, and nothing about one says
/// how far away the edge is, which is the number every soft edge is made of. A
/// distance composes: union is the smaller of two, intersection the larger,
/// growing is subtracting, and an outline is the distance with its sign thrown
/// away. <see cref="CombineModule"/> and <see cref="FillModule"/> are those
/// sentences written out, and neither knows which shape it was given.
/// </para>
/// <para>
/// What comes out of a shape is not something to look at until a
/// <see cref="FillModule"/> has turned it into ink: patched straight into a color
/// it reads as a gradient centred on the form.
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
