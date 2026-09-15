using System.Buffers;
using System.Numerics;

namespace Flyback.Core.Compile;

/// <summary>
/// What an Analyzer charts: the frequency content of a stretch of what the
/// speakers played, laid out across a chart's buffer on a logarithmic axis.
/// </summary>
/// <remarks>
/// The other transformation <see cref="Traces.Refresh"/> can do on the way from a
/// ring to a buffer. A Scope's is a resampling; this one is an averaged
/// periodogram — Hann-windowed segments of the window, their power averaged, and
/// the root of that read off at a frequency per point. Once a frame and outside
/// both programs, like everything else a tap feeds.
/// <para>
/// What lands in the buffer is linear amplitude, calibrated so a sine of
/// amplitude one reads one at its frequency. Decibels are the module's business,
/// because the table read returns silence beyond the ends of a buffer, and
/// silence has to mean nothing there rather than nought decibels.
/// </para>
/// </remarks>
public static class Spectra
{
    /// <summary>The frequency at the left-hand edge of the chart, in hertz.</summary>
    public const double Lowest = 20d;

    /// <summary>The frequency at the right-hand edge, in hertz.</summary>
    public const double Highest = 20_000d;

    /// <summary>
    /// The longest segment the window is cut into, in evaluations: about 85 ms at
    /// the oversampled rate, which puts a bin every 12 Hz.
    /// </summary>
    /// <remarks>
    /// The resolution, fixed rather than grown with the window, because a longer
    /// window asks for a steadier reading rather than a finer one — and a single
    /// transform the length of thirty seconds would be six million points long.
    /// Coarse at the bottom of a log axis, where the first octave is two bins
    /// wide, and that is the honest price of a picture that moves at frame rate.
    /// </remarks>
    public const int Segment = 16_384;

    /// <summary>
    /// The shortest segment, for a window shorter than <see cref="Segment"/>.
    /// Below this the bins are wider than the chart's first two decades together.
    /// </summary>
    private const int ShortestSegment = 256;

    /// <summary>
    /// How many segments a window is averaged over at most. Half-overlapping
    /// until the window is long enough to need more than this, and spread evenly
    /// across it after — a long window is sampled rather than walked, which keeps
    /// what a frame costs independent of the knob.
    /// </summary>
    public const int MaxSegments = 8;

    /// <summary>The frequency a point of a buffer <paramref name="points"/> long stands for.</summary>
    public static double FrequencyAt(int point, int points) =>
        Lowest * Math.Pow(Highest / Lowest, point / (double)Math.Max(points - 1, 1));

    /// <summary>Where along a buffer <paramref name="points"/> long a frequency falls, in points.</summary>
    public static double PointOf(double hertz, int points) =>
        Math.Log(hertz / Lowest) / Math.Log(Highest / Lowest) * (points - 1);

    /// <summary>
    /// Lays the spectrum of the newest <paramref name="span"/> evaluations of a
    /// trace across <paramref name="into"/>, lowest frequency first.
    /// </summary>
    /// <remarks>
    /// A point spanning several bins takes the loudest of them, for the reason a
    /// Scope's bucket takes its peak: a tone falling between two points must not
    /// vanish from the chart. A point narrower than a bin — the bottom of the axis
    /// — is interpolated between the two bins either side of it.
    /// </remarks>
    public static void Chart(DelayState memory, int slot, Span<float> into, int span)
    {
        ArgumentNullException.ThrowIfNull(memory);

        if (into.Length == 0) return;

        var length = Math.Clamp(
            1 << BitOperations.Log2((uint)Math.Max(span, 1)), ShortestSegment, Segment);
        var bins = length / 2 + 1;

        // Half-overlapping, which a Hann window is flat under, up to the most a
        // frame will pay for; evenly spread across whatever is left beyond that.
        var reach = Math.Max(span - length, 0);
        var segments = Math.Clamp(1 + (reach + length / 2 - 1) / (length / 2), 1, MaxSegments);

        var samples = ArrayPool<float>.Shared.Rent(length);
        var real = ArrayPool<double>.Shared.Rent(length);
        var imaginary = ArrayPool<double>.Shared.Rent(length);
        var power = ArrayPool<double>.Shared.Rent(bins);

        try
        {
            Array.Clear(power, 0, bins);

            for (var s = 0; s < segments; s++)
            {
                var age = segments == 1 ? 0 : (int)((long)reach * s / (segments - 1));

                memory.ReadTrace(slot, samples.AsSpan(0, length), age);

                for (var n = 0; n < length; n++)
                {
                    real[n] = samples[n] * (0.5d - 0.5d * Math.Cos(2d * Math.PI * n / length));
                    imaginary[n] = 0d;
                }

                Transform(real.AsSpan(0, length), imaginary.AsSpan(0, length));

                for (var k = 0; k < bins; k++)
                    power[k] += real[k] * real[k] + imaginary[k] * imaginary[k];
            }

            // A periodic Hann window sums to half its length, and a sine's energy
            // is split between a positive and a negative bin, so four over the
            // length is what brings a full-scale tone back to one.
            var calibration = 4d / length;
            var perBin = (double)memory.SampleRate / length;

            // Halfway to the neighboring points in either direction, on the log
            // axis, which is the stretch of spectrum a point stands for.
            var halfStep = Math.Pow(Highest / Lowest, 0.5d / Math.Max(into.Length - 1, 1));

            for (var i = 0; i < into.Length; i++)
            {
                var center = FrequencyAt(i, into.Length) / perBin;
                var from = (int)Math.Ceiling(center / halfStep);
                var to = (int)Math.Floor(center * halfStep);

                double amplitude;

                if (from > to)
                {
                    var below = (int)center;
                    var fraction = center - below;

                    amplitude = (1d - fraction) * Amplitude(below) + fraction * Amplitude(below + 1);
                }
                else
                {
                    amplitude = 0d;
                    for (var k = from; k <= to; k++) amplitude = Math.Max(amplitude, Amplitude(k));
                }

                into[i] = (float)amplitude;
            }

            double Amplitude(int bin) =>
                (uint)bin < (uint)bins ? Math.Sqrt(power[bin] / segments) * calibration : 0d;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(samples);
            ArrayPool<double>.Shared.Return(real);
            ArrayPool<double>.Shared.Return(imaginary);
            ArrayPool<double>.Shared.Return(power);
        }
    }

    /// <summary>
    /// An in-place radix-2 Fourier transform, unscaled. The length must be a power
    /// of two, which <see cref="Chart"/> guarantees.
    /// </summary>
    private static void Transform(Span<double> real, Span<double> imaginary)
    {
        var n = real.Length;

        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;

            if (i >= j) continue;

            (real[i], real[j]) = (real[j], real[i]);
            (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
        }

        for (var size = 2; size <= n; size <<= 1)
        {
            var angle = -2d * Math.PI / size;
            var stepReal = Math.Cos(angle);
            var stepImaginary = Math.Sin(angle);
            var half = size >> 1;

            for (var start = 0; start < n; start += size)
            {
                var turnReal = 1d;
                var turnImaginary = 0d;

                for (var k = 0; k < half; k++)
                {
                    var a = start + k;
                    var b = a + half;

                    var productReal = real[b] * turnReal - imaginary[b] * turnImaginary;
                    var productImaginary = real[b] * turnImaginary + imaginary[b] * turnReal;

                    real[b] = real[a] - productReal;
                    imaginary[b] = imaginary[a] - productImaginary;
                    real[a] += productReal;
                    imaginary[a] += productImaginary;

                    var nextReal = turnReal * stepReal - turnImaginary * stepImaginary;
                    turnImaginary = turnReal * stepImaginary + turnImaginary * stepReal;
                    turnReal = nextReal;
                }
            }
        }
    }
}
