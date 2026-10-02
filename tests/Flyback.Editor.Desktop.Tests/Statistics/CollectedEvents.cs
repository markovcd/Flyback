using Flyback.Editor.Statistics;

namespace Flyback.Editor.Desktop.Tests.Statistics;

/// <summary>A sink that keeps what a run says, so nothing reaches a network.</summary>
internal sealed class CollectedEvents : IUsageSink
{
    public List<UsageEvent> Events { get; } = [];

    public void Send(UsageEvent happened) => Events.Add(happened);

    public void Drain(TimeSpan most) { }
}
