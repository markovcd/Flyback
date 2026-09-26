namespace Flyback.App.Statistics;

/// <summary>Where events go.</summary>
/// <remarks>
/// An interface so that what a run says can be tested without a network, and so
/// that nothing in <see cref="Usage"/> knows which service is being written to.
/// Sending is a thing that is started, never waited for: the caller is on the UI
/// thread and a statistic is worth nothing beside a frame.
/// </remarks>
internal interface IUsageSink
{
    void Send(UsageEvent happened);

    /// <summary>
    /// Waits for what was sent to arrive, for at most <paramref name="most"/>. The
    /// one wait there is, for a run that is ending and would otherwise take its
    /// last events with it.
    /// </summary>
    void Drain(TimeSpan most);
}