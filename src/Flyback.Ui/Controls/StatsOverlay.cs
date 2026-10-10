using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.Ui.Audio;

namespace Flyback.Ui.Controls;

/// <summary>
/// A line in the corner of a full-screen picture saying how it is being drawn: frames a
/// second, what a frame costs, the sound's oversampling, the size,
/// the renderer, the sound's backend and the clock.
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
    private readonly IAudioEngine sound;
    private readonly Func<bool> heard;
    private readonly TextBlock line;
    private readonly DispatcherTimer ticker;

    /// <param name="heard">Whether the patch has sound, without which the oversampling is left unsaid.</param>
    internal StatsOverlay(PreviewHost preview, IAudioEngine sound, Func<bool> heard)
    {
        this.preview = preview;
        this.sound = sound;
        this.heard = heard;

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
            heard() ? sound.Oversample : null,
            preview.Resolution,
            preview.Renderer,
            sound.Backend,
            preview.Time);

    /// <summary>The line, from what was measured.</summary>
    /// <param name="oversample">The sound's oversampling, or null for a patch with no sound.</param>
    /// <param name="renderer">What draws the picture, or null while a graphics context is still coming up.</param>
    /// <param name="backend">What the sound plays through, or null where nothing can play.</param>
    public static string Line(double fps, double milliseconds, int? oversample, PixelSize size, string? renderer, string? backend, double seconds) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{fps:0} fps · {milliseconds:0.0} ms · {(oversample is { } factor ? OversamplingText.Of(factor) + " · " : "")}{size.Width}×{size.Height} · {(renderer is null ? "" : renderer + " · ")}{(backend is null ? "" : backend + " · ")}t {StatusClock.Text(seconds)}");
}
