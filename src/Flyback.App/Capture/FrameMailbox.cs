namespace Flyback.App.Capture;

/// <summary>
/// One frame deep, newest wins. The render thread leaves a frame here and the
/// encoder takes whatever is there when it next looks.
/// </summary>
/// <remarks>
/// A queue would be the wrong shape. The preview draws as fast as it can and the
/// file wants thirty frames a second, so most of what is drawn is surplus by the
/// time anyone asks for it — and the frame that should go in the file is always
/// the most recent one, never the oldest waiting. Keeping one means the surplus
/// is discarded where it is cheapest, before it is encoded.
/// <para>
/// Three buffers and an atomic swap: one the producer is filling, one published,
/// one the consumer is reading. Nothing is copied to hand a frame over and
/// neither side ever waits for the other.
/// </para>
/// </remarks>
internal sealed class FrameMailbox
{
    private byte[] producing;
    private byte[] pending;
    private byte[] consuming;

    private long published;
    private long taken;

    public FrameMailbox(int bytes)
    {
        if (bytes < 1) throw new ArgumentOutOfRangeException(nameof(bytes));

        producing = new byte[bytes];
        pending = new byte[bytes];
        consuming = new byte[bytes];
    }

    /// <summary>Frames published, whether or not anyone collected them.</summary>
    public long Published => Volatile.Read(ref published);

    /// <summary>Where the producer writes. The same array until <see cref="Publish"/>.</summary>
    public Span<byte> Writing => producing;

    /// <summary>Offers what is in <see cref="Writing"/> and hands back a fresh buffer to fill.</summary>
    public void Publish()
    {
        producing = Interlocked.Exchange(ref pending, producing);

        // After the swap, so a frame is never announced before it is reachable.
        Interlocked.Increment(ref published);
    }

    /// <summary>
    /// The newest frame, or an empty span when nothing has been published since
    /// the last call. The span stays valid until the next call.
    /// </summary>
    public ReadOnlySpan<byte> TakeLatest()
    {
        // Read before the swap: a frame published in between is one this call
        // may collect but must not claim, or the next call would skip it.
        var now = Volatile.Read(ref published);

        if (now == taken) return default;

        consuming = Interlocked.Exchange(ref pending, consuming);
        taken = now;

        return consuming;
    }
}
