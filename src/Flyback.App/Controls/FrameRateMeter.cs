using System.Diagnostics;

namespace Flyback.App.Controls;

/// <summary>
/// Counts the frames a preview actually puts on screen and says how many that
/// comes to a second. Marked from whichever thread finishes a frame, read from
/// the UI thread.
/// </summary>
/// <remarks>
/// Counted rather than worked out from a frame's cost: a renderer skips ticks
/// while a frame is in flight, rests after an expensive one, is capped by the
/// preview frame rate and draws nothing while the clock is stopped, and none of
/// that shows in how long one frame took.
/// </remarks>
internal sealed class FrameRateMeter
{
    /// <summary>
    /// How long frames are counted before the rate is worked out again. Long
    /// enough that the number can be read, short enough to follow a change.
    /// </summary>
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(500);

    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Lock gate = new();

    private TimeSpan windowStart;
    private int frames;
    private double rate;

    /// <summary>Frames a second over the last complete window.</summary>
    /// <remarks>
    /// A preview that has stopped drawing never closes another window, so once a
    /// whole one has gone by without closing, the frames in the open one are what
    /// is reported — which falls to nought rather than holding the last rate.
    /// </remarks>
    public double PerSecond
    {
        get
        {
            lock (gate)
            {
                var open = clock.Elapsed - windowStart;
                return open > Window * 2 ? frames / open.TotalSeconds : rate;
            }
        }
    }

    /// <summary>One frame has reached the screen.</summary>
    public void Mark()
    {
        lock (gate)
        {
            frames++;

            var now = clock.Elapsed;
            var open = now - windowStart;
            if (open < Window) return;

            rate = frames / open.TotalSeconds;
            frames = 0;
            windowStart = now;
        }
    }
}
