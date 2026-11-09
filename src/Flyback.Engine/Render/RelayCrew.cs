using System.Diagnostics.CodeAnalysis;

namespace Flyback.Engine.Render;

/// <summary>
/// The threads that take legs of a sound alongside the thread rendering it (ADR-0230),
/// shared by every renderer in the process.
/// </summary>
/// <remarks>
/// A renderer posts its relay on a board and wakes the crew; a helper takes whatever
/// leg is ready, and the rendering thread takes legs too, so a helper that is slow to
/// wake costs speed and never correctness. Nothing here allocates once a helper is running.
/// </remarks>
internal static class RelayCrew
{
    private const int Boards = 8;

    /// <summary>How long a helper keeps looking for work before it sleeps, in spins.</summary>
    private const int Patience = 2_000;

    private static readonly SoundRelay?[] Posted = new SoundRelay?[Boards];
    private static readonly SemaphoreSlim Bell = new(0, int.MaxValue);
    private static readonly Lock Hiring = new();
    private static int helpers;
    private static int sleeping;

    /// <summary>Starts helpers until there are <paramref name="count"/>. Off the sound's thread: starting one allocates.</summary>
    public static void Hire(int count)
    {
        if (Volatile.Read(ref helpers) >= count) return;

        lock (Hiring)
        {
            while (helpers < count)
            {
                var thread = new Thread(Help, 256 * 1024) { IsBackground = true, Name = "Flyback sound leg" };

                try
                {
                    thread.Priority = ThreadPriority.Highest;
                }
                catch (PlatformNotSupportedException)
                {
                    // Where a priority cannot be set, the leg runs at the default one.
                }

                thread.Start();
                helpers++;
            }
        }
    }

    /// <summary>Puts <paramref name="relay"/> where helpers look, or says there is no room and it runs alone.</summary>
    public static bool Post(SoundRelay relay, int wanted)
    {
        for (var i = 0; i < Boards; i++)
        {
            if (Interlocked.CompareExchange(ref Posted[i], relay, null) is not null) continue;

            // Never more rings than sleepers, so a helper that wakes to nothing goes back to sleep.
            var ring = Math.Min(Volatile.Read(ref sleeping), wanted) - Bell.CurrentCount;
            if (ring > 0) Bell.Release(ring);
            return true;
        }

        return false;
    }

    public static void Withdraw(SoundRelay relay)
    {
        for (var i = 0; i < Boards; i++) Interlocked.CompareExchange(ref Posted[i], null, relay);
    }

    /// <summary>A helper thread's body: it takes legs for as long as the process lives.</summary>
    [DoesNotReturn]
    private static void Help()
    {
        var idle = 0;

        while (true)
        {
            var worked = false;

            for (var i = 0; i < Boards; i++)
                if (Volatile.Read(ref Posted[i]) is { } relay && relay.Help())
                    worked = true;

            if (worked)
            {
                idle = 0;
                continue;
            }

            if (++idle < Patience)
            {
                Thread.SpinWait(20);
                continue;
            }

            Interlocked.Increment(ref sleeping);

            // Looked at once more after saying so, so a post made in between still wakes this one.
            var posted = false;
            for (var i = 0; i < Boards; i++) posted |= Volatile.Read(ref Posted[i]) is not null;

            if (!posted) Bell.Wait();

            Interlocked.Decrement(ref sleeping);
            idle = 0;
        }
    }
}
