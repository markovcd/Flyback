using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.App;

/// <summary>
/// Unsaved work kept on disk while it is unsaved, so a crash costs a few seconds of
/// it rather than all of it, and offered back at the next start — ADR-0103.
/// </summary>
/// <remarks>
/// Asked on a timer rather than on every edit: a knob held and turned is an edit a
/// frame, and what is worth keeping is where it was let go. What is written is the
/// document as the unsaved question sees it — the patch, the text where the text is
/// the document, what a bundle carried and the conversation — so that what comes back
/// is what that question would have offered to save.
/// </remarks>
public sealed partial class MainWindow
{
    /// <summary>How often unsaved work is looked at, and so the most a crash can cost.</summary>
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromSeconds(2);

    /// <summary>This window's snapshot, or null where nothing is kept — which is every test.</summary>
    private Recovery? recovery;

    /// <summary>Where the snapshots are, for finding what a crash left behind.</summary>
    private string? recoveryFolder;

    /// <summary>
    /// The last thing handed to <see cref="recovery"/>, so a tick with nothing new
    /// writes nothing. Null for nothing kept.
    /// </summary>
    private RecoveredWork? kept;

    private DispatcherTimer? recoveryTicker;

    /// <summary>What to do about work a crash left behind.</summary>
    private enum Leftover
    {
        /// <summary>Asked about again at the next start, which is what dismissing the question means.</summary>
        Later,

        Restore,

        Discard,
    }

    /// <summary>Starts keeping unsaved work in <paramref name="folder"/>.</summary>
    private void KeepRecovery(string folder)
    {
        recoveryFolder = folder;
        recovery = Recovery.Open(folder);

        if (recovery is null) return;

        recoveryTicker = new DispatcherTimer(DispatcherPriority.Background) { Interval = RecoveryInterval };
        recoveryTicker.Tick += (_, _) => KeepWork(later: true);
        recoveryTicker.Start();

        // An exception nothing caught ends the program once this returns, and the
        // UI thread is the one place the document can still be read from: the last
        // few seconds are written now, rather than lost to the next tick that
        // never comes.
        Dispatcher.UIThread.UnhandledException += KeepWorkOnTheWayDown;
    }

    private void KeepWorkOnTheWayDown(object? sender, DispatcherUnhandledExceptionEventArgs e) =>
        KeepWork(later: false);

    /// <summary>
    /// Writes the document where a crash would leave it, or clears what was written
    /// once there is nothing left to lose.
    /// </summary>
    /// <param name="later">Off this thread, which a tick can afford and a crash cannot.</param>
    private void KeepWork(bool later)
    {
        if (recovery is null) return;

        RecoveredWork? work;

        try
        {
            work = SomethingToLose ? Work() : null;
        }
        catch (Exception ex)
        {
            // Traced rather than reported, since the next tick would only say it again.
            Trace.WriteLine($"recovery: could not take a copy: {ex.Message}");
            return;
        }

        if (Same(work, kept)) return;

        kept = work;

        var write = recovery.Later(work);

        if (later) _ = Task.Run(write);
        else write();
    }

    /// <summary>The document as a crash would lose it.</summary>
    private RecoveredWork Work() => new(
        patchName,
        soundFolder.Beside,
        PatchIO.ToJson(editor.Patch),
        sourceOwned ? source.Source : null,
        assistant?.ConversationToSave(),
        carried?.Bytes);

    /// <summary>
    /// Whether two snapshots hold the same work. The files are compared by which bundle
    /// they came out of, which does not change while it is open.
    /// </summary>
    private static bool Same(RecoveredWork? a, RecoveredWork? b) =>
        a is null || b is null ? a == b : a with { Files = null } == b with { Files = null } && a.Files == b.Files;

    /// <summary>
    /// Offers back the most recent work a crash left behind, where there is any. One a
    /// start: a second is offered at the next, since a window holds one document.
    /// </summary>
    private async Task OfferRecoveryAsync()
    {
        if (recoveryFolder is null) return;

        if (Recovery.Orphans(recoveryFolder) is not [var path, ..]) return;

        if (Recovery.Read(path) is not { } work)
        {
            Recovery.Forget(path);
            return;
        }

        var answer = await AskAboutLeftoverAsync(work);

        if (answer == Leftover.Later) return;

        if (answer == Leftover.Restore && !Recover(work)) return;

        Recovery.Forget(path);
    }

    private async Task<Leftover> AskAboutLeftoverAsync(RecoveredWork work)
    {
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        buttons.Children.Add(Answering("Restore", Leftover.Restore));
        buttons.Children.Add(Answering("Discard", Leftover.Discard));

        var named = work.Name is null ? "a patch" : work.Name;

        var asking = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            MaxWidth = 420,
            Children =
            {
                new TextBlock
                {
                    Text = $"{GlobalConstants.ApplicationName} closed without saving the changes to {named}. "
                        + "Restore them? They open unsaved, as they were.",
                    TextWrapping = TextWrapping.Wrap,
                },
                buttons,
            },
        };

        return await this.ShowDialog<Leftover>("Unsaved work", asking);

        static Button Answering(string text, Leftover with)
        {
            var button = new Button { Content = text, MinWidth = 96 };
            button.Click += (_, _) => Dialog.Close(button, with);

            return button;
        }
    }

    /// <summary>
    /// Puts work a crash left behind back on the canvas, as the document it was and
    /// unsaved — it is on no disk anybody chose.
    /// </summary>
    /// <returns>
    /// Whether it came back. A patch naming a module no plugin now offers is refused
    /// as a file would be, and kept for a start that has the plugin again.
    /// </returns>
    internal bool Recover(RecoveredWork work)
    {
        var loaded = PatchIO.Read(work.Patch);

        if (!loaded.IsComplete)
        {
            Report($"Not restored. {loaded.Summary}", loaded.Detail);
            return false;
        }

        Became(
            work.Name,
            work.Beside,
            work.Files is { } files ? new BundleFiles(files, soundFolder, pictureFolder) : null);

        ClearPresetSelection();

        editor.Patch = loaded.Patch;
        RewindToZero();

        if (work.Source is { } text)
        {
            TakeSource(text);

            // Written nowhere, so all of it is unsaved text.
            sourceOnDisk = string.Empty;
            editor.Remark(Owning());
        }
        else
        {
            DropSource();
        }

        assistant?.Open(work.Conversation);
        editor.MarkUnsaved();

        // Kept at once, since the orphan it came from is about to go.
        KeepWork(later: false);

        Report($"Restored {work.Name ?? "the patch"}. It has not been saved.");

        return true;
    }

    /// <summary>Stops keeping unsaved work, and deletes what was kept: the window is closing on purpose.</summary>
    private void StopRecovery()
    {
        if (recovery is null) return;

        recoveryTicker?.Stop();
        Dispatcher.UIThread.UnhandledException -= KeepWorkOnTheWayDown;

        recovery.Dispose();
        recovery = null;
    }
}
