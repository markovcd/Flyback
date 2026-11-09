namespace Flyback.Ui.Audio;

/// <summary>
/// How loud the sound played lately peaks, falling back between peaks, and how many buffers clipped.
/// Written by the sound callback, read by whoever shows it.
/// </summary>
internal sealed class PeakMeter
{
    /// <summary>How fast the held peak falls between louder buffers, in dB a second.</summary>
    public const double FallPerSecond = 24;

    /// <summary>How many samples in a row one channel holds at full scale before it counts as clipped.</summary>
    public const int ClipRun = 2;

    private float peak;
    private long clipped;

    /// <summary>The loudest sample lately, 1 being full scale.</summary>
    public float Peak => Volatile.Read(ref peak);

    /// <summary>How many buffers have clipped.</summary>
    public long Clipped => Interlocked.Read(ref clipped);

    /// <summary>
    /// Takes in an interleaved stereo buffer that plays for <paramref name="seconds"/>, before it
    /// is turned down by <paramref name="volume"/>.
    /// </summary>
    /// <remarks>
    /// The renderer clamps to full scale, so a clip is a run of samples held there: a clean
    /// full-scale wave touches it for one sample at most. It is counted before the volume,
    /// since the patch was cut however quietly it is then played.
    /// </remarks>
    public void Take(ReadOnlySpan<float> buffer, double seconds, float volume = 1f)
    {
        var loudest = 0f;
        var cut = false;
        Span<int> held = stackalloc int[2];

        for (var i = 0; i < buffer.Length; i++)
        {
            var size = MathF.Abs(buffer[i]);
            if (size > loudest) loudest = size;

            ref var run = ref held[i & 1];
            run = size >= 1f ? run + 1 : 0;
            if (run >= ClipRun) cut = true;
        }

        if (cut) Interlocked.Increment(ref clipped);

        var fallen = Volatile.Read(ref peak) * (float)Math.Pow(10, -FallPerSecond * seconds / 20);
        Volatile.Write(ref peak, Math.Max(loudest * volume, fallen));
    }
}
