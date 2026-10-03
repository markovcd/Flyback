using Avalonia;
using Avalonia.Media;
using Flyback.Core.Graph;

namespace Flyback.Editor.Canvas;

/// <summary>
/// What a module or a shut box looks like while it is carried: drawn a little up and
/// left of where it sits, with a soft shadow under it.
/// </summary>
/// <remarks>
/// One rule for both, so they can never be lifted differently. A module behind a box
/// is lifted when the box is, and its wire ends are drawn with it.
/// </remarks>
internal sealed class CanvasLift(CanvasGestures gestures, CanvasSelection selection)
{
    /// <summary>How far what is lifted is drawn from where it sits.</summary>
    private static readonly Vector Offset = new(-2, -3);

    private const double ShadowStrength = 0.42;

    private static readonly BoxShadows Shadow = ShadowOf(ShadowStrength);

    private static readonly BoxShadows ShadowOff = ShadowOf(ShadowStrength * CanvasPainter.OffOpacity);

    private static BoxShadows ShadowOf(double strength) => new(new BoxShadow
    {
        OffsetX = 1,
        OffsetY = 5,
        Blur = 12,
        Color = Color.FromArgb((byte)Math.Round(255 * strength), 0, 0, 0),
    });

    /// <summary>Whether a module is held up: picked itself, or standing behind a box that is.</summary>
    public bool Raised(Guid id) =>
        gestures.Carrying
        && (selection.Scene.ShutGroupOf(id) is { } box ? Raised(box) : selection.Contains(id));

    /// <summary>Whether a box is held up: all of what is inside it is picked.</summary>
    public bool Raised(NodeGroup box) =>
        gestures.Carrying && selection.Holds(box);

    /// <summary>Where a wire end on a module is drawn: with the socket, which moves with what it is on.</summary>
    public Point Held(Guid id, Point anchor) => Raised(id) ? anchor + Offset : anchor;

    /// <summary>
    /// Sets up drawing something at <paramref name="bounds"/>: its shadow and offset where
    /// <paramref name="raised"/>, and its dimming where <paramref name="off"/>. Everything
    /// drawn until the result is disposed is drawn that way.
    /// </summary>
    public static Scope Hold(DrawingContext context, Rect bounds, bool off, bool raised)
    {
        DrawingContext.PushedState? lift = null;

        if (raised)
        {
            context.DrawRectangle(null, null, new RoundedRect(bounds, NodeGeometry.CornerRadius), off ? ShadowOff : Shadow);
            lift = context.PushTransform(Matrix.CreateTranslation(Offset));
        }

        return new Scope(lift, off ? context.PushOpacity(CanvasPainter.OffOpacity) : null);
    }

    /// <summary>What <see cref="Hold"/> pushed, popped in the opposite order.</summary>
    public readonly struct Scope(DrawingContext.PushedState? lift, DrawingContext.PushedState? dim) : IDisposable
    {
        public void Dispose()
        {
            dim?.Dispose();
            lift?.Dispose();
        }
    }
}
