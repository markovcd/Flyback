using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Fractals;

/// <summary>
/// The Julia set of one c: each pixel is where z starts, and how fast
/// z → z² + c runs away from it is the picture.
/// </summary>
/// <remarks>
/// The Mandelbrot set's own arithmetic with its two roles swapped. A c inside
/// the Mandelbrot set gives a Julia set in one piece, a c outside gives dust,
/// and the c's on its edge give the pictures everybody knows.
/// </remarks>
internal static class JuliaModule
{
    public const string TypeId = "flyback.fractals.julia";

    public const int RePort = 2;
    public const int ImPort = 3;
    public const int ZoomPort = 4;
    public const int ShiftPort = 5;

    /// <summary>Half the height of the picture, on the plane, at zoom nought: the whole of any Julia set.</summary>
    public const float Span = 1.2f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Julia", FractalsPlugin.Category,
        [
            new PortSpec("x", NormalledTo: NodeCatalog.Across),
            new PortSpec("y", NormalledTo: NodeCatalog.Down),
            new PortSpec("re", PortKind.Scalar, -0.8f, -2f, 1f) { Help = "The real part of c." },
            new PortSpec("im", PortKind.Scalar, 0.156f, -1.5f, 1.5f) { Help = "The imaginary part of c." },
            new PortSpec("zoom", PortKind.Scalar, 0f, 0f, 12f) { Help = "Halves the view about the middle each step." },
            new PortSpec("shift", PortKind.Scalar, 0f, 0f, 1f) { Help = MandelbrotModule.ShiftHelp },
        ],
        [
            new PortSpec("color", PortKind.Color) { Help = MandelbrotModule.ColorHelp },
            new PortSpec("escape", PortKind.Scalar, 0f, 0f, 1f) { Help = MandelbrotModule.EscapeHelp },
            new PortSpec("inside", PortKind.Scalar, 0f, 0f, 1f) { Help = MandelbrotModule.InsideHelp },
        ],
        Emit,
        "The Julia set of c: patch the same 're' and 'im' into a Mandelbrot and an Orbit to see "
        + "where c is and hear it. A c inside the Mandelbrot set gives one piece, a c outside "
        + "gives dust, and the edge between is where the famous ones are. The iteration count on "
        + "the node is the Mandelbrot's.")
    {
        Extras = [Escape.Extra],
        Skin = Art.Skin("julia"),
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var zero = em.Constant(0f);
        var (zx, zy) = Escape.Plane(em, node[0], node[1], zero, zero, node[ZoomPort], Span);

        return Escape.Run(
            em,
            Escape.Held(em, zx),
            Escape.Held(em, zy),
            Escape.Held(em, node[RePort]),
            Escape.Held(em, node[ImPort]),
            Escape.Iterations(node),
            0,
            node[ShiftPort]);
    }
}
