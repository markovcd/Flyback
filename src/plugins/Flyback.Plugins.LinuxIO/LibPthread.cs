using System.Runtime.InteropServices;

namespace Flyback.Plugins.LinuxIO;

/// <summary>The one pthread call the JACK device needs.</summary>
internal static unsafe partial class LibPthread
{
    /// <summary>Exports the pthread functions on every glibc, before and after they moved into libc.</summary>
    private const string Library = "libpthread.so.0";

    [StructLayout(LayoutKind.Sequential)]
    private struct TimeSpec
    {
        public long Seconds;
        public long Nanoseconds;
    }

    [LibraryImport(Library, EntryPoint = "pthread_timedjoin_np")]
    private static partial int TimedJoin(nuint thread, IntPtr result, TimeSpec* deadline);

    /// <summary>Whether <paramref name="thread"/> ended within <paramref name="wait"/>, and was joined.</summary>
    public static bool Join(nuint thread, TimeSpan wait)
    {
        // The deadline is on CLOCK_REALTIME, which counts from the Unix epoch.
        var at = DateTimeOffset.UtcNow + wait;
        var deadline = new TimeSpec
        {
            Seconds = at.ToUnixTimeSeconds(),
            Nanoseconds = (at.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) % TimeSpan.TicksPerSecond * 100,
        };

        return TimedJoin(thread, IntPtr.Zero, &deadline) == 0;
    }
}
