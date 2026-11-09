using Flyback.Ui.Audio;
using Shouldly;
using Xunit;

namespace Flyback.Ui.Tests.Audio;

/// <summary>How loud the sound peaks lately, and how often it clipped.</summary>
public sealed class PeakMeterTests
{
    [Fact]
    public void A_buffer_sets_the_peak_to_its_loudest_sample_either_side_of_zero()
    {
        var meter = new PeakMeter();

        meter.Take([0.1f, -0.6f, 0.3f, 0.2f], 0.01);

        meter.Peak.ShouldBe(0.6f);
    }

    [Fact]
    public void A_quieter_buffer_lets_the_peak_fall_at_its_rate_rather_than_drop()
    {
        var meter = new PeakMeter();
        meter.Take([1f / 2, 0], 0.01);

        meter.Take([0, 0], 0.5);

        // Half a second at 24 dB a second is 12 dB down.
        meter.Peak.ShouldBe(0.5f * (float)Math.Pow(10, -12d / 20), 1e-6f);
    }

    [Fact]
    public void A_channel_held_at_full_scale_counts_as_clipped()
    {
        var meter = new PeakMeter();

        // Left, right, left, right: the right channel sits at full scale twice running.
        meter.Take([0.2f, 1f, 0.3f, -1f], 0.01);

        meter.Clipped.ShouldBe(1);
    }

    [Fact]
    public void A_full_scale_wave_touching_full_scale_once_is_not_clipped()
    {
        var meter = new PeakMeter();

        meter.Take([0.99f, 0.1f, 1f, 0.1f, 0.99f, 0.1f], 0.01);
        meter.Take([1f, 1f, 0.5f, 0.5f], 0.01);

        meter.Clipped.ShouldBe(0);
    }

    [Fact]
    public void The_volume_turns_the_peak_down_but_not_a_clip_away()
    {
        var meter = new PeakMeter();

        meter.Take([1f, -0.2f, 1f, -0.2f], 0.01, volume: 0.5f);

        meter.Peak.ShouldBe(0.5f);
        meter.Clipped.ShouldBe(1);
    }
}
