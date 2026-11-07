using Flyback.Core.Graph;
using Flyback.Editor.Sequence;
using Flyback.Engine.Language;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Sequence;

/// <summary>How far the Song has played at a second, and the second it gets to a section.</summary>
public class SongClockTests
{
    /// <summary>A Song of two-second sections on the patch's clock.</summary>
    private static SongClock Clock()
    {
        var load = PatchLanguage.Build("let song = song(rate: 0.5) [ kick: 1 0 ]\nsong.kick |> out.left", NodeCatalog.BuiltIn);
        load.Ok.ShouldBeTrue(load.Report);

        return SongClock.Of(load.Patch, load.Patch.FirstOf(SongModule.TypeId)!.Id).ShouldNotBeNull();
    }

    [Fact]
    public void It_counts_the_sections_played_without_coming_round() =>
        Clock().Played(9).ShouldBe(4.5, tolerance: 1e-6);

    [Fact]
    public void A_section_is_reached_at_the_second_it_starts()
    {
        var clock = Clock();

        clock.SecondsAt(3, 60).ShouldNotBeNull().ShouldBe(6, tolerance: 1e-3);
        clock.SecondsAt(0, 60).ShouldBe(0);
    }

    [Fact]
    public void A_section_past_the_patch_s_length_is_never_reached() =>
        Clock().SecondsAt(40, 60).ShouldBeNull();
}
