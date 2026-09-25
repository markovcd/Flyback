namespace Flyback.Viewer;

/// <summary>The wall clock <c>--for</c> and <c>--loop</c> count played time against, from the start of the run.</summary>
internal sealed class WallClock(TimeProvider time)
{
    private readonly long started = time.GetTimestamp();

    /// <summary>How long the run has been going.</summary>
    public TimeSpan Elapsed => time.GetElapsedTime(started);
}
