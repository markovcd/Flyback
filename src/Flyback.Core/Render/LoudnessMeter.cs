namespace Flyback.Core.Render;

/// <summary>
/// Measures a whole rendered sound the way ITU-R BS.1770-4 does: its integrated
/// loudness, gated, and its true peak.
/// </summary>
/// <remarks>
/// Fed interleaved samples as they are rendered, a block at a time, and read
/// once at the end. Integrated loudness needs the whole program: the relative
/// gate is ten below the loudness of everything above the absolute one, which
/// is not known until everything has been heard. So each 100 ms is kept as one
/// number, and the 400 ms blocks the standard gates are built from four of
/// them.
/// <para>
/// The K-weighting coefficients are libebur128's, worked out from an analog
/// shelf and highpass for whatever rate is asked for. At 48 kHz they are the
/// standard's own table, which is how ffmpeg's <c>ebur128</c> filter gets them
/// too. True peak is the standard's Annex 2: the sound upsampled four times
/// with a 48-tap interpolating filter, and the largest magnitude found.
/// </para>
/// </remarks>
public sealed class LoudnessMeter
{
    /// <summary>What a mean square of one reads as.</summary>
    private const double Offset = -0.691;

    /// <summary>Blocks quieter than this are not counted at all.</summary>
    private const double AbsoluteGate = -70d;

    /// <summary>Blocks this far below the ungated loudness are not counted.</summary>
    private const double RelativeGate = -10d;

    /// <summary>The upsampling factor true peak is measured at.</summary>
    private const int Phases = 4;

    /// <summary>Taps of the interpolating filter per phase: 48 in all.</summary>
    private const int Taps = 12;

    private readonly int channels;
    private readonly int segment;

    private readonly Biquad[] shelves;
    private readonly Biquad[] highpasses;

    private readonly double[][] history;
    private readonly int[] heads;
    private static readonly double[][] Interpolator = Design();

    private readonly List<double> segments = [];
    private double energy;
    private int filled;

    /// <param name="sampleRate">Frames a second.</param>
    /// <param name="channels">
    /// Interleaved channels. Each counts at a weight of one, which is the
    /// standard's weight for left, right and center.
    /// </param>
    public LoudnessMeter(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);

        this.channels = channels;
        segment = Math.Max(1, sampleRate / 10);

        var (shelf, highpass) = Weighting(sampleRate);
        shelves = [.. Enumerable.Range(0, channels).Select(_ => shelf)];
        highpasses = [.. Enumerable.Range(0, channels).Select(_ => highpass)];

        history = [.. Enumerable.Range(0, channels).Select(_ => new double[Taps])];
        heads = new int[channels];
    }

    /// <summary>The largest sample magnitude, before any upsampling.</summary>
    public double SamplePeak { get; private set; }

    /// <summary>The largest magnitude between samples as well as at them, as a gain.</summary>
    public double TruePeak { get; private set; }

    /// <summary>
    /// The integrated loudness in LUFS, or negative infinity when nothing was
    /// loud enough to pass the absolute gate.
    /// </summary>
    public double Integrated
    {
        get
        {
            var blocks = Blocks().Where(b => Lufs(b) > AbsoluteGate).ToArray();
            if (blocks.Length == 0) return double.NegativeInfinity;

            var gate = Lufs(blocks.Average()) + RelativeGate;
            var counted = blocks.Where(b => Lufs(b) > gate).ToArray();

            return counted.Length == 0 ? double.NegativeInfinity : Lufs(counted.Average());
        }
    }

    /// <summary>Takes in interleaved frames, any number at a time.</summary>
    public void Add(ReadOnlySpan<float> interleaved)
    {
        for (var at = 0; at + channels <= interleaved.Length; at += channels)
        {
            for (var c = 0; c < channels; c++)
            {
                double sample = interleaved[at + c];

                SamplePeak = Math.Max(SamplePeak, Math.Abs(sample));
                TruePeak = Math.Max(TruePeak, Between(c, sample));

                var weighted = highpasses[c].Next(shelves[c].Next(sample));
                energy += weighted * weighted;
            }

            if (++filled < segment) continue;

            segments.Add(energy / segment);
            energy = 0d;
            filled = 0;
        }
    }

    /// <summary>The standard's 400 ms blocks, overlapping by three quarters.</summary>
    private IEnumerable<double> Blocks()
    {
        for (var i = 3; i < segments.Count; i++)
            yield return (segments[i - 3] + segments[i - 2] + segments[i - 1] + segments[i]) / 4d;
    }

    private static double Lufs(double meanSquare) => Offset + 10d * Math.Log10(meanSquare);

    /// <summary>
    /// The largest of the <see cref="Phases"/> values this sample and the ones
    /// before it interpolate to.
    /// </summary>
    private double Between(int channel, double sample)
    {
        var past = history[channel];
        var head = heads[channel] = (heads[channel] + 1) % Taps;
        past[head] = sample;

        var largest = 0d;

        foreach (var phase in Interpolator)
        {
            var sum = 0d;

            for (var t = 0; t < Taps; t++)
                sum += phase[t] * past[(head - t + Taps) % Taps];

            largest = Math.Max(largest, Math.Abs(sum));
        }

        return largest;
    }

    /// <summary>
    /// A windowed sinc cut at the original Nyquist, split into its polyphase
    /// components, each scaled to unity gain.
    /// </summary>
    private static double[][] Design()
    {
        const int length = Phases * Taps;
        var middle = (length - 1) / 2d;

        var phases = new double[Phases][];

        for (var p = 0; p < Phases; p++)
        {
            phases[p] = new double[Taps];

            for (var t = 0; t < Taps; t++)
            {
                var n = t * Phases + p;
                var x = (n - middle) / Phases;
                var sinc = x == 0d ? 1d : Math.Sin(Math.PI * x) / (Math.PI * x);

                // Kaiser at beta 8: sidelobes low enough that the interpolation, not
                // the filter, is what a peak reading is made of.
                phases[p][t] = sinc * Kaiser((n - middle) / middle, 8d);
            }

            var gain = phases[p].Sum();
            for (var t = 0; t < Taps; t++) phases[p][t] /= gain;
        }

        return phases;
    }

    private static double Kaiser(double position, double beta) =>
        Bessel(beta * Math.Sqrt(Math.Max(0d, 1d - position * position))) / Bessel(beta);

    /// <summary>The zeroth-order modified Bessel function, by its series.</summary>
    private static double Bessel(double x)
    {
        var sum = 1d;
        var term = 1d;

        for (var k = 1; k < 50; k++)
        {
            term *= x / (2d * k) * (x / (2d * k));
            sum += term;
        }

        return sum;
    }

    /// <summary>libebur128's K-weighting filters for <paramref name="rate"/>.</summary>
    private static (Biquad Shelf, Biquad Highpass) Weighting(int rate)
    {
        var f0 = 1681.974450955533;
        var g = 3.999843853973347;
        var q = 0.7071752369554196;

        var k = Math.Tan(Math.PI * f0 / rate);
        var vh = Math.Pow(10d, g / 20d);
        var vb = Math.Pow(vh, 0.4996667741545416);
        var a0 = 1d + k / q + k * k;

        var shelf = new Biquad(
            (vh + vb * k / q + k * k) / a0,
            2d * (k * k - vh) / a0,
            (vh - vb * k / q + k * k) / a0,
            2d * (k * k - 1d) / a0,
            (1d - k / q + k * k) / a0);

        f0 = 38.13547087602444;
        q = 0.5003270373238773;
        k = Math.Tan(Math.PI * f0 / rate);
        a0 = 1d + k / q + k * k;

        var highpass = new Biquad(1d, -2d, 1d, 2d * (k * k - 1d) / a0, (1d - k / q + k * k) / a0);

        return (shelf, highpass);
    }

    /// <summary>A direct-form-one biquad in doubles, where its coefficients keep their digits.</summary>
    private struct Biquad(double b0, double b1, double b2, double a1, double a2)
    {
        public readonly double B0 = b0, B1 = b1, B2 = b2, A1 = a1, A2 = a2;

        private double x1, x2, y1, y2;

        public double Next(double x)
        {
            var y = B0 * x + B1 * x1 + B2 * x2 - A1 * y1 - A2 * y2;

            x2 = x1;
            x1 = x;
            y2 = y1;
            y1 = y;

            return y;
        }
    }
}
