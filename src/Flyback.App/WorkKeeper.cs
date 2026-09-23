using System.Diagnostics;
using Avalonia.Threading;

namespace Flyback.App;

/// <summary>
/// Unsaved work kept on disk while it is unsaved, so a crash costs a few seconds of
/// it rather than all of it, and put back at the next start — ADR-0103.
/// </summary>
/// <remarks>
/// Asked on a timer rather than on every edit: a knob held and turned is an edit a
/// frame, and what is worth keeping is where it was let go. What a snapshot holds is
/// the window's to say, because the document a crash would lose is the one the
/// unsaved question would have offered to save.
/// </remarks>
internal sealed class WorkKeeper
{
    /// <summary>How often unsaved work is looked at, and so the most a crash can cost.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    /// <summary>Where the snapshots are, for finding what a crash left behind.</summary>
    private readonly string folder;

    /// <summary>The document as a crash would lose it, or null while there is nothing to lose.</summary>
    private readonly Func<RecoveredWork?> work;

    /// <summary>This window's snapshot, or null where the folder would not open.</summary>
    private Recovery? recovery;

    /// <summary>
    /// The last thing handed to <see cref="recovery"/>, so a tick with nothing new
    /// writes nothing. Null for nothing kept.
    /// </summary>
    private RecoveredWork? kept;

    private readonly DispatcherTimer? ticker;

    internal WorkKeeper(string folder, Func<RecoveredWork?> work)
    {
        this.folder = folder;
        this.work = work;

        recovery = Recovery.Open(folder);

        if (recovery is null) return;

        ticker = new DispatcherTimer(DispatcherPriority.Background) { Interval = Interval };
        ticker.Tick += (_, _) => Keep(later: true);
        ticker.Start();

        // An exception nothing caught ends the program once this returns, and the
        // UI thread is the one place the document can still be read from: the last
        // few seconds are written now, rather than lost to the next tick that
        // never comes.
        Dispatcher.UIThread.UnhandledException += KeepOnTheWayDown;
    }

    /// <summary>
    /// Writes the document where a crash would leave it, or clears what was written
    /// once there is nothing left to lose.
    /// </summary>
    /// <param name="later">Off this thread, which a tick can afford and a crash cannot.</param>
    internal void Keep(bool later)
    {
        if (recovery is null) return;

        RecoveredWork? taken;

        try
        {
            taken = work();
        }
        catch (Exception ex)
        {
            // Traced rather than reported, since the next tick would only say it again.
            Trace.WriteLine($"recovery: could not take a copy: {ex.Message}");
            return;
        }

        if (Same(taken, kept)) return;

        kept = taken;

        var write = recovery.Later(taken);

        if (later) _ = Task.Run(write);
        else write();
    }

    /// <summary>
    /// Offers the most recent work a crash left behind to <paramref name="put"/>, and
    /// forgets it once that has taken it. One a start: a second waits for the next,
    /// since a window holds one document.
    /// </summary>
    /// <param name="put">
    /// Puts the work back, and says whether it came back. A patch naming a module no
    /// plugin now offers is refused, and kept for a start that has the plugin again.
    /// </param>
    internal void Restore(Func<RecoveredWork, bool> put)
    {
        if (Recovery.Orphans(folder) is not [var path, ..]) return;

        if (Recovery.Read(path) is { } left && !put(left)) return;

        Recovery.Forget(path);
    }

    /// <summary>Stops keeping unsaved work, and deletes what was kept: the window is closing on purpose.</summary>
    internal void Stop()
    {
        if (recovery is null) return;

        ticker?.Stop();
        Dispatcher.UIThread.UnhandledException -= KeepOnTheWayDown;

        recovery.Dispose();
        recovery = null;
    }

    private void KeepOnTheWayDown(object? sender, DispatcherUnhandledExceptionEventArgs e) =>
        Keep(later: false);

    /// <summary>
    /// Whether two snapshots hold the same work. The files are compared by which bundle
    /// they came out of, which does not change while it is open.
    /// </summary>
    private static bool Same(RecoveredWork? a, RecoveredWork? b) =>
        a is null || b is null ? a == b : a with { Files = null } == b with { Files = null } && a.Files == b.Files;
}
