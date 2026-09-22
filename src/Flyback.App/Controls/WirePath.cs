using Avalonia;
using Avalonia.Media;

namespace Flyback.App.Controls;

/// <summary>The shape a wire is drawn in between two sockets, forwards or round the back.</summary>
internal static class WirePath
{
    /// <summary>
    /// How far below the lower of two modules a wire traveling leftwards runs
    /// back, where there is no gap between them to run through. Also the least
    /// gap that counts as one. Far enough to clear a box rather than hide behind
    /// it — resting wires are drawn under the modules — and no further.
    /// </summary>
    private const double ReturnWireDrop = 30;

    /// <summary>
    /// The furthest a leftward wire's bends reach out sideways from their sockets.
    /// Sized by the turn rather than by the span, and capped here, which is what
    /// keeps a module wired to itself from being drawn as an ellipse wider than
    /// the module. See <see cref="Bend"/>.
    /// </summary>
    private const double ReturnWireReach = 60;

    /// <summary>
    /// The height a wire traveling leftwards runs back at, between the two
    /// modules it joins.
    /// </summary>
    /// <remarks>
    /// Through the gap between them where one sits clear above the other, which
    /// is the short way round and crosses neither. Where they overlap on the
    /// vertical — side by side, or the same module twice — there is no gap to
    /// use, and it passes under both instead.
    /// </remarks>
    public static double ReturnRun(Rect source, Rect target)
    {
        if (target.Top - source.Bottom >= ReturnWireDrop) return (source.Bottom + target.Top) / 2;
        if (source.Top - target.Bottom >= ReturnWireDrop) return (target.Bottom + source.Top) / 2;

        return Math.Max(source.Bottom, target.Bottom) + ReturnWireDrop;
    }

    /// <summary>A horizontal-tangent bezier, so wires leave and enter sockets cleanly.</summary>
    public static void Draw(DrawingContext context, Point from, Point to, IPen pen)
    {
        var reach = Reach(from, to);

        var geometry = new StreamGeometry();
        using (var sink = geometry.Open())
        {
            sink.BeginFigure(from, false);
            sink.CubicBezierTo(from.WithX(from.X + reach), to.WithX(to.X - reach), to);
            sink.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    /// <summary>The point <paramref name="t"/> of the way along the wire <see cref="Draw"/> draws.</summary>
    public static Point At(Point from, Point to, double t)
    {
        var reach = Reach(from, to);
        var (a, b) = (from.WithX(from.X + reach), to.WithX(to.X - reach));
        var u = 1 - t;

        return from * (u * u * u) + a * (3 * u * u * t) + b * (3 * u * t * t) + to * (t * t * t);
    }

    private static double Reach(Point from, Point to) => Math.Max(45, Math.Abs(to.X - from.X) * 0.5);

    /// <summary>
    /// A wire whose input is left of its output: out to the right of the socket it
    /// leaves, round in a U-bend to <paramref name="run"/>, back along it, and
    /// round again into the socket it arrives at from the left. Every piece meets
    /// the next on a shared tangent, so there is no corner anywhere on it — the
    /// same soft line as <see cref="Draw"/>, bent twice.
    /// </summary>
    /// <remarks>
    /// <see cref="Draw"/>'s single bezier is wrong for these. It spreads its
    /// control points by half the span, which turns a long leftward wire into a
    /// diagonal across the patch and a module wired to itself into an ellipse
    /// wider than the module. Here each bend is sized by how far it has to turn
    /// rather than by how far the wire travels, so the wire reads the same whether
    /// it goes round one module or across the canvas.
    /// </remarks>
    /// <param name="run">The height the flat run back sits at — see <see cref="ReturnRun"/>.</param>
    public static void DrawReturn(DrawingContext context, Point from, Point to, double run, IPen pen)
    {
        var geometry = new StreamGeometry();
        using (var sink = geometry.Open())
        {
            sink.BeginFigure(from, false);

            // Out of the output heading right, and round until it is heading left
            // along the run, directly beneath or above where it started.
            var leaving = Bend(from.Y, run);

            sink.CubicBezierTo(
                new Point(from.X + leaving, from.Y),
                new Point(from.X + leaving, run),
                new Point(from.X, run));

            sink.LineTo(new Point(to.X, run));

            // And round the other way, into the input heading right.
            var arriving = Bend(run, to.Y);

            sink.CubicBezierTo(
                new Point(to.X - arriving, run),
                new Point(to.X - arriving, to.Y),
                to);

            sink.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    /// <summary>
    /// How far a U-bend's handles reach out sideways, for a bend that turns across
    /// the given heights.
    /// </summary>
    /// <remarks>
    /// Three quarters of the height is what makes a cubic with both handles level
    /// with its ends close to a half circle — it bulges out by about half the
    /// height. Bounded both ways: a short turn still wants a curve rather than a
    /// kink, and a long one through a wide gap would otherwise swing further out
    /// than any other wire on the canvas.
    /// </remarks>
    private static double Bend(double from, double to) =>
        Math.Clamp(Math.Abs(to - from) * 0.75, ReturnWireReach / 3, ReturnWireReach);
}
