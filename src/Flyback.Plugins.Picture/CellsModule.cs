using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// Scattered points, and how far you are from the nearest of them: cells,
/// cracks, scales, stone.
/// </summary>
/// <remarks>
/// The other half of the subject, and nothing like the first. Value noise is
/// smooth everywhere and can only ever look like weather; this is built out of
/// distance to a set of points, so it has edges in it — and edges are what a
/// picture needs to look like anything grown, cracked or paved.
/// <para>
/// The construction is Worley's. The plane is cut into a grid, one point is
/// scattered inside each square, and every pixel measures the nine squares around
/// it — nine because a point in the square next door can be nearer than the point
/// in your own, and the diagonal ones can too. There is no way to look at fewer
/// and be right, and no way to skip one in a program that has no branches, so
/// nine is what it costs.
/// </para>
/// <para>
/// And what it costs each square is two Noise, which is where nearly all of the
/// price is. What is wanted per square is a hash — one number, no smoothing, no
/// neighbours — and the machine has no hash op. The one a shader would normally
/// use is <c>fract(sin(x) * 43758.5)</c>, which is a way of turning rounding
/// error into randomness and gives a different answer at every precision: the
/// interpreter and the shader would draw different cells, which is a great deal
/// worse than drawing them slowly. Noise is the only agreed randomness in the
/// machine, so Noise is what this uses — sampled far apart, at multiples of the
/// square's own numbers, so that squares next door land in unrelated parts of the
/// field.
/// </para>
/// <para>
/// So it is the dearest module in the catalogue by a wide margin: eighteen noise
/// lookups a pixel against a Fractal's eight at its most. On the shader that is
/// nothing much; on the interpreter — the preview with the GPU switched off, and
/// every command-line render — expect a still to take seconds rather than
/// milliseconds. That is what a Voronoi is, said out loud, rather than a surprise
/// somebody finds later.
/// </para>
/// <para>
/// Three readings off the one pass. 'distance' is how far the nearest point is
/// and shades each cell from its middle outward; 'edge' is how much further the
/// second nearest is, which goes to nothing exactly on the line between two cells
/// and is therefore the crack; and 'cell' is a number belonging to the square
/// that won, the same everywhere inside it, which is what makes flat mosaics and
/// what a Threshold turns into a scatter of shapes. Nothing else can produce that
/// last one: it is the only value here that is constant across a region and
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
    /// them. Bigger than one, so neighbouring squares land in different cells of
    /// the noise field rather than in the same one — which would make their
    /// points drift together and put a grain in the pattern. Not so big that a
    /// square far from the middle of the picture loses its fractional part to
    /// float on the shader.
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
