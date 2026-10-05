using System.Numerics;
using Flyback.Core.Compile;
using Flyback.Engine.Compile;

namespace Flyback.Engine.Slopes;

/// <summary>What the slopes say about a picture's point or a sound's band, run the way the sinks run them.</summary>
internal static class SlopeReadings
{
    public const int FramesPerSecond = 30;

    /// <summary>The octaves a band reading is made in, by their middle.</summary>
    public static IReadOnlyList<double> Octaves { get; } = [31.5, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    /// <summary>
    /// Runs the picture on a grid from 0 to the frame at <paramref name="seconds"/>,
    /// so the frame before and each pixel's memory are as the screen would have them.
    /// </summary>
    /// <returns>The frame before, every pixel's planes, and the time of the frame the grid stopped at.</returns>
    public static (FeedbackFrame Before, float[] Planes, double Seconds) Settle(SlopeProgram sp, double seconds, int columns, int rows)
    {
        var program = sp.Program;
        var pixels = columns * rows;
        var aspect = (double)columns / rows;
        var planes = new float[pixels * program.PlaneCount];
        var remembers = program.PlaneCount > 0 || program.Ops.Any(op => op.Code is OpCode.SampleFeedback);

        if (!remembers) return (default, planes, seconds);

        var previous = new float[pixels * 3];
        var current = new float[pixels * 3];
        var registers = program.AllocateRegisters();
        var frames = (int)Math.Round(seconds * FramesPerSecond);

        for (var frame = 0; frame < frames; frame++)
        {
            var t = frame / (double)FramesPerSecond;
            var before = new FeedbackFrame(frame == 0 ? null : previous, columns, rows);

            for (var p = 0; p < pixels; p++)
            {
                var (x, y) = At(p, columns, rows);
                program.Evaluate(x, y, t, registers, before, null, aspect, sp.Live, planes.AsSpan(p * program.PlaneCount, program.PlaneCount));

                for (var c = 0; c < 3; c++)
                {
                    var value = registers[program.OutputBase + c];
                    current[p * 3 + c] = (float)Math.Clamp(double.IsFinite(value) ? value : 0d, 0d, 1d);
                }
            }

            (previous, current) = (current, previous);
        }

        return (new FeedbackFrame(frames == 0 ? null : previous, columns, rows), planes, frames / (double)FramesPerSecond);
    }

    /// <summary>Where a grid pixel's middle falls in the patch's coordinates.</summary>
    public static (double X, double Y) At(int pixel, int columns, int rows)
    {
        var (row, column) = Math.DivRem(pixel, columns);
        var aspect = (double)columns / rows;

        return ((2d * (column + 0.5d) / columns - 1d) * aspect, 1d - 2d * (row + 0.5d) / rows);
    }

    /// <summary>The grid pixel nearest a point in the patch's coordinates.</summary>
    public static int Nearest(double x, double y, int columns, int rows)
    {
        var aspect = (double)columns / rows;
        var column = Math.Clamp((int)((x / aspect + 1d) * 0.5d * columns), 0, columns - 1);
        var row = Math.Clamp((int)((1d - y) * 0.5d * rows), 0, rows - 1);

        return row * columns + column;
    }

    /// <summary>
    /// Runs the sound from 0 to <paramref name="steps"/> evaluations and reads the
    /// last <paramref name="window"/> of the left channel: each octave's level in
    /// decibels, and how many decibels it moves per unit of each lane's knob.
    /// </summary>
    public static (double[] Levels, double[,] Slopes) Bands(SlopeProgram sp, int[] knobs, int rate, long steps, int window)
    {
        var program = sp.Program;
        var delays = new DelayState(program, rate);
        var slopes = new Slopes(sp, knobs);
        var memory = new SlopeMemory(program, delays, slopes.Lanes);
        var signal = new Complex[window];
        var tangent = new Complex[slopes.Lanes][];

        for (var j = 0; j < slopes.Lanes; j++) tangent[j] = new Complex[window];

        for (long step = 0; step < steps; step++)
        {
            slopes.Sample(step / (double)rate, 16d / 9d, delays, memory);

            var n = step - (steps - window);
            if (n < 0) continue;

            var w = Hann(n, window);
            signal[n] = Finite(slopes.Value(0)) * w;
            for (var j = 0; j < slopes.Lanes; j++) tangent[j][n] = slopes.Output(0)[j] * w;
        }

        Fft(signal);
        for (var j = 0; j < slopes.Lanes; j++) Fft(tangent[j]);

        var levels = new double[Octaves.Count];
        var result = new double[slopes.Lanes, Octaves.Count];

        for (var band = 0; band < Octaves.Count; band++)
        {
            var (low, high) = Bins(band, rate, window);
            var power = 0d;

            for (var bin = low; bin <= high; bin++) power += signal[bin].Magnitude * signal[bin].Magnitude;

            levels[band] = 10d * Math.Log10(power + 1e-30);

            for (var j = 0; j < slopes.Lanes; j++)
            {
                var moved = 0d;

                for (var bin = low; bin <= high; bin++)
                    moved += 2d * (Complex.Conjugate(signal[bin]) * tangent[j][bin]).Real;

                result[j, band] = 10d / Math.Log(10d) * moved / (power + 1e-30);
            }
        }

        return (levels, result);
    }

    /// <summary>The same levels with no slopes, through the plain interpreter.</summary>
    public static double[] Levels(SlopeProgram sp, int rate, long steps, int window)
    {
        var program = sp.Program;
        var delays = new DelayState(program, rate);
        var registers = program.AllocateRegisters();
        var signal = new Complex[window];

        for (long step = 0; step < steps; step++)
        {
            program.Evaluate(0d, 0d, step / (double)rate, registers, default, delays, 16d / 9d, sp.Live);

            var n = step - (steps - window);
            if (n >= 0) signal[n] = Finite(registers[program.OutputBase]) * Hann(n, window);
        }

        Fft(signal);

        var levels = new double[Octaves.Count];

        for (var band = 0; band < Octaves.Count; band++)
        {
            var (low, high) = Bins(band, rate, window);
            var power = 0d;

            for (var bin = low; bin <= high; bin++) power += signal[bin].Magnitude * signal[bin].Magnitude;

            levels[band] = 10d * Math.Log10(power + 1e-30);
        }

        return levels;
    }

    /// <summary>The octave whose middle is nearest <paramref name="hz"/>.</summary>
    public static int Octave(double hz) =>
        Enumerable.Range(0, Octaves.Count).MinBy(band => Math.Abs(Math.Log2(Octaves[band] / Math.Max(hz, 1d))));

    private static (int Low, int High) Bins(int band, int rate, int window)
    {
        var low = (int)Math.Ceiling(Octaves[band] / Math.Sqrt(2d) * window / rate);
        var high = (int)Math.Floor(Octaves[band] * Math.Sqrt(2d) * window / rate);

        return (Math.Max(1, low), Math.Min(window / 2, Math.Max(low, high)));
    }

    private static double Hann(long n, int window) => 0.5d - 0.5d * Math.Cos(2d * Math.PI * n / (window - 1));

    private static double Finite(double v) => double.IsFinite(v) ? v : 0d;

    private static void Fft(Complex[] a)
    {
        var n = a.Length;

        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (a[i], a[j]) = (a[j], a[i]);
        }

        for (var length = 2; length <= n; length <<= 1)
        {
            var angle = -2d * Math.PI / length;
            var turn = new Complex(Math.Cos(angle), Math.Sin(angle));

            for (var i = 0; i < n; i += length)
            {
                var w = Complex.One;

                for (var j = 0; j < length / 2; j++)
                {
                    var u = a[i + j];
                    var v = a[i + j + length / 2] * w;
                    a[i + j] = u + v;
                    a[i + j + length / 2] = u - v;
                    w *= turn;
                }
            }
        }
    }
}
