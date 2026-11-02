using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Canvas;

/// <summary>
/// A ring round the input whose value is turning or has the keys, and its name beside it
/// on a plate, drawn in screen space for a view zoomed out past the rows that would
/// otherwise say which input it is.
/// </summary>
internal static class SocketTag
{
    /// <summary>The ring's radius on the screen.</summary>
    internal const double Radius = 10;

    private const double TextSize = 12;
    private const double Pad = 4;

    private static readonly IPen HotRing = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Attention), 2);
    private static readonly IPen RestRing = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF8)), 2);
    private static readonly IBrush Plate = new ImmutableSolidColorBrush(Colors.Canvas, 0.85);
    private static readonly IBrush Ink = new ImmutableSolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF8));

    /// <summary>Where the plate with the name goes for an input socket at <paramref name="socket"/>: to its left, off the module.</summary>
    internal static Rect PlateAt(Point socket, Size text) =>
        new(socket.X - Radius - Pad * 3 - text.Width, socket.Y - text.Height / 2 - Pad / 2, text.Width + Pad * 2, text.Height + Pad);

    /// <param name="socket">The input's socket, on the screen.</param>
    /// <param name="name">The input's name.</param>
    /// <param name="hot">Whether it is turning, which rings it in the turning color.</param>
    public static void Draw(DrawingContext context, Point socket, string name, bool hot)
    {
        context.DrawEllipse(null, hot ? HotRing : RestRing, socket, Radius, Radius);

        var text = CanvasText.Text(name, TextSize, Ink, 160, true);
        var plate = PlateAt(socket, new Size(text.Width, text.Height));

        context.DrawRectangle(Plate, hot ? HotRing : null, plate, Pad, Pad);
        context.DrawText(text, new Point(plate.X + Pad, plate.Y + Pad / 2));
    }
}
