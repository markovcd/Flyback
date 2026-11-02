using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Flyback.Editor.Canvas;

/// <summary>
/// Whether the patch stands over the picture, and how see-through that leaves what
/// the canvas draws.
/// </summary>
internal sealed class CanvasGlass(SocketDial dial, SocketFocus focus)
{
    /// <summary>How opaque the fills, boxes and wires are drawn while the patch stands over the picture.</summary>
    internal double GlassOpacity { get; set; } = 0.78;

    /// <summary>Whether the patch is drawn over the picture: no ground, no grid, and the fills and wires see-through.</summary>
    internal bool SeeThrough { get; set; }

    /// <summary>Lets the picture through what is drawn next, while the patch stands over it.</summary>
    internal DrawingContext.PushedState Push(DrawingContext context) => context.PushOpacity(SeeThrough ? GlassOpacity : 1);

    /// <summary>
    /// Lets the picture through a module's fill, but not one whose value is in hand,
    /// whose fill stays solid so its text reads against it.
    /// </summary>
    internal double FillOpacity(Guid node) => SeeThrough && dial.Dialed?.Node != node && focus.Focused?.Node != node ? GlassOpacity : 1;

    /// <summary>Over a live picture, the dark casing under a wire.</summary>
    internal static readonly IBrush Casing = new ImmutableSolidColorBrush(Color.FromArgb(200, 0, 0, 0));

    /// <summary>Over a live picture, the dark line outside a module's edge.</summary>
    private static readonly IPen EdgeDark = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(220, 0, 0, 0)), 1.5);

    /// <summary>Over a live picture, the light line inside a module's edge.</summary>
    private static readonly IPen EdgeLight = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1);

    /// <summary>
    /// Over a live picture, a dark line just outside <paramref name="bounds"/> and a light one
    /// just inside, so a module stands out against a picture of any color, its own too.
    /// </summary>
    internal void DrawEdge(DrawingContext context, Rect bounds)
    {
        if (!SeeThrough) return;

        context.DrawRectangle(null, EdgeDark, new RoundedRect(bounds.Inflate(1), NodeGeometry.CornerRadius + 1));
        context.DrawRectangle(null, EdgeLight, new RoundedRect(bounds.Deflate(1), Math.Max(0, NodeGeometry.CornerRadius - 1)));
    }
}
