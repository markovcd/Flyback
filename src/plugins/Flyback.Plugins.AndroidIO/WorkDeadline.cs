using System.Diagnostics;
using Android.App;
using Android.Content;
using Android.OS;

namespace Flyback.Plugins.AndroidIO;

/// <summary>
/// Tells Android how long the audio thread has to work out a block and how long it took
/// (a performance hint session), so the system clocks and places the thread to meet it.
/// </summary>
/// <remarks>
/// Without one the governor sees a thread that sleeps most of each block in its write,
/// runs it on a middle core at a fraction of that core's clock, and a heavy patch's
/// blocks come in late although the core has room to spare. The aim is half a block,
/// so a block that takes longer than usual still lands in time. Nothing happens below
/// Android 12 or where the device offers no hints.
/// </remarks>
internal sealed class WorkDeadline : IDisposable
{
    private readonly PerformanceHintManager.Session? session;
    private long started;

    private WorkDeadline(PerformanceHintManager.Session? session) => this.session = session;

    /// <summary>A deadline for the calling thread, which works out a block of <paramref name="lasts"/>.</summary>
    public static WorkDeadline ForThisThread(TimeSpan lasts)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(31)) return new WorkDeadline(null);

        var hints = Application.Context.GetSystemService(Context.PerformanceHintService) as PerformanceHintManager;
        var aim = (long)(lasts.TotalNanoseconds / 2);

        return new WorkDeadline(hints?.CreateHintSession([global::Android.OS.Process.MyTid()], aim));
    }

    /// <summary>Marks the start of a block's work.</summary>
    public void Begin() => started = Stopwatch.GetTimestamp();

    /// <summary>Reports how long the block's work took since <see cref="Begin"/>.</summary>
    public void End()
    {
        if (session is null || !OperatingSystem.IsAndroidVersionAtLeast(31)) return;

        var took = Stopwatch.GetElapsedTime(started).Ticks * 100;
        if (took > 0) session.ReportActualWorkDuration(took);
    }

    public void Dispose()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(31)) session?.Close();
    }
}
