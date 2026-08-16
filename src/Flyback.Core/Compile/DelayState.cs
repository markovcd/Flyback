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
    private readonly float[][] lines;
    private readonly int[] positions;
    private readonly float[] lengths;

    private readonly float[] phases;
    private readonly float[] previousInputs;
    private readonly bool[] running;

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
    public DelayState(IReadOnlyList<float> lengthsInSeconds, int sampleRate, int phaseCount = 0)
    {
        SampleRate = sampleRate;
        lengths = [.. lengthsInSeconds];
        lines = new float[lengthsInSeconds.Count][];
        positions = new int[lengthsInSeconds.Count];

        phases = new float[phaseCount];
        previousInputs = new float[phaseCount];
        running = new bool[phaseCount];

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

    /// <summary>
    /// Whether this state still fits a program. Op order decides which buffer is
    /// which, so a recompile that changes the delays at all gets fresh buffers —
    /// and the tail that was ringing is lost, which is the honest outcome: the
    /// old tail belonged to a patch that no longer exists.
    /// </summary>
    public bool Fits(IReadOnlyList<float> lengthsInSeconds, int sampleRate, int phaseCount = 0)
    {
        if (sampleRate != SampleRate || lengthsInSeconds.Count != lengths.Length) return false;
        if (phaseCount != phases.Length) return false;

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
    }

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
    public float Advance(int cell, float input, float frequency)
    {
        // A non-finite input carries no distance, so the phase holds where it is
        // rather than being poisoned by it — ADR-0013's rule, applied to
        // something that persists.
        if (!float.IsFinite(input)) input = previousInputs[cell];
        if (!float.IsFinite(frequency)) frequency = 0f;

        var step = running[cell] ? (input - previousInputs[cell]) * frequency : 0f;

        previousInputs[cell] = input;
        running[cell] = true;

        if (!float.IsFinite(step)) step = 0f;

        var next = phases[cell] + step;
        next -= MathF.Floor(next);

        return phases[cell] = float.IsFinite(next) ? next : 0f;
    }

    /// <summary>
    /// The value on line <paramref name="slot"/> from <paramref name="seconds"/>
    /// ago, interpolated between the two samples either side so that sweeping the
    /// delay time glides instead of stepping.
    /// </summary>
    public float Read(int slot, float seconds, float maximum)
    {
        var line = lines[slot];
        var limit = line.Length - 2;

        if (!float.IsFinite(seconds)) seconds = 0f;

        var samples = Math.Clamp(seconds, 0f, maximum) * SampleRate;
        samples = MathF.Min(samples, limit);

        var whole = (int)samples;
        var fraction = samples - whole;

        var newest = positions[slot];
        var first = Index(newest - whole, line.Length);
        var second = Index(first - 1, line.Length);

        return line[first] + (line[second] - line[first]) * fraction;
    }

    /// <summary>Writes at the head of line <paramref name="slot"/> and advances it.</summary>
    public void Write(int slot, float value)
    {
        var line = lines[slot];
        var next = Index(positions[slot] + 1, line.Length);

        line[next] = float.IsFinite(value) ? Math.Clamp(value, -16f, 16f) : 0f;
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
