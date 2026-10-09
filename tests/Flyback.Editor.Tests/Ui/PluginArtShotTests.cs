using System.Globalization;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Xunit;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Tests.Ui;

/// <summary>
/// Draws the preview each shipped plugin with no modules embeds: the sound and MIDI
/// plugins, the secret stores and the assistants, which have nothing on the canvas
/// to photograph.
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

    /// <summary>One accent per system, shared by its sound plugin and its secret store.</summary>
    private static readonly Color Windows = Colors.Source, Mac = Colors.Space, Linux = Colors.Sequencer;

    private static readonly (string Name, Action<DrawingContext> Draw)[] Pictures =
    [
        ("winio", dc => SoundAndMidi(dc, Windows, "WASAPI  ·  Windows MIDI")),
        ("macio", dc => SoundAndMidi(dc, Mac, "Core Audio  ·  Core MIDI")),
        ("linuxio", dc => SoundAndMidi(dc, Linux, "ALSA  ·  PipeWire  ·  PulseAudio")),
        ("dpapi", dc => SecretStore(dc, Windows, "DPAPI  ·  the Windows sign-in")),
        ("keychain", dc => SecretStore(dc, Mac, "the login keychain")),
        ("keyring", dc => SecretStore(dc, Linux, "Secret Service  ·  GNOME Keyring  ·  KWallet")),
        ("gemini", dc => Assistant(dc, Colors.Feedback, sees: true, "Gemini  ·  sees the picture, hears the sound")),
        ("openai", dc => Assistant(dc, Colors.Form, sees: false, "any chat-completions endpoint")),
        ("claudecode", dc => Cli(dc, Colors.Shaping, spark: true, "Claude Code  ·  already signed in, no API key")),
        ("codex", dc => Cli(dc, Colors.Oscillator, spark: false, "Codex  ·  already signed in, no API key")),
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

    // --- the three pictures --------------------------------------------------

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

    /// <summary>A padlock beside a key field whose characters are hidden.</summary>
    private static void SecretStore(DrawingContext dc, Color accent, string caption)
    {
        Padlock(dc, new Point(190, 176), accent);

        var field = new Rect(300, 150, 290, 52);

        dc.DrawText(Text("API key", 14, Colors.Muted), new Point(field.X + 2, field.Y - 26));
        dc.DrawRectangle(new SolidColorBrush(Colors.Panel), new Pen(new SolidColorBrush(Fade(accent, 0.7)), 2), field, 9, 9);

        var dots = new SolidColorBrush(Colors.Label);

        for (var i = 0; i < 11; i++)
            dc.DrawEllipse(dots, null, new Point(field.X + 26 + i * 22, field.Center.Y), 5.5, 5.5);

        dc.DrawLine(
            new Pen(new SolidColorBrush(accent), 2.5),
            new Point(field.X + 26 + 11 * 22 - 4, field.Y + 14),
            new Point(field.X + 26 + 11 * 22 - 4, field.Bottom - 14));

        Caption(dc, caption);
    }

    /// <summary>A speech bubble wiring up a small patch.</summary>
    private static void Assistant(DrawingContext dc, Color accent, bool sees, string caption)
    {
        var bubble = new Rect(36, 82, 236, 150);

        Bubble(dc, bubble, accent);

        var bars = new SolidColorBrush(Fade(Colors.Label, 0.45));

        if (sees)
        {
            Eye(dc, new Point(bubble.X + 50, bubble.Y + 44), accent);
            Waveform(dc, new Point(bubble.X + 92, bubble.Y + 44), 118, accent);
            dc.DrawRectangle(bars, null, new Rect(bubble.X + 26, bubble.Y + 84, 176, 10), 5, 5);
            dc.DrawRectangle(bars, null, new Rect(bubble.X + 26, bubble.Y + 108, 116, 10), 5, 5);
        }
        else
        {
            dc.DrawText(Text("›", 30, accent), new Point(bubble.X + 22, bubble.Y + 14));
            dc.DrawRectangle(new SolidColorBrush(accent), null, new Rect(bubble.X + 46, bubble.Y + 32, 150, 10), 5, 5);
            dc.DrawRectangle(bars, null, new Rect(bubble.X + 26, bubble.Y + 62, 184, 10), 5, 5);
            dc.DrawRectangle(bars, null, new Rect(bubble.X + 26, bubble.Y + 86, 160, 10), 5, 5);
            dc.DrawRectangle(bars, null, new Rect(bubble.X + 26, bubble.Y + 110, 104, 10), 5, 5);
        }

        Patch(dc, bubble, accent);

        Caption(dc, caption);
    }

    /// <summary>A terminal wiring up a small patch.</summary>
    private static void Cli(DrawingContext dc, Color accent, bool spark, string caption)
    {
        var terminal = new Rect(36, 82, 236, 150);

        Terminal(dc, terminal, accent, spark);
        Patch(dc, terminal, accent);
        Caption(dc, caption);
    }

    /// <summary>A sine and a filter into the output, the first two fed by wires out of <paramref name="source"/>.</summary>
    private static void Patch(DrawingContext dc, Rect source, Color accent)
    {
        var sine = new Rect(336, 48, 104, 72);
        var filter = new Rect(336, 196, 104, 72);
        var output = new Rect(512, 118, 104, 92);
        var grey = Colors.Value;

        Wire(dc, new Point(source.Right, source.Y + 44), new Point(sine.X, sine.Y + 50), accent);
        Wire(dc, new Point(source.Right, source.Y + 108), new Point(filter.X, filter.Y + 50), accent);
        Wire(dc, new Point(sine.Right, sine.Y + 50), new Point(output.X, output.Y + 50), grey);
        Wire(dc, new Point(filter.Right, filter.Y + 50), new Point(output.X, output.Y + 70), grey);

        Block(dc, sine, "Sine", Colors.Oscillator, inputs: [50], outputs: [50]);
        Block(dc, filter, "Filter", Colors.Shaping, inputs: [50], outputs: [50]);
        Block(dc, output, "Output", Colors.Sink, inputs: [50, 70], outputs: []);

        Socket(dc, new Point(source.Right, source.Y + 44), accent);
        Socket(dc, new Point(source.Right, source.Y + 108), accent);
    }

    // --- the parts -------------------------------------------------------------

    /// <summary>The canvas, with its grid.</summary>
    private static void Backdrop(DrawingContext dc)
    {
        dc.FillRectangle(new SolidColorBrush(Colors.Canvas), new Rect(0, 0, Wide, Tall));

        var minor = new Pen(new SolidColorBrush(Colors.Grid), 0.75);
        var major = new Pen(new SolidColorBrush(Colors.GridMajor), 1);

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

    /// <summary>A padlock in the accent, shut.</summary>
    private static void Padlock(DrawingContext dc, Point center, Color accent)
    {
        var shackle = new StreamGeometry();

        using (var c = shackle.Open())
        {
            c.BeginFigure(new Point(center.X - 42, center.Y - 20), false);
            c.LineTo(new Point(center.X - 42, center.Y - 50));
            c.ArcTo(new Point(center.X + 42, center.Y - 50), new Size(42, 42), 0, false, SweepDirection.Clockwise);
            c.LineTo(new Point(center.X + 42, center.Y - 20));
            c.EndFigure(false);
        }

        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Colors.Edge), 22, lineCap: PenLineCap.Flat), shackle);
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Colors.Value), 16, lineCap: PenLineCap.Flat), shackle);

        var body = new Rect(center.X - 70, center.Y - 30, 140, 112);
        var shade = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(accent, 0), new GradientStop(Darken(accent, 0.55), 1) },
        };

        dc.DrawRectangle(shade, new Pen(new SolidColorBrush(Colors.Edge), 2.5), body, 16, 16);

        var hole = new StreamGeometry();

        using (var c = hole.Open())
        {
            c.BeginFigure(new Point(center.X - 6, center.Y + 16), true);
            c.LineTo(new Point(center.X - 11, center.Y + 52));
            c.LineTo(new Point(center.X + 11, center.Y + 52));
            c.LineTo(new Point(center.X + 6, center.Y + 16));
            c.EndFigure(true);
        }

        var ink = new SolidColorBrush(Colors.Edge);

        dc.DrawEllipse(ink, null, new Point(center.X, center.Y + 14), 14, 14);
        dc.DrawGeometry(ink, null, hole);
    }

    /// <summary>A rounded bubble with its tail at the bottom left.</summary>
    private static void Bubble(DrawingContext dc, Rect bubble, Color accent)
    {
        var tail = new StreamGeometry();

        using (var c = tail.Open())
        {
            c.BeginFigure(new Point(bubble.X + 34, bubble.Bottom - 2), true);
            c.LineTo(new Point(bubble.X + 26, bubble.Bottom + 30));
            c.LineTo(new Point(bubble.X + 72, bubble.Bottom - 2));
            c.EndFigure(true);
        }

        var fill = new SolidColorBrush(Colors.Panel);
        var pen = new Pen(new SolidColorBrush(accent), 2.5, lineJoin: PenLineJoin.Round);

        dc.DrawGeometry(fill, pen, tail);
        dc.DrawRectangle(fill, pen, bubble, 20, 20);
        dc.FillRectangle(fill, new Rect(bubble.X + 36, bubble.Bottom - 4, 34, 6));
    }

    /// <summary>A terminal window with a prompt typed into it.</summary>
    private static void Terminal(DrawingContext dc, Rect window, Color accent, bool spark)
    {
        var pen = new Pen(new SolidColorBrush(accent), 2.5, lineJoin: PenLineJoin.Round);
        var bars = new SolidColorBrush(Fade(Colors.Label, 0.45));

        dc.DrawRectangle(new SolidColorBrush(Colors.Panel), pen, window, 16, 16);
        dc.DrawLine(new Pen(new SolidColorBrush(Colors.Separator), 2), new Point(window.X + 2, window.Y + 32), new Point(window.Right - 2, window.Y + 32));

        for (var i = 0; i < 3; i++)
            dc.DrawEllipse(new SolidColorBrush(Colors.Inactive), null, new Point(window.X + 22 + i * 18, window.Y + 17), 4.5, 4.5);

        if (spark)
        {
            Spark(dc, new Point(window.X + 34, window.Y + 62), accent);
            dc.DrawRectangle(new SolidColorBrush(accent), null, new Rect(window.X + 60, window.Y + 57, 128, 10), 5, 5);
            dc.DrawRectangle(bars, null, new Rect(window.X + 26, window.Y + 92, 176, 10), 5, 5);
            dc.DrawRectangle(bars, null, new Rect(window.X + 26, window.Y + 116, 120, 10), 5, 5);
        }
        else
        {
            dc.DrawText(Text(">_", 24, accent), new Point(window.X + 20, window.Y + 46));
            dc.DrawRectangle(new SolidColorBrush(accent), null, new Rect(window.X + 62, window.Y + 57, 110, 10), 5, 5);
            dc.DrawRectangle(bars, null, new Rect(window.X + 26, window.Y + 88, 184, 10), 5, 5);
            dc.DrawRectangle(bars, null, new Rect(window.X + 26, window.Y + 108, 150, 10), 5, 5);
            dc.DrawRectangle(bars, null, new Rect(window.X + 26, window.Y + 128, 96, 10), 5, 5);
        }
    }

    /// <summary>A four-pointed star, plain.</summary>
    private static void Spark(DrawingContext dc, Point center, Color accent)
    {
        var star = new StreamGeometry();

        using (var c = star.Open())
        {
            for (var i = 0; i < 8; i++)
            {
                var angle = i * Math.PI / 4 - Math.PI / 2;
                var radius = i % 2 == 0 ? 16 : 5;
                var point = new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));

                if (i == 0) c.BeginFigure(point, true);
                else c.LineTo(point);
            }

            c.EndFigure(true);
        }

        dc.DrawGeometry(new SolidColorBrush(accent), null, star);
    }

    /// <summary>An open eye.</summary>
    private static void Eye(DrawingContext dc, Point center, Color accent)
    {
        var lid = new StreamGeometry();

        using (var c = lid.Open())
        {
            c.BeginFigure(new Point(center.X - 26, center.Y), true);
            c.QuadraticBezierTo(new Point(center.X, center.Y - 30), new Point(center.X + 26, center.Y));
            c.QuadraticBezierTo(new Point(center.X, center.Y + 30), new Point(center.X - 26, center.Y));
            c.EndFigure(true);
        }

        dc.DrawGeometry(null, new Pen(new SolidColorBrush(accent), 3, lineJoin: PenLineJoin.Round), lid);
        dc.DrawEllipse(new SolidColorBrush(accent), null, center, 8, 8);
    }

    /// <summary>A decaying tone, left to right.</summary>
    private static void Waveform(DrawingContext dc, Point start, double width, Color accent)
    {
        var wave = new StreamGeometry();

        using (var c = wave.Open())
        {
            c.BeginFigure(start, false);

            for (var x = 1; x <= width; x++)
            {
                var t = x / width;
                c.LineTo(new Point(start.X + x, start.Y - 18 * (1 - t) * Math.Sin(t * Math.PI * 7)));
            }

            c.EndFigure(false);
        }

        dc.DrawGeometry(null, new Pen(new SolidColorBrush(accent), 3, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), wave);
    }

    /// <summary>A module the way the canvas draws one, in miniature.</summary>
    private static void Block(DrawingContext dc, Rect block, string title, Color accent, double[] inputs, double[] outputs)
    {
        var edge = new Pen(new SolidColorBrush(Colors.Edge), 2);

        var band = new SolidColorBrush(accent);

        dc.DrawRectangle(new SolidColorBrush(Colors.Node), null, block, 7, 7);
        dc.FillRectangle(band, new Rect(block.X, block.Y, block.Width, 24), 7);
        dc.FillRectangle(band, new Rect(block.X, block.Y + 12, block.Width, 12));
        dc.DrawRectangle(null, edge, block, 7, 7);

        dc.DrawText(Text(title, 13, Avalonia.Media.Colors.White), new Point(block.X + 10, block.Y + 4));

        var bars = new SolidColorBrush(Fade(Colors.Label, 0.35));

        foreach (var y in inputs)
        {
            dc.DrawRectangle(bars, null, new Rect(block.X + 14, block.Y + y - 3, 40, 6), 3, 3);
            Socket(dc, new Point(block.X, block.Y + y), Colors.Label);
        }

        foreach (var y in outputs)
        {
            dc.DrawRectangle(bars, null, new Rect(block.Right - 44, block.Y + y - 3, 30, 6), 3, 3);
            Socket(dc, new Point(block.Right, block.Y + y), Colors.Label);
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

    private static Color Darken(Color color, double by) =>
        Color.FromRgb((byte)(color.R * (1 - by)), (byte)(color.G * (1 - by)), (byte)(color.B * (1 - by)));
}
