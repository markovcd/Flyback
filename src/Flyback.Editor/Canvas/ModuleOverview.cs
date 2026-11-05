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

    /// <summary>Names laid out at a zoom, kept while the zoom holds, as it does through a pan.</summary>
    private static readonly Remembered<(string Name, double Size, IBrush Brush, double Room, int Lines), FormattedText> Names = new(1024);

    private static FormattedText Lay(string name, double size, IBrush brush, double room, int lines) =>
        new(name, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush)
        {
            MaxTextWidth = room,
            MaxLineCount = lines,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
        };

    /// <summary>How large the glyph over the name is on the screen, whatever the zoom.</summary>
    private const double GlyphSize = 18;

    /// <summary>The gap between the glyph and the name under it, on the screen.</summary>
    private const double GlyphGap = 3;

    /// <summary>
    /// The name of a module or a box, centered on <paramref name="bounds"/> on a dark plate with its
    /// <paramref name="glyph"/> over it where it has one, and struck through when it is switched off.
    /// </summary>
    internal static void DrawName(
        DrawingContext context, Rect bounds, string name, double zoom, IBrush brush, bool off, Geometry? glyph, IPen? pen)
    {
        var size = TextSize / zoom;
        var room = bounds.Width * (1 + Spill * 2);
        var below = bounds.Y + NodeGeometry.HeaderHeight;
        var space = bounds.Bottom - below;

        // The glyph gives way before the name does: it is drawn only where both fit.
        var mark = glyph is not null && pen is not null ? Math.Min(GlyphSize / zoom, space * 0.4) : 0;
        var gap = mark > 0 ? GlyphGap / zoom : 0;
        var lines = Math.Clamp((int)((space - mark - gap) / (size * 1.3)), 1, MostLines);

        var text = Names.Get((name, size, brush, room, lines), static key => Lay(key.Name, key.Size, key.Brush, key.Room, key.Lines));

        var top = below + (space - mark - gap - text.Height) / 2;
        var centerX = bounds.Center.X;

        if (mark > 0) NodeSkin.DrawGlyph(context, glyph!, new Point(centerX - mark / 2, top), mark, pen!);

        var textTop = top + mark + gap;
        var pad = 3 / zoom;
        var plate = new Rect(centerX - text.Width / 2 - pad, textTop - pad / 2, text.Width + pad * 2, text.Height + pad);

        context.DrawRectangle(Plate, null, plate, pad, pad);
        CanvasText.Draw(context, text, new Point(centerX - room / 2, textTop));

        if (off)
            context.DrawLine(
                new Pen(brush, 1.5 / zoom),
                new Point(plate.Left + pad, textTop + text.Height / 2),
                new Point(plate.Right - pad, textTop + text.Height / 2));
    }
}
