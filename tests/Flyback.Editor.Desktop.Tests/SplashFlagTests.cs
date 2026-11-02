using Shouldly;
using Xunit;

namespace Flyback.Editor.Desktop.Tests;

public sealed class SplashFlagTests
{
    [Fact]
    public void The_flag_is_taken_out_so_it_is_not_opened_as_a_patch()
    {
        var (given, without) = SplashFlag.Taken(["--splash", "nebula.fbk"]);

        given.ShouldBeTrue();
        without.ShouldBe(["nebula.fbk"]);
    }

    [Fact]
    public void A_launch_without_the_flag_is_left_as_it_was()
    {
        var (given, without) = SplashFlag.Taken(["--interpreted", "nebula.fbk"]);

        given.ShouldBeFalse();
        without.ShouldBe(["--interpreted", "nebula.fbk"]);
    }
}
