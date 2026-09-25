using System.Runtime.CompilerServices;
using Flyback.Core.Compile;

namespace Flyback.Core.Render;

/// <summary>
/// Evaluates a compiled audio program one sample at a time and decimates from an
/// oversampled internal rate.
/// </summary>
/// <remarks>
/// Deliberately single-threaded, unlike <see cref="SynthRenderer"/>: stereo at
/// 48 kHz with 4x oversampling is ~192k evaluations a second against video's
/// ~31M, and this runs on an audio callback, where blocking on a parallel loop is
/// what must not happen.
/// </remarks>
public sealed class AudioRenderer
{
    private const int DefaultSampleRate = GlobalConstants.SampleRate;
    
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

    /// <summary>
    /// The width over the height of the frame this is the sound of, which is what
    /// Coordinates' <c>aspect</c> reads here. The speakers have no frame of their
    /// own, so whoever renders says which picture they belong to — an export and a
    /// preview hear the same patch across a different width. Settable rather than
    /// fixed at construction: the live engine's renderer outlives any one preview
    /// size and follows it when it changes (ADR-0077).
    /// </summary>
    public float Aspect { get; set; } = 1f;

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
    /// Memory for a program, carrying over whatever of <paramref name="existing"/>
    /// belongs to a module this program still has — so an edit made while the
    /// sound is playing costs only the modules it touched.
    /// </summary>
    /// <remarks>
    /// Handed back rather than stored, because which lines a program needs is a
    /// property of that program: a caller swapping programs under a live callback
    /// has to swap both together or the old program will index into the new
    /// program's lines. Lines run at the oversampled rate. Matching is by owner
    /// rather than by slot — see <see cref="StateOwners"/> and
    /// <see cref="DelayState.Adopt"/>.
    /// </remarks>
    public DelayState? DelayMemoryFor(CompiledPatch program, DelayState? existing = null)
    {
        if (program.DelayLengths.Count == 0
            && program.PhaseCount == 0
            && program.UnitCount == 0
            && program.TraceCount == 0
            && program.PlaneCount == 0)
            return null;

        var rate = SampleRate * Oversample;

        // Nothing changed at all: the same object, so a knob turned on a patch
        // full of delay lines costs no allocation and no copy.
        if (existing is not null
            && existing.Fits(
                program.DelayLengths,
                rate,
                program.PhaseCount,
                program.UnitCount,
                program.TraceCount,
                program.PlaneCount)
            && existing.Owners.Match(program.Owners))
        {
            return existing;
        }

        var memory = new DelayState(program, rate);

        // Something did change, so this is a different program — but only some
        // of it is different. Every module the edit did not touch takes back its
        // own accumulator and its own line, and only what is genuinely new
        // begins from nothing. Without this, adding one oscillator restarts
        // every tone in the patch.
        if (existing is not null) memory.Adopt(existing);

        return memory;
    }

    /// <summary>
    /// The memory this renderer keeps for itself, and null until it has rendered
    /// something that needs any.
    /// </summary>
    /// <remarks>
    /// Only ever the offline case: a caller that swaps programs passes its own and
    /// this stays null. It is here so an export can read the rings back — a Meter
    /// offline is measured from the same tap a Meter on screen is, and there is
    /// nobody else offline to hold them. See <see cref="MovieRenderer"/>.
    /// </remarks>
    public DelayState? Memory => delays;

    /// <summary>
    /// Fills an interleaved stereo buffer. Allocation-free once constructed, so it
    /// is safe to call from an audio callback.
    /// </summary>
    /// <param name="memory">
    /// The program's delay lines. Pass them explicitly from anywhere that swaps
    /// programs while this is running; leave it null offline and this keeps its
    /// own.
    /// </param>
    /// <param name="program">The sound's own compiled program, rooted at the Output's left and right.</param>
    /// <param name="live">
    /// What is being played into the program while this buffer is filled. Read
    /// once here rather than per sample, so every sample of one buffer hears the
    /// same moment — a key that moved halfway through is a few milliseconds late
    /// and never half a note.
    /// </param>
    /// <param name="interleavedStereo">Where the samples go, left and right alternating. Its length decides how many frames this call renders.</param>
    internal void Render(
        CompiledPatch program,
        Span<float> interleavedStereo,
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

        // Once per buffer, like the memory: IL attached halfway through would
        // change nothing that can be heard, but a buffer is the natural grain.
        var il = program.Il;

        var left = program.OutputBase;
        var right = program.OutputWidth > 1 ? program.OutputBase + 1 : program.OutputBase;

        var innerStep = 1.0 / (SampleRate * Oversample);
        var outerStep = 1.0 / SampleRate;

        for (var frame = 0; frame < frames; frame++)
        {
            for (var k = 0; k < Oversample; k++)
            {
                var t = Time + k * innerStep;

                // Video feedback has no meaning here: there is no previous frame on
                // the audio timeline, so SampleFeedback reads silence. Delay lines
                // are the other way round — this is the only path that runs in
                // order.
                //
                // t goes in at full width (ADR-0032): two consecutive sample times
                // an hour in are the same float, and an oscillator measuring how
                // far its input moved would be handed a staircase. The ear is at no
                // pixel, so x and y are the origin — a Scan is what moves them, for
                // the branch it reads.
                if (il is null) program.Evaluate(0d, 0d, t, registers, default, lines, Aspect, live);
                else il.Evaluate(0d, 0d, t, registers, default, lines, Aspect, live);

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
            // and reaches zero exactly — at the center tap, when taps is odd.
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

        // Normalize to unity gain at DC.
        for (var n = 0; n < taps; n++) h[n] = (float)(h[n] / sum);

        return h;
    }
}
