namespace Flyback.Cli;

/// <summary>The bars the site's player draws for a track.</summary>
internal static class Peaks
{
    public const int Count = 96;

    private const double Floor = 0.10;

    /// <summary>
    /// The loudness of each of <see cref="Count"/> equal slices, as a share of the
    /// loudest, never below <see cref="Floor"/>, to two places. Null for silence.
    /// </summary>
    public static double[]? Of(ReadOnlySpan<float> samples)
    {
        if (samples.Length < Count) return null;

        var rms = new double[Count];

        for (var slice = 0; slice < Count; slice++)
        {
            var from = (int)((long)samples.Length * slice / Count);
            var to = (int)((long)samples.Length * (slice + 1) / Count);

            var sum = 0d;
            for (var i = from; i < to; i++) sum += samples[i] * (double)samples[i];

            rms[slice] = Math.Sqrt(sum / Math.Max(1, to - from));
        }

        var loudest = rms.Max();

        if (loudest < 1e-5) return null;

        return [.. rms.Select(r => Math.Round(Math.Max(Floor, r / loudest), 2))];
    }

    /// <summary>Samples ffmpeg wrote as little-endian 32-bit floats.</summary>
    public static float[] Decoded(byte[] raw)
    {
        var samples = new float[raw.Length / sizeof(float)];

        Buffer.BlockCopy(raw, 0, samples, 0, samples.Length * sizeof(float));

        return samples;
    }
}
