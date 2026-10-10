using Flyback.Plugins.Audio;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>The thread ALSA's and Android's inputs read on, against an input made of delegates.</summary>
public class BlockReaderTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [Fact]
    public void It_opens_on_its_own_thread_delivers_what_was_read_and_closes_there_once_stopped()
    {
        using var heard = new SemaphoreSlim(0);
        var openedOn = 0;
        var deliveredOn = 0;
        var closedOn = 0;
        var lengths = new System.Collections.Concurrent.ConcurrentBag<int>();

        var reader = BlockReader.Start(
            samples =>
            {
                lengths.Add(samples.Length);
                deliveredOn = Environment.CurrentManagedThreadId;
                heard.Release();
            },
            _ =>
            {
                openedOn = Environment.CurrentManagedThreadId;
                return new BlockSource(new float[8], _ => 6, () => closedOn = Environment.CurrentManagedThreadId);
            });

        heard.Wait(Patience, TestContext.Current.CancellationToken).ShouldBeTrue();
        reader.IsRunning.ShouldBeTrue();

        reader.Stop();

        reader.IsRunning.ShouldBeFalse();
        lengths.ShouldAllBe(length => length == 6);
        deliveredOn.ShouldBe(openedOn);
        closedOn.ShouldBe(openedOn);
        openedOn.ShouldNotBe(Environment.CurrentManagedThreadId);
    }

    [Fact]
    public void An_empty_read_delivers_nothing_and_reading_carries_on()
    {
        using var readAgain = new SemaphoreSlim(0);
        var reads = 0;
        var delivered = 0;

        var reader = BlockReader.Start(
            _ => Interlocked.Increment(ref delivered),
            _ => new BlockSource(new float[8], _ =>
            {
                if (Interlocked.Increment(ref reads) > 2) readAgain.Release();
                return 0;
            }, () => { }));

        readAgain.Wait(Patience, TestContext.Current.CancellationToken).ShouldBeTrue();
        reader.Stop();

        delivered.ShouldBe(0);
    }

    [Fact]
    public void A_refused_read_ends_the_thread_and_closes_the_input()
    {
        using var closed = new ManualResetEventSlim();

        var reader = BlockReader.Start(_ => { }, _ => new BlockSource(new float[8], _ => -1, closed.Set));

        closed.Wait(Patience, TestContext.Current.CancellationToken).ShouldBeTrue();
        reader.Stop();
        reader.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public void Nothing_to_open_ends_the_thread_without_delivering()
    {
        var delivered = false;

        var reader = BlockReader.Start(_ => delivered = true, _ => null);

        reader.Stop();

        reader.IsRunning.ShouldBeFalse();
        delivered.ShouldBeFalse();
    }

    [Fact]
    public void Opening_is_told_when_it_is_no_longer_wanted()
    {
        using var asking = new ManualResetEventSlim();
        using var answered = new ManualResetEventSlim();
        var stillWanted = true;
        var delivered = false;

        var reader = BlockReader.Start(
            _ => delivered = true,
            wanted =>
            {
                asking.Set();
                answered.Wait(Patience);
                stillWanted = wanted();
                return null;
            });

        asking.Wait(Patience, TestContext.Current.CancellationToken).ShouldBeTrue();
        reader.Stop(waitMilliseconds: 0);
        answered.Set();
        reader.Stop();

        stillWanted.ShouldBeFalse();
        delivered.ShouldBeFalse();
    }

    [Fact]
    public void A_hung_reader_left_behind_never_reaches_the_reader_after_it()
    {
        using var hung = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var firstClosed = new ManualResetEventSlim();
        var firstThread = 0;

        var first = BlockReader.Start(
            _ => { },
            _ => new BlockSource(new float[8], _ =>
            {
                firstThread = Environment.CurrentManagedThreadId;
                hung.Set();
                release.Wait(Patience);
                return 8;
            }, firstClosed.Set));

        hung.Wait(Patience, TestContext.Current.CancellationToken).ShouldBeTrue();
        first.Stop(waitMilliseconds: 10);

        var secondDeliveredOn = new System.Collections.Concurrent.ConcurrentBag<int>();

        var second = BlockReader.Start(
            _ => secondDeliveredOn.Add(Environment.CurrentManagedThreadId),
            _ => new BlockSource(new float[8], _ => 8, () => { }));

        release.Set();
        firstClosed.Wait(Patience, TestContext.Current.CancellationToken).ShouldBeTrue();
        second.Stop();

        secondDeliveredOn.ShouldNotContain(firstThread);
    }
}
