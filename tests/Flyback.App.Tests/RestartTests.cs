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

    /// <summary>
    /// What a restart carries survives the wait being taken back out of it, and arrives
    /// as the plain argument a file opened with Flyback already is.
    /// </summary>
    [Fact]
    public void A_patch_a_restart_carries_is_what_the_launch_opens()
    {
        var launched = Restart.Arguments(2147483645, new Reopen(Path: "nebula.fbk"));

        Restart.Awaited(launched).ShouldBe(["nebula.fbk"]);
        launched.ShouldNotContain(a => a.StartsWith('-') && a != Restart.AfterFlag);
    }

    /// <summary>
    /// A preset from the site has no file to name, so it travels as its id — and the id
    /// has to come back out before what is left is read as a path.
    /// </summary>
    [Fact]
    public void A_shared_preset_a_restart_carries_travels_as_its_id()
    {
        var launched = Restart.Arguments(2147483645, new Reopen(Shared: "01a0cad9"));
        var waited = Restart.Awaited(launched);

        var (shared, without) = Restart.Shared(waited);

        shared.ShouldBe("01a0cad9");
        without.ShouldBeEmpty();
    }

    [Fact]
    public void A_launch_carrying_no_shared_preset_is_left_as_it_was()
    {
        var (shared, without) = Restart.Shared(["nebula.fbk"]);

        shared.ShouldBeNull();
        without.ShouldBe(["nebula.fbk"]);
    }

    [Fact]
    public void A_restart_with_nothing_to_open_carries_none()
    {
        Restart.Awaited(Restart.Arguments(2147483645, null)).ShouldBeEmpty();
        Restart.Awaited(Restart.Arguments(2147483645, new Reopen())).ShouldBeEmpty();
        Restart.Awaited(Restart.Arguments(2147483645, new Reopen(Path: string.Empty))).ShouldBeEmpty();
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
