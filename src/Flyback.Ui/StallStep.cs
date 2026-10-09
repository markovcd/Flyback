using System.Diagnostics;

namespace Flyback.Ui;

/// <summary>One named piece of work, timed for <see cref="StallTrace"/>; costs a branch while no trace is open.</summary>
internal readonly struct StallStep : IDisposable
{
    private readonly string name;
    private readonly long started;

    public StallStep(string name)
    {
        this.name = name;
        started = StallTrace.On ? Stopwatch.GetTimestamp() : 0;
    }

    /// <summary>A step that began at <paramref name="started"/> on the <see cref="Stopwatch"/> clock.</summary>
    public StallStep(string name, long started)
    {
        this.name = name;
        this.started = StallTrace.On ? started : 0;
    }

    /// <summary>Writes the step down if it ran past <see cref="StallTrace.Threshold"/>.</summary>
    public void Dispose()
    {
        if (started == 0) return;

        var took = Stopwatch.GetElapsedTime(started);

        if (took >= StallTrace.Threshold) StallTrace.Stalled(name, took, step: true);
    }
}
