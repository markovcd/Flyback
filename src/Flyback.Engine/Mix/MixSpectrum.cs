using System.Numerics;
using Flyback.Engine.Compile;

namespace Flyback.Engine.Mix;

/// <summary>Where a stereo clip's power sits: how much below 60 Hz, how much from 200 to 500 Hz, and how wide the bass is.</summary>
/// <param name="Sub">The share of the power from 20 to 60 Hz, in dB.</param>
/// <param name="Bass">The share from 60 to 200 Hz, in dB.</param>
/// <param name="LowMids">The share from 200 to 500 Hz, in dB.</param>
/// <param name="Upper">The share from 500 Hz up, in dB.</param>
/// <param name="LowEnd">The share from 20 to 120 Hz, in dB.</param>
/// <param name="LowEndWidth">The side's power from 20 to 120 Hz against the middle's, in dB.</param>
internal sealed record MixSpectrum(double Sub, double Bass, double LowMids, double Upper, double LowEnd, double LowEndWidth)
{
    /// <summary>The longest transform: about 6 Hz a bin at 48 kHz, so the sub band spans several.</summary>
    private const int Segment = 8192;

    /// <summary>A clip shorter than this many frames is too short to read the bass of.</summary>
    private const int Shortest = 2048;

    /// <summary>Where the bands meet, in hertz.</summary>
    private const double Bottom = 20d, SubTop = 60d, BassTop = 200d, LowMidTop = 500d, LowEndTop = 120d;

    /// <summary>The spectrum of an interleaved stereo clip, or null where it is too short to read.</summary>
    public static MixSpectrum? Of(ReadOnlySpan<float> samples, int sampleRate)
    {
        var frames = samples.Length / 2;

        if (frames < Shortest) return null;

        var mid = new float[frames];
        var side = new float[frames];

        for (var i = 0; i < frames; i++)
        {
            mid[i] = 0.5f * (samples[2 * i] + samples[2 * i + 1]);
            side[i] = 0.5f * (samples[2 * i] - samples[2 * i + 1]);
        }

        var length = 1 << BitOperations.Log2((uint)Math.Min(frames, Segment));
        var middle = Spectra.Amplitudes(mid, length);
        var sides = Spectra.Amplitudes(side, length);
        var perBin = (double)sampleRate / length;

        double total = 0d, sub = 0d, bass = 0d, lowMids = 0d, upper = 0d, lowMid = 0d, lowSide = 0d;

        for (var k = 0; k < middle.Length; k++)
        {
            var hertz = k * perBin;

            if (hertz < Bottom) continue;

            var m = middle[k] * middle[k];
            var s = sides[k] * sides[k];
            var power = m + s;

            total += power;

            switch (hertz)
            {
                case < SubTop: sub += power; break;
                case < BassTop: bass += power; break;
                case < LowMidTop: lowMids += power; break;
                default: upper += power; break;
            }

            if (hertz < LowEndTop)
            {
                lowMid += m;
                lowSide += s;
            }
        }

        return new MixSpectrum(
            Share(sub, total),
            Share(bass, total),
            Share(lowMids, total),
            Share(upper, total),
            Share(lowMid + lowSide, total),
            Share(lowSide, lowMid));
    }

    /// <summary>A part against a whole in dB: negative infinity for no part, positive for a part with no whole, as a side with no middle.</summary>
    private static double Share(double part, double whole) =>
        part <= 0d ? double.NegativeInfinity
        : whole <= 0d ? double.PositiveInfinity
        : 10d * Math.Log10(part / whole);
}
