namespace Flyback.Core.Compile;

/// <summary>
/// The ring buffers behind <see cref="OpCode.Delay"/> and
/// <see cref="OpCode.Allpass"/> — the only memory a program has.
/// </summary>
/// <remarks>
/// It belongs to the renderer rather than to the <see cref="CompiledPatch"/>,
/// for the same reason ADR-0018 keeps a program immutable: a recompile swaps the
/// program under the audio thread, and two programs may briefly both exist. State
/// that lived on the program would be duplicated or lost at that moment; state
/// that lives on the renderer simply carries on.
/// <para>
/// Which buffer an op uses is its position among the delay ops, counted as the
/// program runs. Every op executes exactly once per evaluation and always in the
/// same order, so the count is exact without storing an index on the op.
/// </para>
/// </remarks>
public sealed class DelayState
{
    private readonly float[][] lines;
    private readonly int[] positions;
    private readonly float[] lengths;

    /// <param name="lengthsInSeconds">Longest delay each line must hold, in program order.</param>
    /// <param name="sampleRate">
    /// Evaluations per second — the *oversampled* rate on the audio path, since
    /// that is how often the program actually runs.
    /// </param>
    public DelayState(IReadOnlyList<float> lengthsInSeconds, int sampleRate)
    {
        SampleRate = sampleRate;
        lengths = [.. lengthsInSeconds];
        lines = new float[lengthsInSeconds.Count][];
        positions = new int[lengthsInSeconds.Count];

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

    /// <summary>
    /// Whether this state still fits a program. Op order decides which buffer is
    /// which, so a recompile that changes the delays at all gets fresh buffers —
    /// and the tail that was ringing is lost, which is the honest outcome: the
    /// old tail belonged to a patch that no longer exists.
    /// </summary>
    public bool Fits(IReadOnlyList<float> lengthsInSeconds, int sampleRate)
    {
        if (sampleRate != SampleRate || lengthsInSeconds.Count != lengths.Length) return false;

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
