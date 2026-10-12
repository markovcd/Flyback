using Shouldly;
using Xunit;

namespace Flyback.Plugins.WinIO.Tests;

/// <summary>How long a call to the driver's thread is waited on, and what that wait counts.</summary>
public sealed class AsioThreadTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(1);

    [Fact]
    public void A_call_queued_behind_other_work_is_timed_from_when_the_driver_is_asked()
    {
        using var thread = new AsioThread(Patience);
        using var earlier = new ManualResetEventSlim();

        thread.Post(() => earlier.Wait());
        var answer = Task.Run(() => thread.Invoke(() => Thread.Sleep(Patience * 0.6)), TestContext.Current.CancellationToken);

        Thread.Sleep(Patience * 0.6);
        earlier.Set();

        Should.NotThrow(() => answer.Wait(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_call_stuck_behind_work_that_never_finishes_is_given_up_on()
    {
        using var never = new ManualResetEventSlim();
        var thread = new AsioThread(TimeSpan.FromMilliseconds(100));

        thread.Post(() => never.Wait());

        Should.Throw<TimeoutException>(() => thread.Invoke(() => { }));
        never.Set();
        thread.Dispose();
    }

    [Fact]
    public void A_driver_that_does_not_answer_is_given_up_on_and_stays_given_up_on()
    {
        using var never = new ManualResetEventSlim();
        var thread = new AsioThread(TimeSpan.FromMilliseconds(100));

        Should.Throw<TimeoutException>(() => thread.Invoke(() => never.Wait()));
        Should.Throw<TimeoutException>(() => thread.Invoke(() => { }));
        never.Set();
        thread.Dispose();
    }
}
