using Flyback.Plugins.Audio;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Audio;

/// <summary>The thread ALSA's and Android's outputs write on, against a device made of delegates.</summary>
public class BlockWriterTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [Fact]
    public void It_fills_and_writes_until_stopped_then_closes_on_its_own_thread()
    {
        using var written = new SemaphoreSlim(0);
        var writerThread = 0;
        var closedOn = 0;
        var filled = 0;

        var writer = BlockWriter.Start(
            new float[8],
            block =>
            {
                block.Fill(1f);
                Interlocked.Increment(ref filled);
            },
            (block, _) =>
            {
                block.ShouldAllBe(sample => sample == 1f);
                writerThread = Environment.CurrentManagedThreadId;
                written.Release();
                return true;
            },
            () => closedOn = Environment.CurrentManagedThreadId);

        written.Wait(Patience, TestContext.Current.CancellationToken).ShouldBeTrue();
        writer.IsRunning.ShouldBeTrue();

        writer.Stop();

        writer.IsRunning.ShouldBeFalse();
        filled.ShouldBeGreaterThan(0);
        closedOn.ShouldBe(writerThread);
    }

    [Fact]
    public void A_refused_write_ends_the_thread_and_closes_the_device()
    {
        using var closed = new ManualResetEventSlim();

        var writer = BlockWriter.Start(new float[8], _ => { }, (_, _) => false, closed.Set);

        closed.Wait(Patience, TestContext.Current.CancellationToken).ShouldBeTrue();
        writer.Stop();
        writer.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public void A_throwing_callback_ends_the_thread_quietly_and_closes_the_device()
    {
        using var closed = new ManualResetEventSlim();

        var writer = BlockWriter.Start(
            new float[8],
            _ => throw new InvalidOperationException("the engine fell over"),
            (_, _) => true,
            closed.Set);

        closed.Wait(Patience, TestContext.Current.CancellationToken).ShouldBeTrue();
        writer.Stop();
        writer.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public void A_partial_write_hears_that_it_is_no_longer_wanted()
    {
        using var asked = new ManualResetEventSlim();
        using var stopped = new ManualResetEventSlim();
        var stillWanted = true;

        var writer = BlockWriter.Start(
            new float[8],
            _ => { },
            (_, wanted) =>
            {
                asked.Set();
                stopped.Wait(Patience);
                stillWanted = wanted();
                return stillWanted;
            },
            () => { });

        asked.Wait(Patience, TestContext.Current.CancellationToken).ShouldBeTrue();
        writer.Stop(waitMilliseconds: 0);
        stopped.Set();
        writer.Stop();

        stillWanted.ShouldBeFalse();
    }

    /// <summary>
    /// A device that stops answering outlives <see cref="BlockWriter.Stop"/>. When its write
    /// returns, that thread closes its own device and never touches the writer started after it.
    /// </summary>
    [Fact]
    public void A_hung_writer_left_behind_never_reaches_the_writer_after_it()
    {
        using var hung = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var firstClosed = new ManualResetEventSlim();
        var firstThread = 0;

        var first = BlockWriter.Start(
            new float[8],
            _ => { },
            (_, _) =>
            {
                firstThread = Environment.CurrentManagedThreadId;
                hung.Set();
                release.Wait(Patience);
                return true;
            },
            firstClosed.Set);

        hung.Wait(Patience, TestContext.Current.CancellationToken).ShouldBeTrue();
        first.Stop(waitMilliseconds: 10);

        var secondFilledOn = new System.Collections.Concurrent.ConcurrentBag<int>();
        var secondWrittenOn = new System.Collections.Concurrent.ConcurrentBag<int>();
        var secondClosed = false;

        var second = BlockWriter.Start(
            new float[8],
            _ => secondFilledOn.Add(Environment.CurrentManagedThreadId),
            (_, _) =>
            {
                secondWrittenOn.Add(Environment.CurrentManagedThreadId);
                return true;
            },
            () => secondClosed = true);

        release.Set();
        firstClosed.Wait(Patience, TestContext.Current.CancellationToken).ShouldBeTrue();

        second.IsRunning.ShouldBeTrue();
        secondClosed.ShouldBeFalse();
        secondFilledOn.ShouldNotContain(firstThread);
        secondWrittenOn.ShouldNotContain(firstThread);

        second.Stop();
        secondClosed.ShouldBeTrue();
    }
}
