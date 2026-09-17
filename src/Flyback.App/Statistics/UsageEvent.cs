namespace Flyback.App.Statistics;

/// <summary>
/// One thing a run has to say about itself: a name, and numbers and names beside
/// it. Everything that may appear in one is listed in ADR-0094.
/// </summary>
/// <param name="Name">What happened — <c>started</c>, <c>played</c>, <c>assistant</c>.</param>
/// <param name="Props">
/// What it carries. A value is a string, a number or a flag; nothing else survives
/// the far end, which sorts them into words and numbers.
/// </param>
internal sealed record UsageEvent(string Name, IReadOnlyDictionary<string, object> Props);

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
}
