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
            new PortSpec("re", PortKind.Scalar, -0.8f, -2f, 1f),
            new PortSpec("im", PortKind.Scalar, 0.156f, -1.5f, 1.5f),
            new PortSpec("zoom", PortKind.Scalar, 0f, 0f, 12f),
            new PortSpec("shift", PortKind.Scalar, 0f, 0f, 1f),
        ],
        [
            new PortSpec("color", PortKind.Color),
            new PortSpec("escape", PortKind.Scalar, 0f, 0f, 1f),
            new PortSpec("inside", PortKind.Scalar, 0f, 0f, 1f),
        ],
        Emit,
        "The Julia set of c, whose 're' and 'im' are the point: patch the same two into a "
        + "Mandelbrot and an Orbit to see where c is and hear it. A c inside the Mandelbrot set "
        + "gives one piece, a c outside gives dust, and the edge between is where the famous "
        + "ones are. 'zoom' halves the view about the middle each step. 'color', 'shift', "
        + "'escape' and 'inside' are the Mandelbrot's, and so is the iteration count on the node.")
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
