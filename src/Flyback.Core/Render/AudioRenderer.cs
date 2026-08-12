using System.Runtime.CompilerServices;
using Flyback.Core.Compile;

namespace Flyback.Core.Render;

/// <summary>
/// How the audio program's x and y inputs are driven.
/// </summary>
/// <param name="Scan">
/// False pins x and y to zero, so audio is a pure function of time the way a
/// modular synth is. True sweeps the image instead, and you hear the picture.
/// </param>
/// <param name="Rate">Horizontal sweeps per second when scanning — this is the pitch.</param>
/// <param name="Aspect">Frame aspect, so the sweep covers the same x range the renderer does.</param>
public readonly record struct AudioScan(bool Scan, float Rate, float Aspect)
{
    /// <summary>The vertical position drifts at a fixed rate so that pitch and timbre stay independent.</summary>
    public const float VerticalDriftHz = 0.5f;

    public static AudioScan TimeDriven => new(false, 0f, 1f);
}

/// <summary>
/// Evaluates a compiled audio program one sample at a time and decimates from an
/// oversampled internal rate.
/// </summary>
/// <remarks>
/// Deliberately single-threaded, unlike <see cref="SynthRenderer"/>. Stereo at
/// 48 kHz with 4x oversampling is ~192k evaluations a second against video's
/// ~31M, so there is nothing to gain by splitting it up — and this runs on an
/// audio callback, where blocking on a parallel loop is exactly what must not
/// happen. The asymmetry between the two renderers is intentional.
/// </remarks>
public sealed class AudioRenderer
{
    private const int Taps = 64;
    private const float DcBlockerPole = 0.9993f;

    private readonly float[][] delayLines = [new float[Taps], new float[Taps]];
    private readonly float[] filterTaps;
    private readonly float[] dcPreviousInput = new float[2];
    private readonly float[] dcPreviousOutput = new float[2];

    private float[] registerBank = new float[64];
    private int historyPosition;

    public AudioRenderer(int sampleRate = 48_000, int oversample = 4)
    {
        SampleRate = sampleRate;
        Oversample = Math.Max(1, oversample);
        filterTaps = DesignLowpass(Taps, 0.45 / Oversample);
    }

    public int SampleRate { get; }

    /// <summary>Internal rate multiplier. 1 disables both oversampling and the decimation filter.</summary>
    public int Oversample { get; }

    /// <summary>Sample-accurate position on the timeline. Audio cannot be advanced by wall-clock deltas.</summary>
    public double Time { get; private set; }

    /// <summary>Clears filter state and rewinds. Equivalent to the video renderer's Reset.</summary>
    public void Reset()
    {
        Time = 0;
        historyPosition = 0;
        Array.Clear(delayLines[0]);
        Array.Clear(delayLines[1]);
        Array.Clear(dcPreviousInput);
        Array.Clear(dcPreviousOutput);
    }

    public void SeekTo(double seconds) => Time = seconds;

    /// <summary>
    /// Sizes the register scratch for a program. Call this when swapping in a
    /// recompiled patch, off the audio thread — <see cref="Render"/> calls it
    /// too, but only as a backstop, and there it would allocate in the callback.
    /// </summary>
    public void Prepare(CompiledPatch program)
    {
        var needed = Math.Max(program.RegisterCount, program.OutputWidth);
        if (registerBank.Length < needed) registerBank = new float[needed];
    }

    /// <summary>
    /// Fills an interleaved stereo buffer. Allocation-free once constructed, so
    /// it is safe to call from an audio callback.
    /// </summary>
    public void Render(CompiledPatch program, Span<float> interleavedStereo, in AudioScan scan)
    {
        var frames = interleavedStereo.Length / 2;
        if (frames == 0) return;

        Prepare(program);
        var registers = registerBank;

        var left = program.OutputBase;
        var right = program.OutputWidth > 1 ? program.OutputBase + 1 : program.OutputBase;

        var innerStep = 1.0 / (SampleRate * Oversample);
        var outerStep = 1.0 / SampleRate;

        for (var frame = 0; frame < frames; frame++)
        {
            for (var k = 0; k < Oversample; k++)
            {
                var t = Time + k * innerStep;
                var (x, y) = Position(t, scan);

                // Video feedback has no meaning here: there is no previous frame
                // on the audio timeline, so SampleFeedback reads silence.
                program.Evaluate(x, y, (float)t, registers, default);

                delayLines[0][historyPosition] = registers[left];
                delayLines[1][historyPosition] = registers[right];
                historyPosition = (historyPosition + 1) % Taps;
            }

            interleavedStereo[frame * 2 + 0] = Finish(0);
            interleavedStereo[frame * 2 + 1] = Finish(1);

            Time += outerStep;
        }
    }

    /// <summary>
    /// Where the patch is "looking" at time <paramref name="t"/>. The horizontal
    /// sweep sets the pitch; the vertical position drifts at a fixed rate so
    /// changing pitch does not also change how fast the timbre evolves.
    /// </summary>
    private static (float X, float Y) Position(double t, in AudioScan scan)
    {
        if (!scan.Scan) return (0f, 0f);

        var horizontal = Fract(t * scan.Rate);
        var vertical = Fract(t * AudioScan.VerticalDriftHz);

        return (
            (float)((horizontal * 2.0 - 1.0) * scan.Aspect),
            (float)(1.0 - vertical * 2.0));
    }

    /// <summary>Decimate, remove DC, then clamp to what a speaker can be asked for.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float Finish(int channel)
    {
        var decimated = Oversample == 1
            ? delayLines[channel][(historyPosition - 1 + Taps) % Taps]
            : Convolve(channel);

        // One-pole DC blocker. A patch holding a constant is trivially easy to
        // build and is pure DC — inaudible, but it eats headroom and thumps.
        var blocked = decimated - dcPreviousInput[channel] + DcBlockerPole * dcPreviousOutput[channel];
        dcPreviousInput[channel] = decimated;
        dcPreviousOutput[channel] = blocked;

        return float.IsFinite(blocked) ? Math.Clamp(blocked, -1f, 1f) : 0f;
    }

    private float Convolve(int channel)
    {
        var history = delayLines[channel];
        var taps = filterTaps;
        var sum = 0f;
        var index = historyPosition;

        // Walk backwards from the newest sample through the delay line.
        for (var i = 0; i < Taps; i++)
        {
            index = index == 0 ? Taps - 1 : index - 1;
            sum += history[index] * taps[i];
        }

        return sum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Fract(double v) => v - Math.Floor(v);

    /// <summary>
    /// Blackman-windowed sinc. Oversampling reduces aliasing rather than removing
    /// it — the nonlinearity still folds energy down, this just pushes what
    /// survives far below the noise floor.
    /// </summary>
    private static float[] DesignLowpass(int taps, double cutoff)
    {
        var h = new float[taps];
        var middle = (taps - 1) / 2.0;
        var sum = 0.0;

        for (var n = 0; n < taps; n++)
        {
            var x = n - middle;

            // x is an integer minus (taps-1)/2, so it is exactly representable
            // and reaches zero exactly — at the centre tap, when taps is odd.
            // At the current even Taps it never does, so this branch is there to
            // keep the sinc singularity handled if that constant ever changes.
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            var sinc = x == 0
                ? 2 * cutoff
                : Math.Sin(2 * Math.PI * cutoff * x) / (Math.PI * x);

            var window = 0.42
                - 0.5 * Math.Cos(2 * Math.PI * n / (taps - 1))
                + 0.08 * Math.Cos(4 * Math.PI * n / (taps - 1));

            h[n] = (float)(sinc * window);
            sum += h[n];
        }

        // Normalise to unity gain at DC.
        for (var n = 0; n < taps; n++) h[n] = (float)(h[n] / sum);

        return h;
    }
}
