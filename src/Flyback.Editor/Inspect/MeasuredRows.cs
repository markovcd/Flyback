using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Flyback.Engine.Measure;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Inspect;

/// <summary>
/// What the last measurement found, under an output's row: its numbers and,
/// for a color, its picture, turning between the window's start and its end.
/// </summary>
/// <param name="panel">The rows these go on, which is how the pictures know they are in a window.</param>
internal sealed class MeasuredRows(MeasureLabels measured, StackPanel panel)
{
    /// <summary>The measurement lines on the panel, dimmed in place by an edit rather than rebuilt under a hand on a knob.</summary>
    private readonly List<TextBlock> measurements = [];
    private readonly List<Control> measuredPictures = [];

    /// <summary>Dims every line and picture, for a patch edited since it was measured.</summary>
    public void Dim()
    {
        foreach (var line in measurements) line.Opacity = StaleMeasurement;
        foreach (var picture in measuredPictures) picture.Opacity = StaleMeasurement;
    }

    /// <summary>Forgets every row, for a panel about to be rebuilt.</summary>
    public void Clear()
    {
        measurements.Clear();
        measuredPictures.Clear();
        turning?.Stop();
        turning = null;
        turns = null;
        showingEnd = false;
    }

    /// <summary>What the last measurement found on one output: its numbers and, for a color, its picture.</summary>
    public IEnumerable<Control> Under(NodeInstance node, int port)
    {
        if (Measured(node, port) is { } line) yield return line;

        if (measured.Of(node.Id, port) is { } found && measured.Frame(found) is { } start)
            yield return Pictured(start, measured.Frame(found, 1) ?? start);
    }

    private const double StaleMeasurement = 0.45;

    /// <summary>How long each of a color's two pictures shows before the other.</summary>
    private static readonly TimeSpan PictureTurn = TimeSpan.FromSeconds(4);

    /// <summary>Turns every measured picture on the panel between the window's start and its end.</summary>
    private DispatcherTimer? turning;

    private bool showingEnd;

    /// <summary>A measured color's picture, turning between the start of the window and its end, with which it is under it.</summary>
    private Control Pictured(Avalonia.Media.Imaging.Bitmap start, Avalonia.Media.Imaging.Bitmap end)
    {
        var report = measured.Report!;
        var picture = MeasureLabels.Picture(start, 160);
        var when = new TextBlock { FontSize = Text.Caption, Foreground = Text.Muted };

        // While the pointer is over the picture it shows the end held down and the start otherwise;
        // elsewhere it turns with the rest.
        bool? held = null;

        void Show()
        {
            var atEnd = held ?? showingEnd;

            picture.Source = atEnd ? end : start;
            when.Text = atEnd
                ? $"at the end, {MeasurementWords.Number(report.From + report.Seconds)} s"
                : $"at the start, {MeasurementWords.Number(report.From)} s";
        }

        Show();

        picture.Name = "measuredPicture";
        picture.HorizontalAlignment = HorizontalAlignment.Left;

        picture.PointerEntered += (_, _) => { held = false; Show(); };
        picture.PointerExited += (_, e) => { if (e.Pointer.Captured != picture) { held = null; Show(); } };
        picture.PointerPressed += (_, e) =>
        {
            e.Pointer.Capture(picture);
            held = true;
            Show();
        };
        picture.PointerReleased += (_, e) =>
        {
            e.Pointer.Capture(null);
            held = picture.IsPointerOver ? false : null;
            Show();
        };
        picture.PointerCaptureLost += (_, _) =>
        {
            held = picture.IsPointerOver ? false : null;
            Show();
        };

        var block = new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(8, 0, 0, 4),
            Opacity = measured.Stale ? StaleMeasurement : 1,
            Children = { picture, when },
        };

        ToolTip.SetTip(block, "The picture on the grid it was measured on, at the start of the window and at its end in turn. Hold the mouse down on it to see the end.");
        measuredPictures.Add(block);
        turns += Show;

        if (turning is null)
        {
            turning = new DispatcherTimer { Interval = PictureTurn };
            turning.Tick += (_, _) => Turn();
            turning.Start();
        }

        return block;
    }

    /// <summary>What each picture on the panel does when <see cref="turning"/> ticks.</summary>
    private Action? turns;

    /// <summary>Shows the other of each measured color's two pictures; the timer's tick, and a test's.</summary>
    public void Turn()
    {
        // A panel out of its window has nobody to turn them for.
        if (TopLevel.GetTopLevel(panel) is null)
        {
            turning?.Stop();
            return;
        }

        showingEnd = !showingEnd;
        turns?.Invoke();
    }

    /// <summary>What the last measurement found on one output, both halves, under its row.</summary>
    private TextBlock? Measured(NodeInstance node, int port)
    {
        if (measured.Report is not { } report || measured.Of(node.Id, port) is not { } found) return null;

        var lines = MeasurementWords.Half("sound", found.Sound, report.Seconds)
            .Concat(MeasurementWords.Half("picture", found.Picture, report.Seconds, found.Frames is not null));

        var block = new TextBlock
        {
            Name = "measurement",
            Text = string.Join(Environment.NewLine, lines),
            FontSize = Text.Caption,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, -4, 0, 2),
            Opacity = measured.Stale ? StaleMeasurement : 1,
        };

        ToolTip.SetTip(block, MeasureLabels.Tip(found, report.Seconds, measured.Stale));
        measurements.Add(block);

        return block;
    }
}
