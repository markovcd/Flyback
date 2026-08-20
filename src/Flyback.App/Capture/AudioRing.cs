namespace Flyback.App.Capture;

/// <summary>
/// The one place a recording touches the sound callback: a fixed ring the
/// callback writes into and the encoder thread drains.
/// </summary>
/// <remarks>
/// Single producer, single consumer, and no lock on either side. The audio
/// thread may not block and may not allocate, which rules out a queue and rules
/// out growing — so the buffer is sized once, when recording arms, and the two
/// cursors only ever move forward. Each side writes exactly one of them, which
/// is what makes plain volatile reads enough.
/// <para>
/// An overrun drops whole buffers rather than partial ones. A partial write
/// would shift every later sample by a channel and turn a glitch into a swapped
/// stereo image for the rest of the take. It is counted rather than hidden,
/// because a recording that quietly lost a second of sound is worse than one
/// that says it did.
/// </para>
/// </remarks>
internal sealed class AudioRing
{
    private readonly float[] buffer;

    /// <summary>Written only by the producer, read by both.</summary>
    private long written;

    /// <summary>Written only by the consumer, read by both.</summary>
    private long read;

    private long dropped;

    /// <param name="capacity">
    /// In samples, counting each channel separately. Generous on purpose: the
    /// consumer only has to keep up with a file write, so an overrun here means
    /// something is badly wrong rather than merely busy.
    /// </param>
    public AudioRing(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));

        buffer = new float[capacity];
    }

    /// <summary>Samples the producer could not fit, and so threw away.</summary>
    public long Dropped => Volatile.Read(ref dropped);

    /// <summary>Samples handed over, whether or not they have been drained yet.</summary>
    public long Accepted => Volatile.Read(ref written);

    /// <summary>
    /// Appends a callback's worth. Called on the audio thread, so it allocates
    /// nothing and never waits — a full ring loses the buffer instead.
    /// </summary>
    public void Write(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0) return;

        var end = written;
        var free = buffer.Length - (int)(end - Volatile.Read(ref read));

        if (samples.Length > free)
        {
            Volatile.Write(ref dropped, dropped + samples.Length);
            return;
        }

        var at = (int)(end % buffer.Length);
        var straight = Math.Min(samples.Length, buffer.Length - at);

        samples[..straight].CopyTo(buffer.AsSpan(at));
        samples[straight..].CopyTo(buffer);

        // Published last, so the consumer never sees a cursor pointing at samples
        // that have not landed.
        Volatile.Write(ref written, end + samples.Length);
    }

    /// <summary>Drains up to <paramref name="destination"/>'s length. Returns how much was taken.</summary>
    public int Read(Span<float> destination)
    {
        var start = read;
        var taken = (int)Math.Min(Volatile.Read(ref written) - start, destination.Length);

        if (taken <= 0) return 0;

        var at = (int)(start % buffer.Length);
        var straight = Math.Min(taken, buffer.Length - at);

        buffer.AsSpan(at, straight).CopyTo(destination);
        buffer.AsSpan(0, taken - straight).CopyTo(destination[straight..]);

        // Likewise last: the producer must not reclaim space before it is copied.
        Volatile.Write(ref read, start + taken);

        return taken;
    }
}
