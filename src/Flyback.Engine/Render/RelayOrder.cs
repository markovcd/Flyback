using Flyback.Engine.Compile;

namespace Flyback.Engine.Render;

/// <summary>
/// A renderer's <see cref="SoundRelay"/>, built off the sound's thread: the callback that
/// first meets a program cut into legs asks for one and plays that buffer on one core,
/// and the relay is waiting from a later buffer on.
/// </summary>
internal sealed class RelayOrder(int oversample) : IThreadPoolWorkItem
{
    private IlProgram? wanted;
    private SoundRelay? ready;
    private int queued;

    /// <summary>The relay for <paramref name="il"/>, or null while one is being built. Allocation-free.</summary>
    public SoundRelay? For(IlProgram il)
    {
        var relay = Volatile.Read(ref ready);
        if (relay is not null && relay.Fits(il, oversample)) return relay;

        Volatile.Write(ref wanted, il);
        if (Interlocked.Exchange(ref queued, 1) == 0) ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
        return null;
    }

    /// <summary>Builds the relay for <paramref name="il"/> now, on the caller's thread, unless the one waiting fits.</summary>
    public void Build(IlProgram il)
    {
        var relay = Volatile.Read(ref ready);
        if (relay is null || !relay.Fits(il, oversample)) Volatile.Write(ref ready, new SoundRelay(il, oversample));
    }

    public void Execute()
    {
        Volatile.Write(ref queued, 0);
        if (Volatile.Read(ref wanted) is { } il) Build(il);
    }
}
