using Avalonia;
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
    private const double Gap = 9, PadX = 4, PadY = 1.5, Swatch = 8, MaxWidth = 140;

    private static readonly IBrush Ground = new ImmutableSolidColorBrush(Colors.Canvas, 0.9);
    private static readonly IBrush Ink = new ImmutableSolidColorBrush(Colors.Value);
    private static readonly IBrush Faint = new ImmutableSolidColorBrush(Colors.Value, 0.4);
    private static readonly IPen Ring = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Separator));

    /// <summary>What was last measured, or null before anything was.</summary>
    public MeasureReport? Report { get; private set; }

    /// <summary>Whether the patch has been edited since <see cref="Report"/> was taken.</summary>
    public bool Stale { get; private set; }

    /// <summary>Pins a fresh measurement, in place of whatever was pinned.</summary>
    public void Show(MeasureReport report)
    {
        Report = report;
        Stale = false;
        repaint.Request();
    }

    /// <summary>Takes every label down.</summary>
    public void Clear()
    {
        Report = null;
        Stale = false;
        repaint.Request();
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
        }

        return Task.CompletedTask;
    }

    /// <summary>What was measured on one output, or null where nothing was.</summary>
    public Measurement? Of(Guid node, int port) =>
        Report?.Measurements.FirstOrDefault(m => m.Node == node && m.Port == port);

    /// <summary>The label under <paramref name="graph"/> and the full report for its tip.</summary>
    public (Measurement Measured, string Tip)? Hit(Point graph)
    {
        if (Report is not { } report) return null;

        foreach (var (measured, area) in Labels())
            if (area.Contains(graph))
                return (measured, Tip(measured, report.Seconds, Stale));

        return null;
    }

    public void Draw(DrawingContext context)
    {
        if (Report is null) return;

        var ink = Stale ? Faint : Ink;

        foreach (var (measured, area) in Labels())
        {
            context.DrawRectangle(Ground, Ring, area, 3, 3);

            var x = area.X + PadX;
            var half = Pinned(measured);

            if (half.Count == 3)
            {
                var mean = Color.FromRgb(Byte(half[0].Mean), Byte(half[1].Mean), Byte(half[2].Mean));

                context.DrawRectangle(
                    new ImmutableSolidColorBrush(mean, Stale ? 0.4 : 1),
                    Ring,
                    new Rect(x, area.Center.Y - Swatch / 2, Swatch, Swatch));

                x += Swatch + PadX;
            }

            var text = Text(measured, ink);
            context.DrawText(text, new Point(x, area.Center.Y - text.Height / 2));
        }
    }

    /// <summary>Every label on the canvas and the box it is drawn in, in graph units.</summary>
    private IEnumerable<(Measurement Measured, Rect Area)> Labels()
    {
        var scene = selection.Scene;

        foreach (var measured in Report?.Measurements ?? [])
        {
            if (history.Patch.Find(measured.Node) is not { } node || scene.InPeek(node.Id)) continue;

            var anchor = scene.OutputAnchor(node, measured.Port);
            var text = Text(measured, Ink);
            var width = text.Width + PadX * 2 + (Pinned(measured).Count == 3 ? Swatch + PadX : 0);
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

    private static byte Byte(double v) => (byte)Math.Round(Math.Clamp(double.IsFinite(v) ? v : 0d, 0d, 1d) * 255d);
}
