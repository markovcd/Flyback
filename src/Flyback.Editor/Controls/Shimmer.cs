using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Flyback.App.Controls;

/// <summary>
/// A word with a light sweeping through it, shown while something is being waited
/// on, and only once the wait has gone on long enough to be noticed.
/// </summary>
internal sealed class Shimmer : TextBlock
{
    /// <summary>How long a wait goes unremarked. Most are over well before it.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(150);

    /// <summary>How long the light takes to cross the word once.</summary>
    private static readonly TimeSpan Sweep = TimeSpan.FromSeconds(1.4);

    /// <summary>How wide the light is, against the word.</summary>
    private const double Reach = 0.35;

    /// <summary>The status line's own amber, with the light taken out of it.</summary>
    private static readonly Color Dim = Colors.Shade(Colors.Attention, 0.5);

    private readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly LinearGradientBrush light;
    private Func<bool>? waiting;
    private TimeSpan since;

    public Shimmer(string text)
    {
        Text = text;
        FontSize = Controls.Text.Body;
        VerticalAlignment = VerticalAlignment.Center;
        IsVisible = false;

        // Sliding the brush rather than its stops: padded, everything outside the
        // light is the dim end, however far off the word the light has gone.
        light = new LinearGradientBrush
        {
            SpreadMethod = GradientSpreadMethod.Pad,
            GradientStops =
            {
                new GradientStop(Dim, 0),
                new GradientStop(Colors.Attention, 0.5),
                new GradientStop(Dim, 1),
            },
        };

        Foreground = light;
        timer.Tick += (_, _) => Tick();
    }

    /// <summary>The clock the delay and the sweep are measured on.</summary>
    internal Func<TimeSpan> Now { get; init; } = () => Clock.Elapsed;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    /// <summary>Shows the word, after <see cref="Delay"/>, for as long as <paramref name="busy"/> says so.</summary>
    public void Watch(Func<bool> busy)
    {
        waiting = busy;
        since = Now();

        Tick();
        if (waiting is not null) timer.Start();
    }

    internal void Tick()
    {
        if (waiting?.Invoke() != true)
        {
            waiting = null;
            IsVisible = false;
            timer.Stop();
            return;
        }

        var elapsed = Now() - since;
        IsVisible = elapsed >= Delay;

        // From just off the left edge to just off the right, then round again.
        var at = elapsed.TotalSeconds / Sweep.TotalSeconds % 1 * (1 + 2 * Reach) - Reach;

        light.StartPoint = new RelativePoint(at - Reach, 0.5, RelativeUnit.Relative);
        light.EndPoint = new RelativePoint(at + Reach, 0.5, RelativeUnit.Relative);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }
}
