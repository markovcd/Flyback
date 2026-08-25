namespace Flyback.Core.Compile;

/// <summary>
/// Everything a program remembers between evaluations: the ring buffers behind
/// <see cref="OpCode.Delay"/> and <see cref="OpCode.Allpass"/>, and the running
/// phases behind <see cref="OpCode.Phase"/>.
/// </summary>
/// <remarks>
/// It belongs to the renderer rather than to the <see cref="CompiledPatch"/>,
/// for the same reason ADR-0018 keeps a program immutable: a recompile swaps the
/// program under the audio thread, and two programs may briefly both exist. State
/// that lived on the program would be duplicated or lost at that moment; state
/// that lives on the renderer simply carries on.
/// <para>
/// Which buffer or cell an op uses is its position among the ops of its kind,
/// counted as the program runs. Every op executes exactly once per evaluation and
/// always in the same order, so the count is exact without storing an index on
/// the op.
/// </para>
/// </remarks>
public sealed class DelayState
{
    /// <summary>
    /// The lines stay <see cref="float"/> while the registers around them are
    /// <see cref="double"/>: they hold a signal on its way to a speaker, and
    /// twenty-four bits of mantissa is already four more than a sample gets. The
    /// accumulators below are the opposite case — what they hold is a position on
    /// a clock, which is exactly where a float runs out (ADR-0032).
    /// </summary>
    private readonly float[][] lines;
    private readonly int[] positions;
    private readonly float[] lengths;

    private readonly double[] phases;
    private readonly double[] previousInputs;
    private readonly bool[] running;

    /// <summary>
    /// One cell per <see cref="OpCode.UnitRead"/>/<see cref="OpCode.UnitWrite"/>
    /// pair: what a cycle in the patch carries from one evaluation to the next.
    /// <see cref="double"/> like the accumulators rather than <see cref="float"/>
    /// like the lines, because what sits here is a register on its way back round
    /// a loop rather than a sample on its way to a speaker — it may be a phase, a
    /// modulation index or a coordinate, and none of those is sixteen bits.
    /// </summary>
    private readonly double[] units;

    /// <summary>
    /// One ring per Scope in the patch: the last stretch of what the speakers
    /// played, kept so the eye can be shown it.
    /// </summary>
    /// <remarks>
    /// The only state here that nothing in the program ever reads back. A delay
    /// line, an accumulator and a cell are all written so the next evaluation
    /// can read them; a trace is written for something outside the program
    /// entirely — see <see cref="OpCode.Tap"/>. It lives here anyway because
    /// this is the object the audio path already carries, and because it is
    /// per-run rather than per-program in exactly the way everything else here
    /// is.
    /// <para>
    /// <see cref="float"/> like the delay lines and for the same reason: what
    /// goes in is a signal on its way to a speaker, and a chart of one needs
    /// fewer bits than that rather than more.
    /// </para>
    /// </remarks>
    private readonly float[][] traces;
    private readonly int[] traceHeads;

    /// <param name="lengthsInSeconds">Longest delay each line must hold, in program order.</param>
    /// <param name="sampleRate">
    /// Evaluations per second — the *oversampled* rate on the audio path, since
    /// that is how often the program actually runs.
    /// </param>
    /// <param name="phaseCount">
    /// How many accumulators the program needs. They keep no buffer and so need
    /// no rate: an accumulator measures how far its input moved, and the step it
    /// takes is whatever that was.
    /// </param>
    /// <param name="unitCount">
    /// How many one-evaluation cells the program needs — one per cycle in the
    /// patch. A cell, like an accumulator, is a single number and needs no rate.
    /// </param>
    public DelayState(
        IReadOnlyList<float> lengthsInSeconds,
        int sampleRate,
        int phaseCount = 0,
        int unitCount = 0,
        int traceCount = 0)
    {
        SampleRate = sampleRate;
        lengths = [.. lengthsInSeconds];
        lines = new float[lengthsInSeconds.Count][];
        positions = new int[lengthsInSeconds.Count];

        phases = new double[phaseCount];
        previousInputs = new double[phaseCount];
        running = new bool[phaseCount];

        units = new double[unitCount];

        traces = new float[traceCount][];
        traceHeads = new int[traceCount];

        for (var i = 0; i < traceCount; i++) traces[i] = new float[TraceSamples];

        for (var i = 0; i < lines.Length; i++)
        {
            // Two samples of slack so a read at the full length still lands
            // behind the write head rather than on top of it.
            var samples = (int)MathF.Ceiling(MathF.Max(lengthsInSeconds[i], 0f) * sampleRate) + 2;
            lines[i] = new float[Math.Max(samples, 4)];
        }
    }

    public int SampleRate { get; }

    public int Count => lines.Length;

    public int PhaseCount => phases.Length;

    public int UnitCount => units.Length;

    public int TraceCount => traces.Length;

    /// <summary>
    /// How much of the past a Scope keeps, in evaluations — two seconds at the
    /// oversampled audio rate, which is longer than any window a chart offers.
    /// </summary>
    public const int TraceSamples = 48_000 * 4 * 2;

    /// <summary>Puts one evaluation into trace <paramref name="slot"/>.</summary>
    /// <remarks>
    /// Called from the sound callback, so it allocates nothing and takes no
    /// lock. What reads it is <see cref="CopyTrace"/>, on the thread that draws
    /// — which may therefore read across a write. A chart with a seam in it for
    /// one frame is a better answer than a lock on the audio thread, and it is
    /// the same trade <c>AudioRing</c> already makes for a recording.
    /// </remarks>
    public void Tap(int slot, double value)
    {
        if ((uint)slot >= (uint)traces.Length) return;

        var ring = traces[slot];

        ring[traceHeads[slot]] = double.IsFinite(value) ? (float)value : 0f;
        traceHeads[slot] = (traceHeads[slot] + 1) % ring.Length;
    }

    /// <summary>
    /// Lays the newest <paramref name="span"/> evaluations of a trace out across
    /// <paramref name="into"/>, oldest first, so what the caller holds is a
    /// stretch of the past in the order it happened and at whatever width it
    /// means to draw.
    /// </summary>
    /// <remarks>
    /// Each cell is the furthest from nought of the evaluations that fall in it,
    /// keeping its sign. A window is always more evaluations than there are
    /// columns — a fiftieth of a second is four thousand of them, two seconds is
    /// four hundred thousand — so something has to be chosen, and which is
    /// chosen decides what a long timebase looks like.
    /// <para>
    /// Taking one per cell aliases: a tone whose period happens to divide the
    /// step charts as a slow wobble or a straight line, which is the one failure
    /// a scope must not have. Averaging is worse still at the far end — a
    /// waveform is symmetric about nought, so a bucket holding whole cycles of
    /// it averages to nothing and two seconds of a loud tone draws as silence.
    /// The peak has neither problem: a bucket of many cycles gives its
    /// amplitude, adjacent buckets take opposite sides of the wave, and with the
    /// fill under the trace that is the solid band a real scope shows at a sweep
    /// far slower than the signal. A bucket of one evaluation is that evaluation,
    /// so nothing is done to a chart that did not need decimating.
    /// </para>
    /// <para>
    /// May be read across a write, and is meant to be: see <see cref="Tap"/>.
    /// </para>
    /// </remarks>
    public void CopyTrace(int slot, Span<float> into, int span)
    {
        if ((uint)slot >= (uint)traces.Length || into.Length == 0)
        {
            into.Clear();
            return;
        }

        var ring = traces[slot];

        span = Math.Clamp(span, 1, ring.Length);

        // Where the newest evaluation is not: the head is the next cell to be
        // written, so the span ends just before it.
        var start = traceHeads[slot] - span;

        for (var i = 0; i < into.Length; i++)
        {
            var from = start + (int)((long)i * span / into.Length);
            var to = start + (int)((long)(i + 1) * span / into.Length);

            // A window shorter than the chart is wide: cells share evaluations
            // rather than some of them holding none.
            if (to <= from) to = from + 1;

            var peak = 0f;
            for (var j = from; j < to; j++)
            {
                var value = ring[Index(j, ring.Length)];
                if (MathF.Abs(value) > MathF.Abs(peak)) peak = value;
            }

            into[i] = peak;
        }
    }

    /// <summary>
    /// Whether this state still fits a program. Op order decides which buffer is
    /// which, so a recompile that changes the delays at all gets fresh buffers —
    /// and the tail that was ringing is lost, which is the honest outcome: the
    /// old tail belonged to a patch that no longer exists.
    /// </summary>
    public bool Fits(
        IReadOnlyList<float> lengthsInSeconds,
        int sampleRate,
        int phaseCount = 0,
        int unitCount = 0,
        int traceCount = 0)
    {
        if (sampleRate != SampleRate || lengthsInSeconds.Count != lengths.Length) return false;
        if (phaseCount != phases.Length || unitCount != units.Length) return false;
        if (traceCount != traces.Length) return false;

        for (var i = 0; i < lengths.Length; i++)
            if (lengths[i] != lengthsInSeconds[i])
                return false;

        return true;
    }

    public void Clear()
    {
        for (var i = 0; i < lines.Length; i++)
        {
            Array.Clear(lines[i]);
            positions[i] = 0;
        }

        Array.Clear(phases);
        Array.Clear(previousInputs);
        Array.Clear(running);
        Array.Clear(units);

        // The traces too: a rewind puts the patch back at nought, and a chart
        // still showing what was played before it would be a picture of a
        // moment that no longer exists on the timeline.
        for (var i = 0; i < traces.Length; i++)
        {
            Array.Clear(traces[i]);
            traceHeads[i] = 0;
        }
    }

    /// <summary>
    /// What cell <paramref name="slot"/> was left holding, which is zero until
    /// something has written one — so a loop starts from silence rather than from
    /// whatever a previous patch left behind.
    /// </summary>
    public double ReadUnit(int slot) => units[slot];

    /// <summary>Puts a value in cell <paramref name="slot"/> for the next evaluation to read.</summary>
    /// <remarks>
    /// Bounded exactly as <see cref="Write"/> is, and for a sharper version of the
    /// same reason. A delay line's feedback is clamped below one before it ever
    /// reaches the buffer; a cycle drawn as wires has no such coefficient anywhere
    /// in it, so a loop with a gain above one is not merely possible but easy, and
    /// this is the only place it can be caught. Clamping rather than refusing
    /// keeps the runaway audible as a value pinned at the rails — which is what a
    /// real rack does too — instead of turning the patch into silent NaN.
    /// </remarks>
    public void WriteUnit(int slot, double value) =>
        units[slot] = double.IsFinite(value) ? Math.Clamp(value, -16d, 16d) : 0d;

    /// <summary>
    /// The same, for a cell holding the renderer's clock rather than a signal —
    /// bounded to what a number can be and to nothing else.
    /// </summary>
    /// <remarks>
    /// The clamp above is for a value a wire can reach, where a loop with a gain
    /// above one is easy to draw and pinning it at the rails is the only way to
    /// keep it audible rather than NaN. A clock is neither: nothing in a patch
    /// can write one and nothing can make it run away. What it does do is pass
    /// sixteen, after sixteen seconds — and clamped, it stops there for good,
    /// leaving every module that measures its own rate off it to see an interval
    /// that grows for the rest of the session. See <see cref="OpCode.ClockWrite"/>.
    /// </remarks>
    public void WriteClock(int slot, double value) =>
        units[slot] = double.IsFinite(value) ? value : 0d;

    /// <summary>
    /// Advances accumulator <paramref name="cell"/> by however far
    /// <paramref name="input"/> has moved since the last evaluation, counted in
    /// cycles of <paramref name="frequency"/>, and returns the phase that
    /// results. Wrapped into [0, 1), which every waveform is periodic over and
    /// which is also what keeps the running total from losing its low bits.
    /// </summary>
    /// <remarks>
    /// The first evaluation takes no step at all: there is no previous input to
    /// measure against, and starting from a guess would be a click of exactly
    /// the kind this exists to remove. So a cell begins at phase zero and the
    /// patch is heard from the start of a cycle.
    /// </remarks>
    public double Advance(int cell, double input, double frequency)
    {
        // A non-finite input carries no distance, so the phase holds where it is
        // rather than being poisoned by it — ADR-0013's rule, applied to
        // something that persists.
        if (!double.IsFinite(input)) input = previousInputs[cell];
        if (!double.IsFinite(frequency)) frequency = 0d;

        var step = running[cell] ? (input - previousInputs[cell]) * frequency : 0d;

        previousInputs[cell] = input;
        running[cell] = true;

        if (!double.IsFinite(step)) step = 0d;

        var next = phases[cell] + step;
        next -= Math.Floor(next);

        return phases[cell] = double.IsFinite(next) ? next : 0d;
    }

    /// <summary>
    /// The value on line <paramref name="slot"/> from <paramref name="seconds"/>
    /// ago, interpolated between the two samples either side so that sweeping the
    /// delay time glides instead of stepping.
    /// </summary>
    public double Read(int slot, double seconds, float maximum)
    {
        var line = lines[slot];
        var limit = line.Length - 2;

        if (!double.IsFinite(seconds)) seconds = 0d;

        var samples = Math.Clamp(seconds, 0d, maximum) * SampleRate;
        samples = Math.Min(samples, limit);

        var whole = (int)samples;
        var fraction = samples - whole;

        var newest = positions[slot];
        var first = Index(newest - whole, line.Length);
        var second = Index(first - 1, line.Length);

        return line[first] + (line[second] - line[first]) * fraction;
    }

    /// <summary>Writes at the head of line <paramref name="slot"/> and advances it.</summary>
    public void Write(int slot, double value)
    {
        var line = lines[slot];
        var next = Index(positions[slot] + 1, line.Length);

        line[next] = double.IsFinite(value) ? (float)Math.Clamp(value, -16d, 16d) : 0f;
        positions[slot] = next;
    }

    /// <summary>
    /// Wraps an index that may have gone either side of the buffer. Feedback is
    /// clamped below one, but a delay line is still the one place in the program
    /// where a value can accumulate, so the write is bounded as well — ADR-0013's
    /// rule, applied to something that persists.
    /// </summary>
    private static int Index(int index, int length)
    {
        index %= length;
        return index < 0 ? index + length : index;
    }
}
