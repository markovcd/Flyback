namespace Flyback.Engine.Render;

/// <summary>
/// What a capture device heard, handed from its thread to the sound's, without a lock.
/// </summary>
/// <remarks>
/// A ring with one writer and one reader. The reader never lets the lag grow past
/// <see cref="MaxLag"/> frames: it skips to <see cref="Target"/> instead, so a stalled
/// sound or a drifting clock costs a click rather than an echo that grows. An empty ring
/// reads as silence. A reader that renders ahead of the device, as a page's sound does,
/// asks for a <see cref="Cushion"/> so that jitter in when frames arrive is not heard.
/// </remarks>
public sealed class LineInFeed : ILineInSource
{
    private const int Capacity = 1 << 15;
    private const int Mask = Capacity - 1;

    /// <summary>The most frames the sound may trail what was heard by, about 85 ms at 48 kHz.</summary>
    public const int MaxLag = 4096;

    /// <summary>How far behind it is put when it has fallen further behind than <see cref="MaxLag"/>.</summary>
    public const int Target = 1024;

    private readonly float[] lefts = new float[Capacity];
    private readonly float[] rights = new float[Capacity];
    private long written;
    private long read;
    private bool primed;

    /// <summary>
    /// Frames that must be waiting before reading starts, or starts again after the ring has
    /// run dry, with silence until then. Nought reads at once.
    /// </summary>
    public int Cushion { get; init; }

    /// <summary>Frames waiting to be read.</summary>
    public int Pending => (int)Math.Min(Volatile.Read(ref written) - read, Capacity);

    /// <summary>
    /// Takes what the capture device just heard. Called from its thread only.
    /// </summary>
    /// <param name="interleaved">Frames of <paramref name="channels"/> samples each.</param>
    /// <param name="channels">One for a microphone, which is heard on both sides, or two.</param>
    public void Write(ReadOnlySpan<float> interleaved, int channels)
    {
        if (channels < 1) return;

        var at = Volatile.Read(ref written);
        var frames = interleaved.Length / channels;

        for (var i = 0; i < frames; i++, at++)
        {
            var l = Finite(interleaved[i * channels]);
            var r = channels > 1 ? Finite(interleaved[i * channels + 1]) : l;

            lefts[at & Mask] = l;
            rights[at & Mask] = r;
        }

        Volatile.Write(ref written, at);
    }

    /// <inheritdoc />
    public void Next(out float left, out float right)
    {
        var end = Volatile.Read(ref written);

        if (end - read > MaxLag) read = end - Target;

        if (!primed && end - read >= Cushion) primed = true;

        if (!primed || read >= end)
        {
            primed = false;
            left = right = 0f;
            return;
        }

        left = lefts[read & Mask];
        right = rights[read & Mask];
        read++;
    }

    /// <summary>Forgets what was heard, so a restart does not play it.</summary>
    public void Clear()
    {
        read = Volatile.Read(ref written);
        primed = false;
    }

    private static float Finite(float value) => float.IsFinite(value) ? Math.Clamp(value, -1f, 1f) : 0f;
}
