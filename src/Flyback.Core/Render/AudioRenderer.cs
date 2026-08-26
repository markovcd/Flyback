using System.Runtime.CompilerServices;
using Flyback.Core.Compile;
using Flyback.Core.Graph;

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

    /// <summary>
    /// What the Output of <paramref name="patch"/> asks for, read off its knobs.
    /// </summary>
    /// <remarks>
    /// Every caller that renders a patch offline needs this and none of them
    /// should work it out again — it is two socket indices and a threshold, and
    /// three places disagreeing about which is which is exactly the silent kind
    /// of wrong.
    /// <para>
    /// The knobs rather than the signals: a value patched into 'scan' arrives per
    /// sample, and this is a property of the whole render. Sweeping the sweep is
    /// the one thing the sockets cannot do, here as on screen.
    /// </para>
    /// </remarks>
    /// <param name="patch"></param>
    /// <param name="aspect">
    /// The frame the sweep should cover, which belongs to whoever is rendering
    /// rather than to the patch: an export at one size and a preview at another
    /// hear the same picture across a different width.
    /// </param>
    /// <param name="modules"></param>
    public static AudioScan For(Patch patch, float aspect, ModuleCatalog? modules = null)
    {
        var sink = patch.FirstOf(NodeCatalog.OutputTypeId);
        var def = (modules ?? NodeCatalog.Current).Get(NodeCatalog.OutputTypeId);

        if (sink is null || def is null) return TimeDriven;

        return new AudioScan(
            Knob(NodeCatalog.OutputScanPort) >= 0.5f,
            MathF.Max(Knob(NodeCatalog.OutputScanRatePort), 1f),
            aspect);

        // The instance's value, the definition's default for a file saved before
        // the socket existed, and zero for a definition that has since lost it.
        float Knob(int port) =>
            port < sink.InputValues.Length ? sink.InputValues[port]
            : port < def.Inputs.Count ? def.Inputs[port].Default
            : 0f;
    }
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
    public const int DefaultSampleRate = 48_000;
    
    private const int Taps = 64;
    private const float DcBlockerPole = 0.9993f;

    private readonly float[][] delayLines = [new float[Taps], new float[Taps]];
    private readonly float[] filterTaps;
    private readonly float[] dcPreviousInput = new float[2];
    private readonly float[] dcPreviousOutput = new float[2];

    private double[] registerBank = new double[64];
    private int historyPosition;

    /// <summary>
    /// Delay lines for callers that do not manage their own — offline rendering
    /// and tests, where nothing swaps a program underneath us.
    /// </summary>
    private DelayState? delays;

    public AudioRenderer(int sampleRate = DefaultSampleRate, int oversample = 4)
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
        delays?.Clear();
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
        if (registerBank.Length < needed) registerBank = new double[needed];
    }

    /// <summary>
    /// Memory for a program, reusing what is already there when the shape has not
    /// changed — so turning a knob keeps the tail ringing and the oscillators in
    /// phase, and only adding or removing a stateful op cuts either.
    /// </summary>
    /// <remarks>
    /// Handed back rather than stored, because which lines a program needs is a
    /// property of *that* program. A caller swapping programs under a live
    /// callback has to swap both together or the old program will index into the
    /// new program's lines — see the state record in <c>AudioEngine</c>. Lines run
    /// at the oversampled rate, because that is how often the program is
    /// evaluated.
    /// </remarks>
    public DelayState? DelayMemoryFor(CompiledPatch program, DelayState? existing = null)
    {
        if (program.DelayLengths.Count == 0
            && program.PhaseCount == 0
            && program.UnitCount == 0
            && program.TraceCount == 0)
            return null;

        var rate = SampleRate * Oversample;

        return existing?.Fits(
            program.DelayLengths, rate, program.PhaseCount, program.UnitCount, program.TraceCount) == true
            ? existing
            : new DelayState(
                program.DelayLengths, rate, program.PhaseCount, program.UnitCount, program.TraceCount);
    }

    /// <summary>
    /// Fills an interleaved stereo buffer. Allocation-free once constructed, so
    /// it is safe to call from an audio callback.
    /// </summary>
    /// <param name="scan">Whether to sweep the image instead of running on time alone, and how fast.</param>
    /// <param name="memory">
    /// The program's delay lines. Pass them explicitly from anywhere that swaps
    /// programs while this is running, so the pair is always consistent; leave it
    /// null offline and this keeps its own, allocating them on the spot.
    /// </param>
    /// <param name="program">The sound's own compiled program, rooted at the Output's left and right.</param>
    /// <param name="live">
    /// What is being played into the program while this buffer is filled. Read
    /// once here rather than per sample, so every sample of one buffer hears the
    /// same moment — a key that moved halfway through is heard at the start of
    /// the next one, which is a few milliseconds late and never half a note.
    /// </param>
    /// <param name="interleavedStereo">Where the samples go, left and right alternating. Its length decides how many frames this call renders.</param>
    public void Render(
        CompiledPatch program,
        Span<float> interleavedStereo,
        in AudioScan scan,
        DelayState? memory = null,
        LiveValues? live = null)
    {
        var frames = interleavedStereo.Length / 2;
        if (frames == 0) return;

        Prepare(program);
        var registers = registerBank;

        // Read once. Re-reading a field per sample would let a swap take effect
        // halfway through a buffer, which is the one place it must not.
        var lines = memory ?? Own(program);

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
                // on the audio timeline, so SampleFeedback reads silence. Delay
                // lines are the other way round — this is the only path that has
                // them, because it is the only one that runs in order.
                //
                // t goes in at full width. Narrowing it here is what ADR-0032
                // removed: two consecutive sample times an hour into a session
                // are the same float, and an oscillator measuring how far its
                // input moved would be handed a staircase to run on.
                // The frame goes in even here. Nothing the speakers reach is
                // drawn into one, but a scanned patch sweeps x across exactly
                // this width — so a module asking how far x reaches is told the
                // same thing on both paths rather than a different picture per
                // sink.
                program.Evaluate(x, y, t, registers, default, lines, scan.Aspect, live);

                delayLines[0][historyPosition] = (float)registers[left];
                delayLines[1][historyPosition] = (float)registers[right];
                historyPosition = (historyPosition + 1) % Taps;
            }

            interleavedStereo[frame * 2 + 0] = Finish(0);
            interleavedStereo[frame * 2 + 1] = Finish(1);

            Time += outerStep;
        }
    }

    /// <summary>
    /// Lines for a caller that did not bring any. Safe here only because such a
    /// caller is by definition not swapping programs concurrently.
    /// </summary>
    private DelayState? Own(CompiledPatch program) => delays = DelayMemoryFor(program, delays);

    /// <summary>
    /// Where the patch is "looking" at time <paramref name="t"/>. The horizontal
    /// sweep sets the pitch; the vertical position drifts at a fixed rate so
    /// changing pitch does not also change how fast the timbre evolves.
    /// </summary>
    private static (double X, double Y) Position(double t, in AudioScan scan)
    {
        if (!scan.Scan) return (0d, 0d);

        var horizontal = Fract(t * scan.Rate);
        var vertical = Fract(t * AudioScan.VerticalDriftHz);

        return (
            (horizontal * 2.0 - 1.0) * scan.Aspect,
            1.0 - vertical * 2.0);
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
