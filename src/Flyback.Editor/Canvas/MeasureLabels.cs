using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Core.Graph;
using Flyback.Editor.Notices;
using Flyback.Engine.Measure;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Canvas;

/// <summary>
/// The last measurement, pinned beside each measured output socket: its value, or its
/// range and rate, dimmed once the patch has been edited since.
/// </summary>
/// <remarks>
/// The picture's half where it varies across the picture, since the sound's is then
/// one point of it, and the sound's otherwise. The hover gives both.
/// </remarks>
internal sealed class MeasureLabels(CanvasHistory history, CanvasSelection selection, Repaint repaint)
    : IReactTo<PatchChanged>
{
    private const double Gap = 9, PadX = 4, PadY = 1.5, Thumb = 10, MaxWidth = 140;

    private static readonly IBrush Ground = new ImmutableSolidColorBrush(Colors.Canvas, 0.9);
    private static readonly IBrush Ink = new ImmutableSolidColorBrush(Colors.Value);
    private static readonly IBrush Faint = new ImmutableSolidColorBrush(Colors.Value, 0.4);
    private static readonly IPen Ring = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Separator));

    /// <summary>Each color's picture, made the first time it is drawn.</summary>
    private readonly Dictionary<(Measurement Measured, int At), Bitmap> frames = [];

    /// <summary>Raised when labels are pinned, taken down or go out of date.</summary>
    public event Action? Changed;

    /// <summary>What was last measured, or null before anything was.</summary>
    public MeasureReport? Report { get; private set; }

    /// <summary>Whether the patch has been edited since <see cref="Report"/> was taken.</summary>
    public bool Stale { get; private set; }

    /// <summary>Pins a fresh measurement, in place of whatever was pinned.</summary>
    public void Show(MeasureReport report)
    {
        Report = report;
        Stale = false;
        frames.Clear();
        repaint.Request();
        Changed?.Invoke();
    }

    /// <summary>Takes every label down.</summary>
    public void Clear()
    {
        Report = null;
        Stale = false;
        frames.Clear();
        repaint.Request();
        Changed?.Invoke();
    }

    public Task On(PatchChanged notice)
    {
        if (Report is null) return Task.CompletedTask;

        // Another patch's numbers mean nothing here; this one's are only old.
        if (notice.Opened) Clear();
        else if (!Stale)
        {
            Stale = true;
            repaint.Request();
            Changed?.Invoke();
        }

        return Task.CompletedTask;
    }

    /// <summary>What was measured on one output, or null where nothing was.</summary>
    public Measurement? Of(Guid node, int port) =>
        Report?.Measurements.FirstOrDefault(m => m.Node == node && m.Port == port);

    /// <summary>The label under <paramref name="graph"/> and the full report for its tip, a color's picture above its numbers.</summary>
    public (Measurement Measured, object Tip)? Hit(Point graph)
    {
        if (Report is not { } report) return null;

        // Only the peek's own while one is up: everything else is under its scrim.
        foreach (var (measured, area) in Shown(peeked: selection.Scene.Peek is not null))
        {
            if (!area.Contains(graph)) continue;

            var said = Tip(measured, report.Seconds, Stale);

            if (Frame(measured) is not { } frame) return (measured, said);

            return (measured, new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    Picture(frame, 192),
                    new TextBlock { Text = said },
                },
            });
        }

        return null;
    }

    /// <summary>A color's picture at the start of the window, or at its end with <paramref name="at"/> 1; null for a number.</summary>
    public Bitmap? Frame(Measurement measured, int at = 0)
    {
        if (measured.Frames is not { } kept || at < 0 || at >= kept.Count) return null;
        if (Report is not { Columns: > 0, Rows: > 0 } report) return null;

        if (!frames.TryGetValue((measured, at), out var bitmap))
            frames[(measured, at)] = bitmap = MeasureFrames.Bitmap(kept[at], report.Columns, report.Rows);

        return bitmap;
    }

    /// <summary>A picture drawn <paramref name="width"/> wide in its own shape, its grid's pixels kept square-edged.</summary>
    public static Image Picture(Bitmap frame, double width)
    {
        var image = new Image
        {
            Source = frame,
            Width = width,
            Height = width * frame.PixelSize.Height / frame.PixelSize.Width,
            Stretch = Stretch.Fill,
        };

        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);
        return image;
    }

    /// <summary>The labels of what is drawn under a peek, or of the peek's own modules, which are drawn over it.</summary>
    public void Draw(DrawingContext context, bool peeked)
    {
        if (Report is null) return;

        var ink = Stale ? Faint : Ink;

        foreach (var (measured, area) in Shown(peeked))
        {
            context.DrawRectangle(Ground, Ring, area, 3, 3);

            var x = area.X + PadX;
            var half = Pinned(measured);

            if (Frame(measured) is { } frame)
            {
                var wide = ThumbWidth(frame);
                var spot = new Rect(x, area.Center.Y - Thumb / 2, wide, Thumb);

                using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
                using (context.PushOpacity(Stale ? 0.4 : 1))
                    context.DrawImage(frame, spot);

                context.DrawRectangle(null, Ring, spot);
                x += wide + PadX;
            }
            else if (half.Count == 3)
            {
                var mean = Color.FromRgb(Byte(half[0].Mean), Byte(half[1].Mean), Byte(half[2].Mean));

                context.DrawRectangle(
                    new ImmutableSolidColorBrush(mean, Stale ? 0.4 : 1),
                    Ring,
                    new Rect(x, area.Center.Y - Thumb / 2, Thumb, Thumb));

                x += Thumb + PadX;
            }

            var text = Text(measured, ink);
            context.DrawText(text, new Point(x, area.Center.Y - text.Height / 2));
        }
    }

    /// <summary>
    /// Every label shown and the box it is drawn in, in graph units: beside the box's
    /// socket for an output on a shut box's edge, and none for one inside it.
    /// </summary>
    internal IEnumerable<(Measurement Measured, Rect Area)> Shown(bool peeked)
    {
        var scene = selection.Scene;

        foreach (var measured in Report?.Measurements ?? [])
        {
            if (history.Patch.Find(measured.Node) is not { } node) continue;
            if (scene.InPeek(node.Id) != peeked || scene.Unseen(node.Id, measured.Port)) continue;

            var anchor = scene.OutputAnchor(node, measured.Port);
            var text = Text(measured, Ink);
            var picture = Frame(measured) is { } frame ? ThumbWidth(frame) + PadX : Pinned(measured).Count == 3 ? Thumb + PadX : 0;
            var width = text.Width + PadX * 2 + picture;
            var height = text.Height + PadY * 2;

            yield return (measured, new Rect(anchor.X + Gap, anchor.Y - height / 2, width, height));
        }
    }

    private static FormattedText Text(Measurement measured, IBrush ink) =>
        CanvasText.Text(MeasurementWords.Brief(Pinned(measured)), CanvasText.RowSize, ink, MaxWidth, true);

    private static IReadOnlyList<ComponentStats> Pinned(Measurement measured) =>
        measured.Picture.Any(c => c.Across) ? measured.Picture : measured.Sound;

    /// <summary>Both halves in full, as the hover shows them.</summary>
    internal static string Tip(Measurement measured, double seconds, bool stale)
    {
        var lines = new List<string> { $"{measured.Module}.{measured.Socket}" };

        lines.AddRange(MeasurementWords.Half("sound", measured.Sound, seconds));
        lines.AddRange(MeasurementWords.Half("picture", measured.Picture, seconds));

        if (measured.Differs) lines.Add("The sound and the picture differ.");
        if (stale) lines.Add("Out of date: the patch has changed since. Measure again (Ctrl+M).");

        return string.Join(Environment.NewLine, lines);
    }

    private static double ThumbWidth(Bitmap frame) => Thumb * frame.PixelSize.Width / frame.PixelSize.Height;

    private static byte Byte(double v) => (byte)Math.Round(Math.Clamp(double.IsFinite(v) ? v : 0d, 0d, 1d) * 255d);
}
