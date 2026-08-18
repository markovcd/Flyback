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
        int unitCount = 0)
    {
        SampleRate = sampleRate;
        lengths = [.. lengthsInSeconds];
        lines = new float[lengthsInSeconds.Count][];
        positions = new int[lengthsInSeconds.Count];

        phases = new double[phaseCount];
        previousInputs = new double[phaseCount];
        running = new bool[phaseCount];

        units = new double[unitCount];

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
        int unitCount = 0)
    {
        if (sampleRate != SampleRate || lengthsInSeconds.Count != lengths.Length) return false;
        if (phaseCount != phases.Length || unitCount != units.Length) return false;

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
