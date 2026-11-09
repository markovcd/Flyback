using System.Globalization;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Xunit;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Tests.Canvas;

/// <summary>
/// Draws the preview each shipped plugin with no modules embeds: the sound and MIDI
/// plugins, which have nothing on the canvas to photograph.
/// </summary>
/// <remarks>
/// Run by hand, with SHOT_DIR naming somewhere to write to; skipped otherwise.
/// Each PNG becomes the plugin's <c>preview.webp</c> at quality 88. Sixteen by nine
/// and bold, because the plugins window shows it at 128 by 72.
/// </remarks>
public class PluginArtShotTests : EditorTest
{
    private static string? Where => Environment.GetEnvironmentVariable("SHOT_DIR");

    /// <summary>Drawn on a 640 by 360 box at twice the pixels.</summary>
    private const double Wide = 640, Tall = 360;

    /// <summary>One accent per system.</summary>
    private static readonly Color Windows = Colors.Source, Mac = Colors.Space, Linux = Colors.Sequencer;

    private static readonly (string Name, Action<DrawingContext> Draw)[] Pictures =
    [
        ("winio", dc => SoundAndMidi(dc, Windows, "WASAPI  ·  Windows MIDI")),
        ("macio", dc => SoundAndMidi(dc, Mac, "Core Audio  ·  Core MIDI")),
        ("linuxio", dc => SoundAndMidi(dc, Linux, "ALSA  ·  PipeWire  ·  PulseAudio")),
    ];

    [AvaloniaFact]
    public void Draw_the_other_plugins_previews()
    {
        var folder = Where;
        Assert.SkipWhen(folder is null, "a tool for the site's pictures, run with SHOT_DIR naming a folder to draw into");

        Directory.CreateDirectory(folder);

        foreach (var (name, draw) in Pictures)
        {
            using var target = new RenderTargetBitmap(new PixelSize(1280, 720), new Vector(192, 192));

            using (var dc = target.CreateDrawingContext())
            {
                Backdrop(dc);
                draw(dc);
            }

            target.Save(Path.Combine(folder, "plugin-" + name + ".png"), new PngBitmapEncoderOptions());
        }
    }

    // --- the picture ----------------------------------------------------------

    /// <summary>A keyboard wired to a speaker.</summary>
    private static void SoundAndMidi(DrawingContext dc, Color accent, string caption)
    {
        Keyboard(dc, new Rect(56, 104, 168, 112), accent);

        var from = new Point(238, 190);
        var to = new Point(386, 132);

        Wire(dc, from, to, accent);
        Socket(dc, from, accent);
        Speaker(dc, new Point(474, 160), accent);
        Socket(dc, to, accent);

        Caption(dc, caption);
    }

    // --- the parts -------------------------------------------------------------

    /// <summary>The canvas, with its grid.</summary>
    private static void Backdrop(DrawingContext dc)
    {
        dc.FillRectangle(new SolidColorBrush(Colors.Canvas), new Rect(0, 0, Wide, Tall));

        var minor = new Pen(new SolidColorBrush(Colors.Grid), 0.75);
        var major = new Pen(new SolidColorBrush(Colors.GridMajor));

        for (var x = 0; x <= Wide; x += 20) dc.DrawLine(x % 100 == 0 ? major : minor, new Point(x, 0), new Point(x, Tall));
        for (var y = 0; y <= Tall; y += 20) dc.DrawLine(y % 100 == 0 ? major : minor, new Point(0, y), new Point(Wide, y));
    }

    private static void Caption(DrawingContext dc, string caption) =>
        dc.DrawText(Text(caption, 15, Colors.Muted), new Point(36, 300));

    /// <summary>An octave and a note, with a chord held down in the accent.</summary>
    private static void Keyboard(DrawingContext dc, Rect keys, Color accent)
    {
        const int whites = 8;
        var white = keys.Width / whites;
        var outline = new Pen(new SolidColorBrush(Colors.Edge), 2);
        var held = new SolidColorBrush(accent);
        var ivory = new SolidColorBrush(Colors.Label);

        dc.DrawRectangle(new SolidColorBrush(Colors.Node), outline, keys.Inflate(8), 8, 8);

        for (var i = 0; i < whites; i++)
            dc.DrawRectangle(i is 2 or 4 ? held : ivory, outline, new Rect(keys.X + i * white, keys.Y, white, keys.Height), 3, 3);

        var ebony = new SolidColorBrush(Colors.Edge);

        foreach (var i in new[] { 0, 1, 3, 4, 5, 7 })
        {
            if (i == whites - 1) continue;

            var black = new Rect(keys.X + (i + 1) * white - white * 0.3, keys.Y, white * 0.6, keys.Height * 0.6);

            dc.DrawRectangle(i == 0 ? held : ebony, outline, black, 2, 2);
        }
    }

    /// <summary>A cone in its box, and three rings of sound off its face.</summary>
    private static void Speaker(DrawingContext dc, Point center, Color accent)
    {
        var box = new Rect(center.X - 88, center.Y - 88, 176, 176);
        var edge = new Pen(new SolidColorBrush(Colors.Edge), 2);

        dc.DrawRectangle(new SolidColorBrush(Colors.Node), edge, box, 14, 14);
        dc.DrawEllipse(new SolidColorBrush(Colors.Edge), null, center, 70, 70);
        dc.DrawEllipse(new SolidColorBrush(Colors.GridMajor), null, center, 62, 62);
        dc.DrawEllipse(new SolidColorBrush(Colors.Grid), new Pen(new SolidColorBrush(Fade(accent, 0.8)), 3), center, 40, 40);
        dc.DrawEllipse(new SolidColorBrush(Colors.NodeSelected), edge, center, 18, 18);

        for (var ring = 0; ring < 3; ring++)
        {
            var radius = 104 + ring * 22;
            var sweep = 34 * Math.PI / 180;
            var start = new Point(center.X + radius * Math.Cos(-sweep), center.Y + radius * Math.Sin(-sweep));
            var end = new Point(center.X + radius * Math.Cos(sweep), center.Y + radius * Math.Sin(sweep));
            var arc = new StreamGeometry();

            using (var c = arc.Open())
            {
                c.BeginFigure(start, false);
                c.ArcTo(end, new Size(radius, radius), 0, false, SweepDirection.Clockwise);
                c.EndFigure(false);
            }

            var pen = new Pen(new SolidColorBrush(Fade(accent, 0.9 - ring * 0.28)), 5, lineCap: PenLineCap.Round);

            dc.DrawGeometry(null, pen, arc);
        }
    }

    /// <summary>A wire as the canvas draws one: level at both ends.</summary>
    private static void Wire(DrawingContext dc, Point from, Point to, Color color)
    {
        var reach = Math.Max(24, (to.X - from.X) * 0.5);
        var wire = new StreamGeometry();

        using (var c = wire.Open())
        {
            c.BeginFigure(from, false);
            c.CubicBezierTo(new Point(from.X + reach, from.Y), new Point(to.X - reach, to.Y), to);
            c.EndFigure(false);
        }

        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Fade(color, 0.25)), 10, lineCap: PenLineCap.Round), wire);
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(color), 4, lineCap: PenLineCap.Round), wire);
    }

    private static void Socket(DrawingContext dc, Point at, Color color) =>
        dc.DrawEllipse(new SolidColorBrush(color), new Pen(new SolidColorBrush(Colors.Outline), 2), at, 7, 7);

    private static FormattedText Text(string text, double size, Color color) => new(
        text,
        CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight,
        Typeface.Default,
        size,
        new SolidColorBrush(color));

    private static Color Fade(Color color, double alpha) =>
        Color.FromArgb((byte)(alpha * 255), color.R, color.G, color.B);
}
