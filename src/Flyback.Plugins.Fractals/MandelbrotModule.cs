using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Fractals;

/// <summary>
/// The Mandelbrot set: each pixel is a point c, and how fast z → z² + c runs
/// away from nought is the picture. The map of every c the other two take.
/// </summary>
/// <remarks>
/// The picture is float on the shader, so a zoom past about twelve halvings runs
/// out of digits and turns to blocks before it runs out of detail.
/// </remarks>
internal static class MandelbrotModule
{
    public const string TypeId = "flyback.fractals.mandelbrot";

    public const int RePort = 2;
    public const int ImPort = 3;
    public const int ZoomPort = 4;
    public const int ShiftPort = 5;

    /// <summary>Where the picture rests: the whole set, a little left of the origin.</summary>
    public const float Middle = -0.75f;

    /// <summary>Half the height of the picture, on the plane, at zoom nought.</summary>
    public const float Span = 1.25f;

    /// <summary>The help on the sockets the Julia set shares.</summary>
    public const string ShiftHelp = "Turns the gradient. A clock on it cycles the colors.",
        ColorHelp = "The classic gradient, navy through white and gold, with the set black.",
        EscapeHelp = "How long each point took to run away, smooth and 0 to 1. For a Palette of your own.",
        InsideHelp = "One on the set, nought off it.";

    public static NodeDef Definition { get; } = new(
        TypeId, "Mandelbrot", FractalsPlugin.Category,
        [
            new PortSpec("x", NormalledTo: NodeCatalog.Across),
            new PortSpec("y", NormalledTo: NodeCatalog.Down),
            new PortSpec("re", PortKind.Scalar, Middle, -2f, 1f) { Help = "The real part of the c in the middle of the picture." },
            new PortSpec("im", PortKind.Scalar, 0f, -1.5f, 1.5f) { Help = "The imaginary part of the c in the middle of the picture." },
            new PortSpec("zoom", PortKind.Scalar, 0f, 0f, 12f)
            {
                Help = "Halves the view each step, so a steady ramp is a steady dive. Past 12 it turns to blocks.",
            },
            new PortSpec("shift", PortKind.Scalar, 0f, 0f, 1f) { Help = ShiftHelp },
        ],
        [
            new PortSpec("color", PortKind.Color) { Help = ColorHelp },
            new PortSpec("escape", PortKind.Scalar, 0f, 0f, 1f) { Help = EscapeHelp },
            new PortSpec("inside", PortKind.Scalar, 0f, 0f, 1f) { Help = InsideHelp },
        ],
        Emit,
        "The Mandelbrot set, the map of every c. The iteration count is set on the node: deeper "
        + "zooms need more, and each costs about a dozen ops.")
    {
        Extras = [Escape.Extra],
        Skin = Art.Skin("mandelbrot"),
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var (re, im) = Escape.Plane(em, node[0], node[1], node[RePort], node[ImPort], node[ZoomPort], Span);

        var cx = Escape.Held(em, re);
        var cy = Escape.Held(em, im);

        // z starts at c, the first step from nought taken for free.
        return Escape.Run(em, cx, cy, cx, cy, Escape.Iterations(node) - 1, 1, node[ShiftPort]);
    }
}
