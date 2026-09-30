using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.App.Audio;

namespace Flyback.App.Controls;

/// <summary>
/// A line in the corner of a full-screen picture saying how it is being drawn: frames a
/// second, what a frame costs, the editor's picture and sound ops, the oversampling, the size,
/// the renderer and the clock.
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
    private readonly bool counted;
    private readonly TextBlock line;
    private readonly DispatcherTimer ticker;

    /// <param name="counted">Whether the line counts the picture's and the sound's ops, which only the editor's does.</param>
    internal StatsOverlay(PreviewHost preview, IAudioEngine sound, bool counted)
    {
        this.preview = preview;
        this.sound = sound;
        this.counted = counted;

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
            counted ? (preview.Program.Ops.Length, sound.Ops) : null,
            sound.Oversample,
            preview.Resolution,
            preview.Renderer,
            preview.Time);

    /// <summary>The line, from what was measured.</summary>
    /// <param name="ops">The picture's and the sound's ops, or null to leave them unsaid.</param>
    /// <param name="renderer">What draws the picture, or null while a graphics context is still coming up.</param>
    public static string Line(double fps, double milliseconds, (int Picture, int Sound)? ops, int oversample, PixelSize size, string? renderer, double seconds) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{fps:0} fps · {milliseconds:0.0} ms · {(ops is (var picture, var sound) ? $"{picture}/{sound} picture/sound ops · " : "")}{oversample}× oversampling · {size.Width}×{size.Height} · {(renderer is null ? "" : renderer + " · ")}t {StatusClock.Text(seconds)}");
}
