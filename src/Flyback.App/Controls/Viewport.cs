using Avalonia;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// Where the canvas is looking: its zoom and pan, the size of the control it fills,
/// and turning a point between the screen and the patch.
/// </summary>
/// <remarks>
/// Pan and zoom are one matrix pushed around the whole render, and every pan goes
/// through <see cref="PanTo"/>, which holds the view on the canvas.
/// </remarks>
internal sealed class Viewport
{
    /// <summary>
    /// How far past the canvas the view may be scrolled, in graph units: a strip of
    /// the ground beyond, so the edge reads as an edge with something on the far
    /// side rather than as the window's own frame.
    /// </summary>
    internal const double ViewMargin = 160;

    /// <summary>
    /// How far out the view may zoom, whether by the wheel or by framing. At this much
    /// the whole canvas fits a window about two thousand pixels wide.
    /// </summary>
    internal const double MinZoom = 0.13;

    private const double MaxZoom = 3.0;

    /// <summary>How far in framing may zoom, which is less than the wheel may.</summary>
    private const double MaxFrameZoom = 1.4;

    /// <summary>How far from the origin the view may see, to either side.</summary>
    internal const double ViewReachAcross = NodeInstance.Across + ViewMargin;

    /// <summary>The same going down, since the canvas is wider than it is tall.</summary>
    internal const double ViewReachDown = NodeInstance.Down + ViewMargin;

    /// <summary>The canvas itself, in graph units: the ground a module may stand on.</summary>
    internal static readonly Rect CanvasBounds = new(
        -NodeInstance.Across,
        -NodeInstance.Down,
        NodeInstance.Across * 2,
        NodeInstance.Down * 2);

    private static readonly Point Home = new(40, 40);

    private readonly CanvasHistory history;
    private readonly CanvasSelection selection;
    private readonly Repaint repaint;

    public Viewport(CanvasHistory history, CanvasSelection selection, Repaint repaint)
    {
        this.history = history;
        this.selection = selection;
        this.repaint = repaint;

        // A patch opened, or rebuilt by the assistant, is looked at whole; a step
        // through the history keeps the view where it was.
        history.Replaced += (_, how) =>
        {
            if (how != Replacement.Restored) FrameAll();
        };
    }

    public double Zoom { get; private set; } = 1;

    public Point Pan { get; private set; } = Home;

    /// <summary>The size of the control the canvas fills.</summary>
    public Size Size { get; private set; }

    /// <summary>
    /// Whether a frame was asked for before there was a viewport to fit it into. A
    /// patch is usually loaded before the control has been measured.
    /// </summary>
    private bool framePending = true;

    public Matrix GraphToScreen => Matrix.CreateScale(Zoom, Zoom) * Matrix.CreateTranslation(Pan.X, Pan.Y);

    public Point ToGraph(Point screen) => new((screen.X - Pan.X) / Zoom, (screen.Y - Pan.Y) / Zoom);

    /// <summary>A rectangle of graph units, in the control's own coordinates.</summary>
    public Rect OnScreen(Rect graph) => graph.TransformToAABB(GraphToScreen);

    /// <summary>The middle of the view, in graph units: where anything added with no place of its own goes.</summary>
    public Point Middle => ToGraph(new Point(Size.Width / 2, Size.Height / 2));

    /// <summary>What the view shows, in graph units.</summary>
    public Rect Visible => new(ToGraph(default), ToGraph(new Point(Size.Width, Size.Height)));

    /// <summary>The control has a new size: framed if a frame is waiting, and held on the canvas otherwise.</summary>
    public void Resize(Size size)
    {
        Size = size;

        if (framePending)
        {
            FrameAll();
            return;
        }

        // A window pulled wider shows more canvas without the view having moved,
        // which is the one way to end up outside it without panning.
        PanTo(Pan);
        repaint.Request();
    }

    /// <summary>
    /// Fits every module into view, or defers until there is a viewport to fit into.
    /// </summary>
    public void FrameAll()
    {
        if (Size.Width < 1 || Size.Height < 1)
        {
            framePending = true;
            return;
        }

        framePending = false;

        if (history.Patch.Nodes.Count == 0)
        {
            Zoom = 1;
            PanTo(Home);
            repaint.Request();
            return;
        }

        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;

        foreach (var bounds in selection.Scene.OnCanvas())
        {
            left = Math.Min(left, bounds.Left);
            top = Math.Min(top, bounds.Top);
            right = Math.Max(right, bounds.Right);
            bottom = Math.Max(bottom, bounds.Bottom);
        }

        if (left > right) return;

        const double margin = 50;
        var scaleX = Size.Width / (right - left + margin * 2);
        var scaleY = Size.Height / (bottom - top + margin * 2);

        Zoom = Math.Clamp(Math.Min(scaleX, scaleY), MinZoom, MaxFrameZoom);

        // Centered on what it is framing, and then held inside the canvas, so a
        // patch built hard against an edge is pushed off center rather than being
        // centered over ground the view is not allowed to be on.
        PanTo(new Point(
            (Size.Width - (right - left) * Zoom) / 2 - left * Zoom,
            (Size.Height - (bottom - top) * Zoom) / 2 - top * Zoom));

        repaint.Request();
    }

    /// <summary>Zooms by <paramref name="notches"/> of the wheel, keeping the point under <paramref name="screen"/> where it is.</summary>
    public void ZoomAt(Point screen, double notches)
    {
        var anchor = ToGraph(screen);

        Zoom = Math.Clamp(Zoom * Math.Pow(1.12, notches), MinZoom, MaxZoom);

        // As far as the edge of the canvas allows, since zooming out in a corner
        // walks the view outwards as surely as dragging it does.
        PanTo(new Point(screen.X - anchor.X * Zoom, screen.Y - anchor.Y * Zoom));

        repaint.Request();
    }

    public void PanBy(Vector by)
    {
        PanTo(Pan + by);
        repaint.Request();
    }

    /// <summary>
    /// Moves the view, held so that it never leaves the canvas.
    /// </summary>
    /// <remarks>
    /// The reach is a little wider than <see cref="NodeInstance.Across"/> and
    /// <see cref="NodeInstance.Down"/>, because those hold a corner and the body hangs
    /// below and right of it. A view wider than the canvas is centered on it instead:
    /// past about two thousand pixels the whole canvas fits at the zoom's floor.
    /// </remarks>
    private void PanTo(Point to)
    {
        Pan = new Point(
            Held(to.X, Size.Width, ViewReachAcross),
            Held(to.Y, Size.Height, ViewReachDown));

        double Held(double offset, double viewport, double reach)
        {
            var edge = reach * Zoom;

            return viewport > edge * 2
                ? viewport / 2
                : Math.Clamp(offset, viewport - edge, edge);
        }
    }
}
