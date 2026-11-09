using Flyback.Editor.Bars;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Bars;

/// <summary>A peak read as decibels, and where it reaches on the level bar.</summary>
public sealed class SoundLevelTests
{
    [Theory]
    [InlineData(1f, 0)]
    [InlineData(0.5f, -6.02)]
    [InlineData(0.1f, -20)]
    public void A_peak_reads_as_decibels_below_full_scale(float peak, double decibels) =>
        SoundLevel.Decibels(peak).ShouldBe(decibels, 0.01);

    [Fact]
    public void Silence_is_no_level_at_all() => SoundLevel.Decibels(0).ShouldBe(double.NegativeInfinity);

    [Theory]
    [InlineData(double.NegativeInfinity, 0)]
    [InlineData(-60, 0)]
    [InlineData(-24, 0.5)]
    [InlineData(0, 1)]
    [InlineData(3, 1)]
    public void The_bar_reaches_evenly_in_decibels_from_its_floor_to_full_scale(double decibels, double reach) =>
        SoundLevel.Reach(decibels).ShouldBe(reach);

    [Theory]
    [InlineData(-3.5, false)]
    [InlineData(-2.9, true)]
    [InlineData(0, true)]
    public void Within_three_decibels_of_full_scale_is_hot(double decibels, bool hot) => SoundLevel.Hot(decibels).ShouldBe(hot);
}
