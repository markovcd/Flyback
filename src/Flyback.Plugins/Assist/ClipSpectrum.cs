using System.Globalization;
using System.Numerics;
using System.Text;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;

namespace Flyback.Plugins.Assist;

/// <summary>What <c>listen</c> measures in a rendered clip's frequency content: its octave bands, its brightness and the tones standing out of it.</summary>
internal static class ClipSpectrum
{
    /// <summary>The transform length: about 3 Hz a bin at the rate a clip is rendered at, fine enough to tell a note from the next one down.</summary>
    private const int Segment = 8192;

    /// <summary>A clip shorter than this is too short to read a spectrum from.</summary>
    private const int ShortestSegment = 512;

    /// <summary>The lowest frequency reported, in hertz: the bottom of hearing, as an Analyzer's axis starts.</summary>
    private const double Lowest = 20d;

    /// <summary>Where the first octave band is centered, in hertz; each next one is twice as high.</summary>
    private const double FirstBand = 31.25d;

    /// <summary>How far either side of a bin a tone must be the loudest, in bins: past the main lobe of the window.</summary>
    private const int Reach = 4;

    /// <summary>How far out the surroundings a tone must stand above are taken from, in bins.</summary>
    private const int Surround = 40;

    /// <summary>How far above its surroundings a peak must stand to be a tone, as a power ratio: 10 dB.</summary>
    private const double Prominence = 10d;

    /// <summary>How far below the strongest tone one may be and still be named, as a power ratio: 50 dB.</summary>
    private const double Floor = 1e-5d;

    /// <summary>The most tones named.</summary>
    private const int MostTones = 8;

    /// <summary>Below this an octave band is not given a figure: 60 dB under the loudest.</summary>
    private const double Quiet = -60d;

    /// <summary>
    /// The spectrum of an interleaved stereo clip over the whole of it, as figures
    /// with their yardstick beside them, or nothing where the clip is too short.
    /// </summary>
    /// <remarks>
    /// A tone is a narrow peak standing well above what is around it, so a steady
    /// note names its pitch and its harmonics and noise names none, which is the
    /// difference between a sine and a hiss that levels alone cannot show.
    /// </remarks>
    public static string Described(ReadOnlySpan<float> samples, int sampleRate)
    {
        var frames = samples.Length / NodeCatalog.AudioChannels;
        var length = 1 << BitOperations.Log2((uint)Math.Min(frames, Segment));

        if (length < ShortestSegment) return string.Empty;

        var power = Power(samples, length);
        var perBin = (double)sampleRate / length;

        var text = new StringBuilder("Spectrum, from the samples, over the whole clip. ")
            .Append("Octave bands in dB from the loudest band, which is 0, up to ")
            .Append(Hertz(sampleRate / 2d))
            .Append(", as far as the clip is rendered: ")
            .Append(Bands(power, perBin, sampleRate))
            .Append(". Brightness, the spectrum's center of mass: ")
            .Append(Hertz(Centroid(power, perBin)))
            .Append(". ")
            .Append(Tones(power, perBin));

        return text.ToString();
    }

    /// <summary>The power in each bin, both channels averaged.</summary>
    private static double[] Power(ReadOnlySpan<float> samples, int length)
    {
        var frames = samples.Length / NodeCatalog.AudioChannels;
        var power = new double[length / 2 + 1];
        var channel = new float[frames];

        for (var c = 0; c < NodeCatalog.AudioChannels; c++)
        {
            for (var i = 0; i < frames; i++) channel[i] = samples[i * NodeCatalog.AudioChannels + c];

            var amplitudes = Spectra.Amplitudes(channel, length);

            for (var k = 0; k < power.Length; k++)
                power[k] += amplitudes[k] * amplitudes[k] / NodeCatalog.AudioChannels;
        }

        return power;
    }

    private static string Bands(double[] power, double perBin, int sampleRate)
    {
        var nyquist = sampleRate / 2d;
        var bands = new List<(double Center, double Total)>();

        for (var center = FirstBand; center * Math.Sqrt(2) <= nyquist; center *= 2)
        {
            var from = Math.Max(center / Math.Sqrt(2), Lowest);
            var to = center * Math.Sqrt(2);
            var total = 0d;

            for (var k = (int)Math.Ceiling(from / perBin); k < power.Length && k * perBin < to; k++)
                total += power[k];

            bands.Add((center, total));
        }

        var loudest = bands.Max(band => band.Total);
        var text = new StringBuilder();

        foreach (var (center, total) in bands)
        {
            var level = total <= 0d ? double.NegativeInfinity : 10 * Math.Log10(total / loudest);

            if (text.Length > 0) text.Append(", ");

            text.Append(Hertz(center)).Append(' ')
                .Append(level < Quiet ? "under -60" : Math.Round(level).ToString("0", CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    private static double Centroid(double[] power, double perBin)
    {
        double weighted = 0d, total = 0d;

        for (var k = (int)Math.Ceiling(Lowest / perBin); k < power.Length; k++)
        {
            weighted += k * perBin * power[k];
            total += power[k];
        }

        return total <= 0d ? 0d : weighted / total;
    }

    private static string Tones(double[] power, double perBin)
    {
        var tones = new List<(double Frequency, double Level)>();

        for (var k = Math.Max((int)Math.Ceiling(Lowest / perBin), Reach); k < power.Length - Reach - 1; k++)
        {
            if (!IsPeak(power, k) || power[k] < Prominence * Around(power, k)) continue;

            // The parabola through the log amplitudes either side puts the pitch
            // between bins, where a 3 Hz grid would put a 440 Hz note at 439.5.
            if (power[k - 1] <= 0d || power[k + 1] <= 0d)
            {
                tones.Add((k * perBin, power[k]));
                continue;
            }

            var below = Math.Log(power[k - 1]);
            var at = Math.Log(power[k]);
            var above = Math.Log(power[k + 1]);
            var offset = 0.5d * (below - above) / (below - 2d * at + above);

            tones.Add(((k + offset) * perBin, Math.Exp(at - 0.25d * (below - above) * offset)));
        }

        if (tones.Count == 0)
        {
            return "No distinct tones: the energy is spread across the spectrum, as in noise or "
                + "a sound that moves too fast to hold a pitch.";
        }

        var strongest = tones.Max(tone => tone.Level);
        var named = tones
            .Where(tone => tone.Level >= Floor * strongest)
            .OrderByDescending(tone => tone.Level)
            .Take(MostTones)
            .OrderBy(tone => tone.Frequency)
            .Select(tone => $"{Hertz(tone.Frequency)} {Math.Round(10 * Math.Log10(tone.Level / strongest)).ToString("0", CultureInfo.InvariantCulture)} dB");

        return "Tones, narrow peaks standing 10 dB or more above what is around them, in dB from "
            + "the strongest: " + string.Join(", ", named) + ". A note shows as a pitch with "
            + "harmonics at whole multiples of it; listen to a shorter stretch to read one note "
            + "out of a tune.";
    }

    /// <summary>Whether bin <paramref name="k"/> is the loudest within <see cref="Reach"/> bins either way.</summary>
    private static bool IsPeak(double[] power, int k)
    {
        for (var other = k - Reach; other <= k + Reach; other++)
        {
            if (other == k) continue;

            if (power[other] > power[k] || (other < k && power[other] == power[k])) return false;
        }

        return true;
    }

    /// <summary>The mean power from <see cref="Reach"/> to <see cref="Surround"/> bins either side of <paramref name="k"/>.</summary>
    private static double Around(double[] power, int k)
    {
        var total = 0d;
        var count = 0;

        for (var other = Math.Max(k - Surround, 1); other <= Math.Min(k + Surround, power.Length - 1); other++)
        {
            if (Math.Abs(other - k) <= Reach) continue;

            total += power[other];
            count++;
        }

        return count == 0 ? 0d : total / count;
    }

    private static string Hertz(double frequency) => frequency switch
    {
        >= 1000d => (frequency / 1000d).ToString("0.##", CultureInfo.InvariantCulture) + " kHz",
        >= 100d => frequency.ToString("0", CultureInfo.InvariantCulture) + " Hz",
        _ => frequency.ToString("0.#", CultureInfo.InvariantCulture) + " Hz",
    };
}
