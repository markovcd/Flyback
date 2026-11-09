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
    public void Only_a_buffer_past_full_scale_counts_as_clipped()
    {
        var meter = new PeakMeter();

        meter.Take([1f, -1f], 0.01);
        meter.Take([0.5f, -1.01f], 0.01);
        meter.Take([1.3f, 0], 0.01);

        meter.Clipped.ShouldBe(2);
    }
}
