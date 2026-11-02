using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Canvas;

/// <summary>The sheet the patch is laid on: its ground, its grid and its edge.</summary>
internal sealed class GroundPainter(Viewport view)
{
    internal static readonly IBrush Background = new ImmutableSolidColorBrush(Colors.Canvas);
    private static readonly IBrush Beyond = new ImmutableSolidColorBrush(Colors.Edge);
    private static readonly IPen GridPenMajor = new ImmutablePen(new ImmutableSolidColorBrush(Colors.GridMajor));

    /// <summary>
    /// The edge of the canvas. Brighter than any grid line and a little heavier,
    /// because it is the one line out there that means something other than
    /// "this is where another forty-eight units went".
    /// </summary>
    private static readonly IPen EdgePen = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Separator), 2);

    /// <summary>
    /// Two grounds: the canvas is a sheet of finite size, and past it is whatever
    /// the bounded pan lets you see a little of, which is what makes the edge an
    /// edge rather than a stop to walk into.
    /// </summary>
    internal void DrawGround(DrawingContext context)
    {
        context.FillRectangle(Beyond, new Rect(view.Size));
        context.FillRectangle(Background, view.OnScreen(Viewport.CanvasBounds));
    }

    /// <summary>
    /// Rules the edge of the canvas.
    /// </summary>
    /// <remarks>
    /// Over the modules rather than under them, so that the one standing hard
    /// against an edge has the line across it and the rest of its body out on
    /// the far side. That is what the module's own bound looks like: a
    /// coordinate names a corner, and the body hangs off it.
    /// </remarks>
    internal void DrawEdge(DrawingContext context) =>
        context.DrawRectangle(null, EdgePen, view.OnScreen(Viewport.CanvasBounds));

    internal void DrawGrid(DrawingContext context)
    {
        // What is being looked at, and never more of it than there is canvas.
        // The grid is what says where a module would land, so ruling ground no
        // module may stand on would be a lie told in the one part of the view
        // that has nothing else in it to read.
        var visible = new Rect(
                view.ToGraph(new Point(0, 0)),
                view.ToGraph(new Point(view.Size.Width, view.Size.Height)))
            .Intersect(Viewport.CanvasBounds);

        // Zoomed in hard against an edge, the canvas can be off the view
        // entirely bar the line around it.
        if (visible.Width <= 0 || visible.Height <= 0) return;

        const double spacing = 48;

        // Zoomed far out the grid stops being useful and only costs draw calls.
        if (visible.Width / spacing > 400) return;

        // Rounded up rather than down: a line started just short of the canvas
        // would land off the control and invisible, rather than ruling the
        // ground past the edge.
        // Ruled like a scope's graticule: a line every fifth division, and the
        // divisions between as ticks across those lines.
        const double major = spacing * 5, tick = 4;

        var ticks = new StreamGeometry();

        using (var sink = ticks.Open())
        {
            for (var x = Math.Ceiling(visible.X / spacing) * spacing; x <= visible.Right; x += spacing)
            {
                if (OnMajor(x)) continue;

                for (var y = Math.Ceiling(visible.Y / major) * major; y <= visible.Bottom; y += major)
                    Line(sink, new Point(x, y - tick), new Point(x, y + tick));
            }

            for (var y = Math.Ceiling(visible.Y / spacing) * spacing; y <= visible.Bottom; y += spacing)
            {
                if (OnMajor(y)) continue;

                for (var x = Math.Ceiling(visible.X / major) * major; x <= visible.Right; x += major)
                    Line(sink, new Point(x - tick, y), new Point(x + tick, y));
            }
        }

        context.DrawGeometry(null, GridPenMajor, ticks);

        for (var x = Math.Ceiling(visible.X / major) * major; x <= visible.Right; x += major)
            context.DrawLine(GridPenMajor, new Point(x, visible.Y), new Point(x, visible.Bottom));

        for (var y = Math.Ceiling(visible.Y / major) * major; y <= visible.Bottom; y += major)
            context.DrawLine(GridPenMajor, new Point(visible.X, y), new Point(visible.Right, y));

        static bool OnMajor(double at) => Math.Abs(at % major) < 0.5;
    }

    private static void Line(StreamGeometryContext sink, Point from, Point to)
    {
        sink.BeginFigure(from, false);
        sink.LineTo(to);
        sink.EndFigure(false);
    }
}
