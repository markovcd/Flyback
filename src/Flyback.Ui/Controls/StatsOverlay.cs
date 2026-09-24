using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Flyback.App.Controls;

/// <summary>
/// A line in the corner of a full-screen picture saying how it is being drawn: frames a
/// second, what a frame costs, the patch's ops, the size, the renderer and the clock.
/// </summary>
/// <remarks>
/// Hidden until asked for, and read only while shown, so a picture nobody is measuring
/// pays nothing for it. Never takes a click, so it cannot come between the pointer and
/// the knobs or the transport.
/// </remarks>
public sealed class StatsOverlay : Border
{
    /// <summary>How often the line is read again while it is shown.</summary>
    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(250);

    private readonly PreviewHost preview;
    private readonly TextBlock line;
    private readonly DispatcherTimer ticker;

    public StatsOverlay(PreviewHost preview)
    {
        this.preview = preview;

        Name = "stats";
        IsVisible = false;
        IsHitTestVisible = false;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(12);
        Padding = new Thickness(8, 4);
        CornerRadius = new CornerRadius(4);
        Background = new SolidColorBrush(Avalonia.Media.Colors.Black, 0.6);

        Child = line = new TextBlock
        {
            FontSize = Text.Body,
            FontFamily = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace"),
            Foreground = Brushes.White,
        };

        ticker = new DispatcherTimer(DispatcherPriority.Background) { Interval = Every };
        ticker.Tick += (_, _) => Update();

        PropertyChanged += (_, e) =>
        {
            if (e.Property != IsVisibleProperty) return;

            if (IsVisible)
            {
                Update();
                ticker.Start();
            }
            else
            {
                ticker.Stop();
            }
        };
    }

    /// <summary>What the line says now.</summary>
    public string Said => line.Text ?? string.Empty;

    /// <summary>Shows the line, or puts it away.</summary>
    public void Toggle() => IsVisible = !IsVisible;

    /// <summary>Reads the picture again.</summary>
    public void Update() =>
        line.Text = Line(
            preview.FramesPerSecond,
            preview.FrameMilliseconds,
            preview.Program.Ops.Length,
            preview.Resolution,
            preview.Backend,
            preview.Time);

    /// <summary>The line, from what was measured.</summary>
    public static string Line(double fps, double milliseconds, int ops, PixelSize size, PreviewBackend backend, double seconds) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{fps:0} fps · {milliseconds:0.0} ms · {ops} ops · {size.Width}×{size.Height} · {(backend == PreviewBackend.Gpu ? "GPU" : "CPU")} · t {StatusClock.Text(seconds)}");
}
