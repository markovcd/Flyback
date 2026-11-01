using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Canvas;

/// <summary>
/// How a module or a shut box is drawn when the view is zoomed too far out for its rows
/// to be read: its body and sockets as ever, and its name at a fixed size on the screen
/// across the middle of it in place of the rows.
/// </summary>
internal static class ModuleOverview
{
    /// <summary>The zoom below which modules are drawn this way, where a row's text is under seven pixels.</summary>
    internal const double Below = 0.6;

    /// <summary>How large the name is on the screen, whatever the zoom.</summary>
    internal const double TextSize = 11;

    /// <summary>How far the name may run past the body to either side, as a share of its width.</summary>
    private const double Spill = 0.15;

    /// <summary>The most lines a name is wrapped onto.</summary>
    private const int MostLines = 3;

    private static readonly IBrush Plate = new ImmutableSolidColorBrush(Colors.Canvas, 0.72);

    /// <summary>Whether the view at <paramref name="zoom"/> draws modules this way.</summary>
    internal static bool At(double zoom) => zoom < Below;

    /// <summary>
    /// The name of a module or a box, centered on <paramref name="bounds"/> on a dark plate so it
    /// reads over the body's mark, and struck through when it is switched off.
    /// </summary>
    internal static void DrawName(DrawingContext context, Rect bounds, string name, double zoom, IBrush brush, bool off)
    {
        var size = TextSize / zoom;
        var room = bounds.Width * (1 + Spill * 2);
        var lines = Math.Clamp((int)(bounds.Height / (size * 1.3)), 1, MostLines);

        var text = new FormattedText(name, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush)
        {
            MaxTextWidth = room,
            MaxLineCount = lines,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
        };

        var center = bounds.Center;
        var pad = 3 / zoom;
        var plate = new Rect(center.X - text.Width / 2 - pad, center.Y - text.Height / 2 - pad / 2, text.Width + pad * 2, text.Height + pad);

        context.DrawRectangle(Plate, null, plate, pad, pad);
        context.DrawText(text, new Point(center.X - room / 2, center.Y - text.Height / 2));

        if (off)
            context.DrawLine(
                new Pen(brush, 1.5 / zoom),
                new Point(plate.Left + pad, center.Y),
                new Point(plate.Right - pad, center.Y));
    }
}
