using Flyback.App.Tests.Headless;
using Xunit;

[assembly: AssemblyFixture(typeof(PoolHeadroom))]

namespace Flyback.App.Tests.Headless;

/// <summary>
/// Enough pool threads that xunit's own cannot use them all up.
/// </summary>
/// <remarks>
/// xunit runs tests on pool threads, and every <c>[AvaloniaFact]</c> blocks one
/// while it waits its turn on the UI thread, so the UI collections alone can
/// hold every thread the pool keeps ready. Past that the pool only grows by
/// starvation injection, which stops while the machine's memory is nearly full;
/// until it frees up, nothing needing a pool thread runs, whether it is the UI
/// tests themselves, an assistant's continuation or ffmpeg's pipe readers.
/// </remarks>
public sealed class PoolHeadroom
{
    public PoolHeadroom()
    {
        ThreadPool.GetMinThreads(out var workers, out var ports);
        ThreadPool.SetMinThreads(Math.Max(workers, Environment.ProcessorCount * 4), ports);
    }
}
