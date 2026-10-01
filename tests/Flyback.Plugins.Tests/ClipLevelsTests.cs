using System.Globalization;
using System.Text.RegularExpressions;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>What <c>listen</c> tells a model about the clip's level, in the figures a loudness target is given in.</summary>
public sealed partial class ClipLevelsTests
{
    private const int Rate = 24_000;

    /// <summary>
    /// A 1 kHz tone in both channels a tenth of full scale: BS.1770 reads one channel
    /// at full scale as -3.01 LUFS, and the second channel puts the 3 dB back.
    /// </summary>
    [Fact]
    public void A_tone_a_tenth_of_full_scale_measures_about_minus_twenty_LUFS()
    {
        var said = Measured(Tone(0.1f));

        Number(Lufs().Match(said)).ShouldBe(-20d, 0.5d);
        Number(TruePeak().Match(said)).ShouldBe(-20d, 0.5d);
    }

    [Fact]
    public void A_clip_too_quiet_to_gate_says_so_rather_than_giving_a_number()
    {
        Measured(Tone(0.0001f)).ShouldContain("too quiet to gate");
    }

    private static string Measured(float[] samples)
    {
        var (peak, rms) = ClipLevels.Levels(samples);

        return ClipLevels.Measured(samples, peak, rms, Rate);
    }

    private static float[] Tone(float amplitude)
    {
        var samples = new float[Rate * 2 * NodeCatalog.AudioChannels];

        for (var i = 0; i < samples.Length / NodeCatalog.AudioChannels; i++)
        {
            var value = amplitude * (float)Math.Sin(2 * Math.PI * 1000 * i / Rate);

            samples[i * 2] = value;
            samples[i * 2 + 1] = value;
        }

        return samples;
    }

    private static double Number(Match match)
    {
        match.Success.ShouldBeTrue();

        return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"(-?\d+\.\d) LUFS integrated")]
    private static partial Regex Lufs();

    [GeneratedRegex(@"true peak (-?\d+\.\d) dBTP")]
    private static partial Regex TruePeak();
}
