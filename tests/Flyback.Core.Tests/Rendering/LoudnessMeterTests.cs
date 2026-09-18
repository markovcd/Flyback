using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Rendering;

/// <summary>
/// The render's loudness report, against the figures BS.1770 is calibrated by.
/// </summary>
public class LoudnessMeterTests
{
    private const int Rate = GlobalConstants.SampleRate;

    /// <summary>
    /// The standard's own calibration: 997 Hz at full scale in one channel of
    /// two reads -3.01 LUFS.
    /// </summary>
    [Fact]
    public void A_full_scale_sine_in_one_channel_reads_minus_three()
    {
        Measure(Stereo(Sine(997d, 1d, 10), Silence(10))).Integrated.ShouldBe(-3.01d, 0.02d);
    }

    [Fact]
    public void Both_channels_at_minus_twenty_read_minus_twenty()
    {
        var tone = Sine(997d, 0.1d, 10);

        Measure(Stereo(tone, tone)).Integrated.ShouldBe(-20d, 0.02d);
    }

    /// <summary>
    /// The gates: silence after the music does not make the music quieter. The
    /// few blocks that straddle the end are partly quiet and pass the gates, as
    /// the standard has them do, which costs a few hundredths.
    /// </summary>
    [Fact]
    public void Silence_is_not_counted()
    {
        var tone = Sine(997d, 0.1d, 10).Concat(Silence(10)).ToArray();

        Measure(Stereo(tone, tone)).Integrated.ShouldBe(-20d, 0.1d);
    }

    /// <summary>
    /// The relative gate: a passage 30 dB under the rest is left out, so it
    /// counts for nothing rather than for a third of the time.
    /// </summary>
    [Fact]
    public void A_passage_far_under_the_rest_is_gated_out()
    {
        var tone = Sine(997d, 0.1d, 10).Concat(Sine(997d, 0.1d / 31.6d, 10)).ToArray();

        Measure(Stereo(tone, tone)).Integrated.ShouldBe(-20d, 0.1d);
    }

    [Fact]
    public void Nothing_at_all_reads_minus_infinity()
    {
        Measure(Stereo(Silence(2), Silence(2))).Integrated.ShouldBe(double.NegativeInfinity);
    }

    /// <summary>
    /// A quarter of the rate at 45 degrees is sampled at ±0.707 of a full-scale
    /// peak it never lands on — the case true peak exists for.
    /// </summary>
    [Fact]
    public void A_peak_between_samples_is_found()
    {
        var tone = Sine(Rate / 4d, 1d, 1, Math.PI / 4d);
        var meter = Measure(Stereo(tone, tone));

        meter.SamplePeak.ShouldBe(Math.Sqrt(0.5d), 1e-6);
        (20d * Math.Log10(meter.TruePeak)).ShouldBe(0d, 0.2d);
    }

    [Fact]
    public void Blocks_of_any_size_measure_the_same()
    {
        var tone = Sine(440d, 0.3d, 3);
        var whole = Stereo(tone, tone);

        var once = Measure(whole);

        var inPieces = new LoudnessMeter(Rate, 2);
        for (var at = 0; at < whole.Length; at += 1234)
            inPieces.Add(whole.AsSpan(at, Math.Min(1234, whole.Length - at)));

        inPieces.Integrated.ShouldBe(once.Integrated, 1e-9);
        inPieces.TruePeak.ShouldBe(once.TruePeak);
    }

    private static LoudnessMeter Measure(float[] interleaved)
    {
        var meter = new LoudnessMeter(Rate, 2);
        meter.Add(interleaved);
        return meter;
    }

    private static float[] Sine(double hertz, double amplitude, double seconds, double phase = 0d) =>
        [.. Enumerable.Range(0, (int)(seconds * Rate)).Select(i => (float)(amplitude * Math.Sin(2d * Math.PI * hertz * i / Rate + phase)))];

    private static float[] Silence(double seconds) => new float[(int)(seconds * Rate)];

    private static float[] Stereo(float[] left, float[] right) =>
        [.. left.Zip(right).SelectMany(pair => new[] { pair.First, pair.Second })];
}
