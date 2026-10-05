using System.Globalization;
using System.Text.RegularExpressions;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>What <c>listen</c> tells a model about a clip's frequency content.</summary>
public sealed partial class ClipSpectrumTests
{
    private const int Rate = 24_000;

    [Fact]
    public void A_steady_note_is_named_by_its_pitch_to_within_a_hertz()
    {
        var said = ClipSpectrum.Described(Sum(Rate * 2, (440d, 0.5d)), Rate);

        Tones(said).ShouldHaveSingleItem().Frequency.ShouldBe(440d, 1d);
    }

    [Fact]
    public void A_note_with_harmonics_names_each_with_its_level_from_the_strongest()
    {
        var said = ClipSpectrum.Described(Sum(Rate * 2, (220d, 0.5d), (440d, 0.25d), (660d, 0.125d)), Rate);

        var tones = Tones(said);

        tones.Select(t => Math.Round(t.Frequency)).ShouldBe([220d, 440d, 660d]);
        tones.Select(t => t.Level).ShouldBe([0d, -6d, -12d], 1d);
    }

    [Fact]
    public void Noise_has_no_distinct_tones()
    {
        var random = new Random(7);
        var samples = new float[Rate * 2 * NodeCatalog.AudioChannels];

        for (var i = 0; i < samples.Length; i++) samples[i] = (float)(random.NextDouble() * 2 - 1) * 0.3f;

        var said = ClipSpectrum.Described(samples, Rate);

        said.ShouldContain("No distinct tones");
        Tones(said).ShouldBeEmpty();
    }

    [Fact]
    public void A_tone_puts_the_loudest_octave_band_around_it_and_its_brightness_at_its_pitch()
    {
        var said = ClipSpectrum.Described(Sum(Rate * 2, (1000d, 0.5d)), Rate);

        said.ShouldContain("1 kHz 0,");
        said.ShouldContain("500 Hz under -60");
        var center = Brightness().Match(said);

        center.Groups[1].Value.ShouldNotBeEmpty(said);
        double.Parse(center.Groups[1].Value, CultureInfo.InvariantCulture).ShouldBe(1d, 0.05d);
    }

    [Fact]
    public void Bands_stop_at_what_the_sample_rate_can_hold()
    {
        var said = ClipSpectrum.Described(Sum(Rate, (1000d, 0.5d)), Rate);

        said.ShouldContain("8 kHz");
        said.ShouldNotContain("16 kHz");
    }

    [Fact]
    public void A_clip_too_short_to_read_says_nothing() =>
        ClipSpectrum.Described(Sum(200, (1000d, 0.5d)), Rate).ShouldBeEmpty();

    [Fact]
    public void Listen_carries_the_spectrum_beside_the_levels()
    {
        var samples = Sum(Rate * 2, (440d, 0.5d));
        var (peak, rms) = ClipLevels.Levels(samples);

        ClipLevels.Measured(samples, peak, rms, Rate).ShouldContain("Spectrum, from the samples");
    }

    private static float[] Sum(int frames, params (double Hertz, double Amplitude)[] partials)
    {
        var samples = new float[frames * NodeCatalog.AudioChannels];

        for (var i = 0; i < frames; i++)
        {
            var value = 0d;

            foreach (var (hertz, amplitude) in partials)
                value += amplitude * Math.Sin(2 * Math.PI * hertz * i / Rate);

            samples[i * 2] = (float)value;
            samples[i * 2 + 1] = (float)value;
        }

        return samples;
    }

    private static List<(double Frequency, double Level)> Tones(string said)
    {
        var tones = new List<(double, double)>();
        var listed = said.IndexOf("Tones,", StringComparison.Ordinal);

        if (listed < 0) return tones;

        foreach (Match match in Tone().Matches(said[listed..]))
        {
            var frequency = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);

            tones.Add((match.Groups[2].Value == "kHz" ? frequency * 1000 : frequency,
                double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)));
        }

        return tones;
    }

    [GeneratedRegex(@"([\d.]+) (Hz|kHz) (-?\d+) dB")]
    private static partial Regex Tone();

    [GeneratedRegex(@"center of mass: ([\d.]+) kHz")]
    private static partial Regex Brightness();
}
