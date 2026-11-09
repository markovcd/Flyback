namespace Flyback.Ui.Audio;

/// <summary>
/// How loud the sound played lately peaks, falling back between peaks, and how many buffers clipped: held a sample past full scale.
/// Written by the sound callback, read by whoever shows it.
/// </summary>
internal sealed class PeakMeter
{
    /// <summary>How fast the held peak falls between louder buffers, in dB a second.</summary>
    public const double FallPerSecond = 24;

    private float peak;
    private long clipped;

    /// <summary>The loudest sample lately, 1 being full scale.</summary>
    public float Peak => Volatile.Read(ref peak);

    /// <summary>How many buffers have held a sample past full scale.</summary>
    public long Clipped => Interlocked.Read(ref clipped);

    /// <summary>Takes in a buffer that plays for <paramref name="seconds"/>.</summary>
    public void Take(ReadOnlySpan<float> buffer, double seconds)
    {
        var loudest = 0f;

        foreach (var sample in buffer)
        {
            var size = MathF.Abs(sample);
            if (size > loudest) loudest = size;
        }

        if (loudest > 1f) Interlocked.Increment(ref clipped);

        var fallen = Volatile.Read(ref peak) * (float)Math.Pow(10, -FallPerSecond * seconds / 20);
        Volatile.Write(ref peak, Math.Max(loudest, fallen));
    }
}
