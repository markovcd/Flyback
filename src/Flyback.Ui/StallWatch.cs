using System.Diagnostics;
using Avalonia.Threading;

namespace Flyback.Ui;

/// <summary>Notices when the UI thread does not come back for <see cref="StallTrace.Threshold"/>, and says so to <see cref="StallTrace"/>.</summary>
internal sealed class StallWatch : IDisposable
{
    private static readonly TimeSpan Beat = TimeSpan.FromMilliseconds(20);

    private readonly DispatcherTimer timer = new(DispatcherPriority.Normal) { Interval = Beat };
    private long last = Stopwatch.GetTimestamp();

    public StallWatch()
    {
        timer.Tick += (_, _) => Tick();
        timer.Start();
    }

    private void Tick()
    {
        var now = Stopwatch.GetTimestamp();
        var gap = Stopwatch.GetElapsedTime(last, now);

        if (gap >= StallTrace.Threshold) StallTrace.UiStalled(gap, last);

        last = now;
    }

    public void Dispose() => timer.Stop();
}
