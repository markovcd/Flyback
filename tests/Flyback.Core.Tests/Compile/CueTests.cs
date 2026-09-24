using Flyback.Core.Compile;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary><see cref="Cue"/> — when an opened patch's sound and picture start.</summary>
public class CueTests
{
    [Fact]
    public void It_waits_for_its_opener_and_every_part_taken()
    {
        var start = new Cue();
        start.Take();
        start.Take();

        start.Give();
        start.Waiting.ShouldBeTrue();

        start.Give();
        start.Waiting.ShouldBeTrue();

        start.Give();
        start.Waiting.ShouldBeFalse();
    }

    /// <summary>A part nobody gives back, a preview never drawn, costs a wait and not the patch.</summary>
    [Fact]
    public void It_goes_once_its_patience_runs_out()
    {
        var start = new Cue(TimeSpan.Zero);
        start.Take();

        start.Waiting.ShouldBeFalse();
    }
}
