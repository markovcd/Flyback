using Shouldly;
using Xunit;

namespace Flyback.Editor.Desktop.Tests;

public sealed class TraceFlagTests
{
    [Fact]
    public void The_file_is_taken_out_so_it_is_not_opened_as_a_patch()
    {
        var (path, without) = TraceFlag.Taken(["--trace", "stalls.txt", "nebula.fbk"]);

        path.ShouldBe("stalls.txt");
        without.ShouldBe(["nebula.fbk"]);
    }

    [Fact]
    public void A_launch_without_the_flag_is_left_as_it_was()
    {
        var (path, without) = TraceFlag.Taken(["--interpreted", "nebula.fbk"]);

        path.ShouldBeNull();
        without.ShouldBe(["--interpreted", "nebula.fbk"]);
    }

    [Fact]
    public void A_flag_with_no_file_is_dropped()
    {
        var (path, without) = TraceFlag.Taken(["nebula.fbk", "--trace"]);

        path.ShouldBeNull();
        without.ShouldBe(["nebula.fbk"]);
    }
}
