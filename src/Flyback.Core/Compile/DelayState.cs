namespace Flyback.Core.Compile;

/// <summary>
/// Everything a program remembers between evaluations: the ring buffers behind
/// <see cref="OpCode.Delay"/> and <see cref="OpCode.Allpass"/>, and the running
/// phases behind <see cref="OpCode.Phase"/>.
/// </summary>
/// <remarks>
/// It belongs to the renderer rather than to the <see cref="CompiledPatch"/>: a
/// recompile swaps the program under the audio thread, and two programs may
/// briefly both exist, so state on the program would be duplicated or lost.
/// Which buffer or cell an op uses is its position among the ops of its kind,
/// counted as the program runs — every op executes once per evaluation and
/// always in the same order.
/// </remarks>
public sealed class DelayState
{
    /// <summary>
    /// The lines stay <see cref="float"/> while the registers are
    /// <see cref="double"/>: they hold a signal on its way to a speaker. The
    /// accumulators below hold a position on a clock, which is where a float runs
    /// out (ADR-0032).
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
    /// <see cref="double"/> rather than <see cref="float"/>, because what sits
    /// here may be a phase, a modulation index or a coordinate.
    /// </summary>
    private readonly double[] units;

    /// <summary>
    /// One ring per Scope in the patch: the last stretch of what the speakers
    /// played, kept so the eye can be shown it.
    /// </summary>
    /// <remarks>
    /// The only state here nothing in the program reads back — a trace is written
    /// for something outside it, see <see cref="OpCode.Tap"/> — and it lives here
    /// because this is what the audio path already carries.
    /// <see cref="float"/> like the delay lines: a chart of a signal needs fewer
    /// bits than the signal.
    /// </remarks>
    private readonly float[][] traces;
    private readonly int[] traceHeads;

    /// <param name="lengthsInSeconds">Longest delay each line must hold, in program order.</param>
    /// <param name="sampleRate">
    /// Evaluations per second — the oversampled rate on the audio path, since
    /// that is how often the program runs.
    /// </param>
    /// <param name="phaseCount">
    /// How many accumulators the program needs. They keep no buffer and need no
    /// rate: an accumulator measures how far its input moved.
    /// </param>
    /// <param name="unitCount">
    /// How many one-evaluation cells the program needs — one per cycle in the
    /// patch, and a single number like an accumulator.
    /// </param>
    /// <param name="traceCount"></param>
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

    /// <summary>
    /// Memory for one program, sized and labelled from the program itself.
    /// </summary>
    /// <param name="program">What is about to be run, which knows both how many cells it needs and whose they are.</param>
    /// <param name="sampleRate">Evaluations per second — the oversampled rate on the audio path.</param>
    public DelayState(CompiledPatch program, int sampleRate)
        : this(
            program.DelayLengths,
            sampleRate,
            program.PhaseCount,
            program.UnitCount,
            program.TraceCount)
    {
        Owners = program.Owners;

        // Kept beside the other three rather than in StateOwners, because a
        // trace is the one cell here the compiler numbers explicitly: the Taps
        // are already in slot order and already carry the Scope they belong to.
        traceOwners = [.. program.Taps.Select(tap => tap.Node)];
    }

    public int SampleRate { get; }

    /// <summary>
    /// Whose cells these are, as the program that asked for them said — see
    /// <see cref="Adopt"/>.
    /// </summary>
    public StateOwners Owners { get; } = StateOwners.None;

    private readonly IReadOnlyList<Guid> traceOwners = [];

    /// <summary>
    /// Takes over whatever of <paramref name="previous"/> belongs to a module
    /// this program still has.
    /// </summary>
    /// <remarks>
    /// For an edit made while the sound is playing: matching by owner rather than
    /// by position, the modules that were not touched carry on and only what
    /// changed begins again. A line whose length changed keeps its tail — the
    /// samples are copied newest first into a ring of either size — so a delay
    /// time turned while it is ringing goes on ringing. A change of rate cannot
    /// be carried over, since it resizes every line against a clock that no
    /// longer means the same thing.
    /// <para>
    /// Read on the thread that recompiles while the callback may still be filling
    /// <paramref name="previous"/>: nothing here resizes anything and every cell
    /// is a single aligned write, so the worst of that race is a few samples of a
    /// tail arriving out of order.
    /// </para>
    /// </remarks>
    public void Adopt(DelayState previous)
    {
        var phaseMap = StateOwners.Adopt(Owners.Phases, previous.Owners.Phases);

        for (var i = 0; i < phaseMap.Length && i < phases.Length; i++)
        {
            var from = phaseMap[i];

            if (from < 0 || from >= previous.phases.Length) continue;

            phases[i] = previous.phases[from];
            previousInputs[i] = previous.previousInputs[from];

            // Carried too, and it matters: a cell that has not run yet takes no
            // step at all, so a tone that kept its phase but lost this would
            // stall for one evaluation on every edit.
            running[i] = previous.running[from];
        }

        var unitMap = StateOwners.Adopt(Owners.Units, previous.Owners.Units);

        for (var i = 0; i < unitMap.Length && i < units.Length; i++)
        {
            var from = unitMap[i];

            if (from >= 0 && from < previous.units.Length) units[i] = previous.units[from];
        }

        if (previous.SampleRate != SampleRate) return;

        var delayMap = StateOwners.Adopt(Owners.Delays, previous.Owners.Delays);

        for (var i = 0; i < delayMap.Length && i < lines.Length; i++)
        {
            var from = delayMap[i];

            if (from < 0 || from >= previous.lines.Length) continue;

            Rewind(lines[i], previous.lines[from], previous.positions[from], newest: 0);
            positions[i] = 0;
        }

        var traceMap = StateOwners.Adopt(traceOwners, previous.traceOwners);

        for (var i = 0; i < traceMap.Length && i < traces.Length; i++)
        {
            var from = traceMap[i];

            if (from < 0 || from >= previous.traces.Length) continue;

            // A head is the next cell to be written rather than the newest one,
            // so the stretch to copy ends one before it.
            Rewind(traces[i], previous.traces[from], previous.traceHeads[from] - 1, newest: -1);
            traceHeads[i] = 0;
        }
    }

    /// <summary>
    /// Copies a ring into another of any size, newest sample first, so what is
    /// kept is the most recent past rather than the start of the buffer.
    /// </summary>
    /// <param name="newest">Where in <paramref name="into"/> the newest sample lands.</param>
    private static void Rewind(float[] into, float[] from, int head, int newest)
    {
        var count = Math.Min(into.Length, from.Length);

        for (var k = 0; k < count; k++)
            into[Index(newest - k, into.Length)] = from[Index(head - k, from.Length)];
    }

    public int Count => lines.Length;

    public int PhaseCount => phases.Length;

    public int UnitCount => units.Length;

    public int TraceCount => traces.Length;

    /// <summary>
    /// The longest stretch of the past a chart may ask for, in seconds — the
    /// window knob's own ceiling (<see cref="Graph.PortDisplay.Duration"/> tops
    /// out at 10^1.5, about 31.62 s) rounded up.
    /// </summary>
    /// <remarks>
    /// The one place that number lives: it bounds both what a Scope's window may
    /// compile to — see <c>PatchCompiler.WindowOf</c> — and how much ring there is
    /// to answer it with. Raising one alone does nothing, which is why they are
    /// one constant.
    /// </remarks>
    public const float MaxWindowSeconds = 32f;

    /// <summary>
    /// How much of the past a Scope or Meter keeps, in evaluations — see
    /// <see cref="MaxWindowSeconds"/>, at the oversampled audio rate.
    /// </summary>
    public const int TraceSamples = (int)(GlobalConstants.SampleRate * 4 * MaxWindowSeconds);

    /// <summary>Puts one evaluation into trace <paramref name="slot"/>.</summary>
    /// <remarks>
    /// Called from the sound callback, so it allocates nothing and takes no lock.
    /// <see cref="CopyTrace"/> reads it on the drawing thread and may therefore
    /// read across a write: a chart with a seam in it for one frame is a better
    /// answer than a lock on the audio thread.
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
    /// <paramref name="into"/>, oldest first, at whatever width the caller means
    /// to draw.
    /// </summary>
    /// <remarks>
    /// Each cell is the furthest from nought of the evaluations that fall in it,
    /// keeping its sign — a window is always more evaluations than there are
    /// columns, so something has to be chosen. Taking one per cell aliases: a tone
    /// whose period divides the step charts as a wobble or a straight line.
    /// Averaging is worse at the far end, since a waveform is symmetric about
    /// nought and a bucket of whole cycles averages to silence. The peak has
    /// neither problem, and a bucket of one evaluation is that evaluation.
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
    /// How loud trace <paramref name="slot"/> has been over its newest
    /// <paramref name="span"/> evaluations: the furthest any of them got from
    /// nought, and the root mean square of all of them.
    /// </summary>
    /// <remarks>
    /// Both at once, off one pass, because they answer different questions: the
    /// peak is what hits, and the root mean square is the power in the window,
    /// which is what reads as loudness. Neither is smoothed — the window is the
    /// smoothing, and it is a knob. Squares are summed in a <see cref="double"/>,
    /// since a float accumulator stops noticing new samples about a hundred
    /// thousand in, which would read as a meter that goes deaf.
    /// <para>
    /// May be read across a write, and is meant to be: see <see cref="Tap"/>.
    /// </para>
    /// </remarks>
    public (float Peak, float Level) Measure(int slot, int span)
    {
        if ((uint)slot >= (uint)traces.Length) return (0f, 0f);

        var ring = traces[slot];

        span = Math.Clamp(span, 1, ring.Length);

        // The head is the next cell to be written, so the newest span ends just
        // before it — the same arithmetic CopyTrace walks, without the buckets.
        var start = traceHeads[slot] - span;

        var peak = 0f;
        var power = 0d;

        for (var i = 0; i < span; i++)
        {
            var value = ring[Index(start + i, ring.Length)];

            peak = MathF.Max(peak, MathF.Abs(value));
            power += (double)value * value;
        }

        return (peak, (float)Math.Sqrt(power / span));
    }

    /// <summary>
    /// Whether this state still fits a program. Op order decides which buffer is
    /// which, so a recompile that changes the delays gets fresh buffers, and the
    /// tail that was ringing belonged to a patch that no longer exists.
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
    /// Bounded as <see cref="Write"/> is, and more sharply needed: a cycle drawn
    /// as wires has no feedback coefficient anywhere in it, so a loop with a gain
    /// above one is easy to draw and this is the only place to catch it. Clamping
    /// rather than refusing keeps the runaway audible at the rails instead of
    /// turning the patch into silent NaN.
    /// </remarks>
    public void WriteUnit(int slot, double value) =>
        units[slot] = double.IsFinite(value) ? Math.Clamp(value, -16d, 16d) : 0d;

    /// <summary>
    /// The same, for a cell holding the renderer's clock rather than a signal —
    /// bounded to what a number can be and to nothing else.
    /// </summary>
    /// <remarks>
    /// Nothing in a patch can write a clock or make it run away, but it does pass
    /// sixteen after sixteen seconds — and clamped it would stop there for good,
    /// leaving every module that measures its own rate off it seeing an interval
    /// that grows for the rest of the session. See
    /// <see cref="OpCode.ClockWrite"/>.
    /// </remarks>
    public void WriteClock(int slot, double value) =>
        units[slot] = double.IsFinite(value) ? value : 0d;

    /// <summary>
    /// Advances accumulator <paramref name="cell"/> by however far
    /// <paramref name="input"/> has moved since the last evaluation, counted in
    /// cycles of <paramref name="frequency"/>, and returns the phase. Wrapped into
    /// [0, 1), which every waveform is periodic over and which keeps the running
    /// total from losing its low bits.
    /// </summary>
    /// <remarks>
    /// The first evaluation takes no step: there is no previous input to measure
    /// against, and starting from a guess would be a click of exactly the kind
    /// this exists to remove.
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
    /// clamped below one, but a delay line is still the one place a value can
    /// accumulate, so the write is bounded as well — ADR-0013's rule, applied to
    /// something that persists.
    /// </summary>
    private static int Index(int index, int length)
    {
        index %= length;
        return index < 0 ? index + length : index;
    }
}
