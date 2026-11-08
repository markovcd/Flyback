using Flyback.Editor.Bars;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Bars;

/// <summary>How a sound's speed reads as rungs lit and a headroom.</summary>
public sealed class SoundHeadroomTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1.09, 0)]
    [InlineData(1.1, 1)]
    [InlineData(1.6, 2)]
    [InlineData(2.5, 3)]
    [InlineData(3.1, 4)]
    [InlineData(12, 5)]
    public void Each_rung_lights_at_its_speed(double speed, int lit) => SoundHeadroom.Lit(speed).ShouldBe(lit);

    [Theory]
    [InlineData(0, 2, "untimed")]
    [InlineData(3.1, 2, "plenty")]
    [InlineData(1.3, 2, "tight")]
    [InlineData(0.9, 2, "tight")]
    [InlineData(0.9, 1, "behind")]
    [InlineData(1.2, 1, "tight")]
    public void Behind_is_only_slower_than_real_time_with_no_oversampling_left_to_give_up(double speed, int oversample, string headroom) =>
        SoundHeadroom.Word(SoundHeadroom.Of(speed, oversample)).ShouldBe(headroom);
}
