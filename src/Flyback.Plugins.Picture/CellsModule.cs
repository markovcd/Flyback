using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// Scattered points, and how far you are from the nearest of them: cells, cracks,
/// scales, stone.
/// </summary>
/// <remarks>
/// Worley's construction: the plane is cut into a grid, one point is scattered in
/// each square, and every pixel measures the nine squares around it — a point next
/// door can be nearer than your own, and there is no way to look at fewer in a
/// program with no branches.
/// <para>
/// Each square costs two Noise, which is nearly all of the price. What is wanted
/// is a hash, and the machine has no hash op: the usual
/// <c>fract(sin(x) * 43758.5)</c> turns rounding error into randomness and gives
/// a different answer at every precision, so the interpreter and the shader would
/// draw different cells. Noise is the only agreed randomness there is — sampled
/// far apart, so squares next door land in unrelated parts of the field. That
/// makes this the dearest module in the catalogue: eighteen noise lookups a pixel
/// against a Fractal's eight, which on the interpreter is seconds rather than
/// milliseconds for a still.
/// </para>
/// <para>
/// Three readings off the one pass. 'distance' shades each cell from its middle
/// outward; 'edge' is how much further the second nearest is, and so goes to
/// nothing on the line between two cells; 'cell' is a number belonging to the
/// square that won, which is the only value here constant across a region and
/// discontinuous at its border.
/// </para>
/// </remarks>
internal static class CellsModule
{
    public const string TypeId = "flyback.picture.cells";

    /// <summary>
    /// Further than any point in the nine squares can be, so the first
    /// comparison always takes the candidate. The furthest a nearest point can
    /// actually be is under two squares' width.
    /// </summary>
    private const float Beyond = 8f;

    /// <summary>
    /// What a square's coordinates are multiplied by before the noise is read at
    /// them. Bigger than one, so neighbouring squares land in different cells of the
    /// noise field rather than drifting together into a grain; not so big that a
    /// square far from the middle loses its fractional part to float on the shader.
    /// </summary>
    private const float Apart = 13.7f;

    private const float Across = 27.3f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Cells", ModuleCategories.Patterns,
        [
            new PortSpec("x", NormalledTo: NodeCatalog.Across),
            new PortSpec("y", NormalledTo: NodeCatalog.Down),
            new PortSpec("z"),
            new PortSpec("scale", PortKind.Scalar, 4f, 0f, 32f),
            new PortSpec("jitter", PortKind.Scalar, 1f, 0f, 1f),
        ],
        [
            new PortSpec("distance", PortKind.Scalar, 0f, 0f, 1f),
            new PortSpec("edge", PortKind.Scalar, 0f, 0f, 1f),
            new PortSpec("cell", PortKind.Scalar, 0f, 0f, 1f),
        ],
        Emit,
        "Scattered points and the distance to the nearest — cells, cracks, scales and stone, "
        + "which is the one thing smooth noise cannot make because it has no edges in it. "
        + "'distance' shades each cell outward from its point. 'edge' is nothing exactly on the "
        + "line between two cells and rises inside them, so a Threshold on it is a crack and a "
        + "Smoothstep is a soft one. 'cell' is one number for the whole cell and a different "
        + "one next door, which is a flat mosaic and the only value here that jumps at a "
        + "border. 'jitter' at 1 scatters the points and at 0 pins them to the middle of a "
        + "square grid, so a patch can slide between organic and mechanical. 'z' drifts the "
        + "points. It is the most expensive module in the catalogue — eighteen noise lookups a "
        + "pixel, which is what measuring nine squares costs — so it is a joy on the GPU and "
        + "slow on the interpreter, which is what a command-line render uses.");

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var zero = em.Constant(0f);
        var one = em.Constant(1f);
        var half = em.Constant(0.5f);

        var jitter = em.Ternary(OpCode.Clamp, node[4], zero, one);
        var scale = node[3];

        var x = em.Mul(node[0], scale);
        var y = em.Mul(node[1], scale);

        // Which square this is in, and where in it. Fract rather than x - floor(x)
        // because both backends already agree about Fract for a negative input,
        // which is most of the picture.
        var squareX = em.Unary(OpCode.Floor, x);
        var squareY = em.Unary(OpCode.Floor, y);
        var withinX = em.Unary(OpCode.Fract, x);
        var withinY = em.Unary(OpCode.Fract, y);

        var nearest = em.Constant(Beyond);
        var second = em.Constant(Beyond);
        var chosen = zero;

        for (var down = -1; down <= 1; down++)
        for (var across = -1; across <= 1; across++)
        {
            var atX = em.Add(squareX, across);
            var atY = em.Add(squareY, down);

            // Two lookups, sampled far enough apart that the two coordinates of
            // one point are unrelated — one lookup with the second coordinate
            // derived from it would put every point on a curve, and drifting z
            // would walk them along it.
            var alongX = em.Mul(atX, Apart);
            var alongY = em.Mul(atY, Across);

            var pointX = em.Ternary(OpCode.Noise3, alongX, alongY, node[2]);
            var pointY = em.Ternary(
                OpCode.Noise3, em.Add(alongX, 41.9f), em.Add(alongY, 7.3f), node[2]);

            // Held about the middle of the square rather than about nought, so
            // that jitter turns the scatter down to a plain grid instead of
            // dragging every point into one corner.
            var awayX = em.Sub(
                em.Add(em.Ternary(OpCode.Mix, half, pointX, jitter), across), withinX);

            var awayY = em.Sub(
                em.Add(em.Ternary(OpCode.Mix, half, pointY, jitter), down), withinY);

            var far = em.Binary(OpCode.Hypot, awayX, awayY);

            // Which square is winning, taken before the winner is updated. Step
            // answers 1 where the candidate is no nearer, so one minus it is the
            // swap — and mixing on that is how a program with no branches picks.
            var closer = em.Sub(one, em.Binary(OpCode.Step, nearest, far));
            chosen = em.Ternary(OpCode.Mix, chosen, pointX, closer);

            // The runner-up is the nearer of what it was and whichever of the two
            // this comparison did not keep.
            second = em.Binary(OpCode.Min, second, em.Binary(OpCode.Max, nearest, far));
            nearest = em.Binary(OpCode.Min, nearest, far);
        }

        return [nearest, em.Sub(second, nearest), chosen];
    }
}
