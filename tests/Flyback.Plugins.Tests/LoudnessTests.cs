using Flyback.Core.Graph;
using Shouldly;
using Xunit;
using static Flyback.Plugins.Tests.MasteringBench;

namespace Flyback.Plugins.Tests;

public class LoudnessTests
{
    private const string Type = "flyback.mastering.loudness";

    private const int Momentary = 0;
    private const int Short = 1;
    private const int Peak = 2;

    [Fact]
    public void The_mastering_plugin_offers_it_under_measurement_for_audio()
    {
        var def = Catalog.Get(Type).ShouldNotBeNull();

        def.Name.ShouldBe("Loudness");
        def.Category.ShouldBe(ModuleCategories.Measurement);
        def.Sinks.ShouldBe(ModuleSinks.Audio);
    }

    /// <summary>
    /// The standard's own calibration: a 997 Hz sine at full scale in one channel
    /// reads -3.01 LUFS.
    /// </summary>
    [Fact]
    public void A_full_scale_sine_in_one_side_reads_minus_three()
    {
        var heard = Play(Type, Sine(997d, 1f, 3.5), Hold(0f, 3.5), heard: (Momentary, Short));

        heard.Left[^1].ShouldBe(-3.01f, 0.05f);
        heard.Right[^1].ShouldBe(-3.01f, 0.05f);
    }

    [Fact]
    public void Both_sides_at_minus_twenty_read_minus_twenty()
    {
        Play(Type, Sine(997d, 0.1f, 1), heard: (Momentary, Momentary)).Left[^1].ShouldBe(-20f, 0.05f);
    }

    [Fact]
    public void The_short_term_reading_takes_three_seconds_to_arrive()
    {
        var heard = Play(Type, Sine(997d, 0.1f, 3.2), heard: (Momentary, Short));

        heard.Left[Rate].ShouldBe(-20f, 0.05f);
        heard.Right[Rate].ShouldBeLessThan(-24f);
        heard.Right[^1].ShouldBe(-20f, 0.05f);
    }

    /// <summary>K-weighting's highpass: a bass note counts for less than its level.</summary>
    [Fact]
    public void Deep_bass_counts_for_less()
    {
        Play(Type, Sine(25d, 0.1f, 1), heard: (Momentary, Momentary)).Left[^1].ShouldBeLessThan(-22f);
    }

    [Fact]
    public void Silence_reads_the_floor_and_comes_back_to_it()
    {
        var signal = Sine(997d, 0.5f, 0.5).Concat(Hold(0f, 0.5)).ToArray();

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        Play(Type, Hold(0f, 0.1), heard: (Momentary, Momentary)).Left.ShouldAllBe(l => l == -70f);
        Play(Type, signal, heard: (Momentary, Momentary)).Left[^1].ShouldBe(-70f);
    }

    [Fact]
    public void The_peak_is_the_highest_sample_and_falls_back()
    {
        var signal = Hold(0.5f, 0.01).Concat(Hold(0f, 1.7)).ToArray();

        var peak = Play(Type, signal, heard: (Peak, Peak)).Left;

        peak[Rate / 100 - 1].ShouldBe(-6.02f, 0.01f);
        peak[^1].ShouldBe(-26.02f, 0.05f);
    }
}
