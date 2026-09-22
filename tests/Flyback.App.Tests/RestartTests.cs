using Shouldly;
using Xunit;

namespace Flyback.App.Tests;

public sealed class RestartTests
{
    [Fact]
    public void The_process_to_wait_for_is_taken_out_of_the_arguments()
    {
        Restart.Awaited(["--interpreted", Restart.AfterFlag, "2147483645", "nebula.fbk"]).ShouldBe(["--interpreted", "nebula.fbk"]);
    }

    [Fact]
    public void A_launch_with_nothing_to_wait_for_is_left_as_it_was()
    {
        Restart.Awaited(["nebula.fbk"]).ShouldBe(["nebula.fbk"]);
    }

    /// <summary>Process ids are multiples of four on Windows and small elsewhere, so this one is never running.</summary>
    [Fact]
    public void A_process_already_gone_is_not_waited_for()
    {
        var started = DateTime.UtcNow;

        Restart.Awaited([Restart.AfterFlag, "2147483645"]).ShouldBeEmpty();

        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(1));
    }

    /// <summary>Waiting on a process this user may not open would otherwise stop the launch before it began.</summary>
    [Fact]
    public void A_process_that_cannot_be_watched_is_not_waited_for()
    {
        Restart.Awaited([Restart.AfterFlag, "0"]).ShouldBeEmpty();
    }
}
