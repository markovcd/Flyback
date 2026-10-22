namespace Flyback.Editor.Statistics;

/// <summary>
/// Keeps a run's events in memory until the person answers the first-start question:
/// a yes sends them on, so the first start is counted with the rest; a no drops them
/// unsent.
/// </summary>
/// <remarks>Locked, because a crash is said from whichever thread threw.</remarks>
internal sealed class HeldSink(IUsageSink target) : IUsageSink
{
    private readonly Lock gate = new();

    /// <summary>What has been said and not sent, or null once the answer came.</summary>
    private List<UsageEvent>? waiting = [];

    private bool dropped;

    /// <summary>Whether nothing has been answered yet.</summary>
    public bool Waiting
    {
        get
        {
            lock (gate) return waiting is not null;
        }
    }

    public void Send(UsageEvent happened)
    {
        lock (gate)
        {
            if (dropped) return;

            if (waiting is not null)
            {
                waiting.Add(happened);
                return;
            }
        }

        target.Send(happened);
    }

    /// <summary>Sends what was kept, and everything after it as it is said.</summary>
    public void Release()
    {
        List<UsageEvent> kept;

        lock (gate)
        {
            if (waiting is null) return;

            kept = waiting;
            waiting = null;
        }

        foreach (var happened in kept) target.Send(happened);
    }

    /// <summary>Forgets what was kept and sends nothing from now on.</summary>
    public void Drop()
    {
        lock (gate)
        {
            waiting = null;
            dropped = true;
        }
    }

    public void Drain(TimeSpan most)
    {
        lock (gate)
        {
            if (dropped || waiting is not null) return;
        }

        target.Drain(most);
    }
}
