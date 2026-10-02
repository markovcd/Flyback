using Avalonia.Controls;
using Avalonia.Media;
using Flyback.Core;
using Flyback.Editor.Capture;
using Flyback.Editor.Files;
using Flyback.Editor.Settings;
using Flyback.Ui.Midi;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Windows;

/// <summary>
/// The editor's window on the desktop: it holds the <see cref="EditorView"/> its container
/// composes, keeps its size and place, and asks the unsaved question on close (ADR-0148).
/// </summary>
internal sealed class MainWindow : Window
{
    private readonly EditorView view;
    private readonly SettingsSession settingsSession;
    private readonly ReportLine report;
    private readonly ShellLayout shell;
    private readonly UnsavedWork unsaved;
    private readonly WorkKeeper keeper;
    private readonly WindowLayoutKeeper layoutKeeper;
    private readonly RecordingState recordingState;

    public MainWindow(
        EditorView view,
        UnsavedWork unsaved,
        ReportLine report,
        MidiHub midi,
        TakeRecording recording,
        RecordingState recordingState,
        WorkKeeper keeper,
        EditorOpened editorOpened,
        WindowLayoutKeeper layoutKeeper,
        ShellLayout shell,
        SettingsSession settingsSession)
    {
        this.view = view;
        this.unsaved = unsaved;
        this.report = report;
        this.settingsSession = settingsSession;
        this.keeper = keeper;
        this.layoutKeeper = layoutKeeper;
        this.shell = shell;
        this.recordingState = recordingState;

        Recording = recording;

        // Everything let go when this stops being the window you are typing
        // into. A key released over another program is a key this never hears
        // about, and the note would hang until something else happened to move
        // it — alt-tabbing away mid-chord should not leave a drone behind.
        Deactivated += (_, _) => midi.AllOff();

        // The other half of Attention.Request: a blink some window managers
        // would otherwise leave lit after the window it was about is the one
        // in front.
        Activated += (_, _) => Attention.Clear(this);

        Title = GlobalConstants.ApplicationName;
        Width = 1280;
        Height = 800;
        // A tablet held upright, whose screen is narrower than two columns need; the layout goes to one.
        MinWidth = 360;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = new SolidColorBrush(Colors.Window);
        layoutKeeper.Track(this);

        view.Hold(this);
        Content = view;

        // Dialogs and the storage provider are ready only once the window is open.
        Opened += async (_, _) => await editorOpened.RunAsync();
    }

    /// <summary>The take this window is recording, counting in, or about to.</summary>
    internal TakeRecording Recording { get; }

    /// <summary>
    /// Brings the editor up: the saved layout, the first patch and everything that
    /// compiling it starts. Once, before the window is shown; the constructor only wires.
    /// </summary>
    public void Start() => view.Start();

    /// <inheritdoc cref="EditorView.ClearPresetSelection"/>
    internal void ClearPresetSelection() => view.ClearPresetSelection();

    // The editing session as opposed to the patch: undo and redo from wherever the
    // focus is, what the title bar says about unsaved work, and the window's own close.
    // The question every route out of a patch asks is UnsavedWork's; what is here is
    // the close that asks it.

    /// <summary>Set while a close is waiting for a take to be finished, so a second close does not wait twice.</summary>
    private bool waitingOnTake;

    /// <summary>Whether the window could close without asking anything.</summary>
    internal bool HoldsNoWork => !unsaved.SomethingToLose;

    /// <summary>
    /// Closes without asking about unsaved work, for a test tearing its window
    /// down: there is nobody to answer the question, and a canceled close would
    /// leave the window and its engine running for the rest of the assembly.
    /// </summary>
    internal void CloseWithoutAsking()
    {
        unsaved.Leave();
        Close();
    }

    /// <summary>
    /// Nothing may block inside a closing handler, so a window with unsaved work
    /// in it cancels the close, asks, and closes itself again on the way back.
    /// </summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (e.Cancel) return;

        if (recordingState.Running)
        {
            e.Cancel = true;
            Report("Stop the recording before closing the window.");
            return;
        }

        // Every attempt, not only the one that goes through: the window's monitor
        // is no longer asked for once it has closed, and a refused close leaves it
        // as it is.
        RememberLayout();

        if (unsaved.Leaving) return;

        // Already asking. The close is refused and nothing else happens: putting
        // the question up a second time is the one response that would make the
        // window look broken, and there is nothing else to do with a close that
        // arrived while the same close is still being answered. The settings
        // window is refused the same way, for the answer it is still waiting on.
        if (unsaved.Asking || settingsSession.IsShowing)
        {
            e.Cancel = true;
            return;
        }

        // A take first, since it is the one thing here that cannot be had again:
        // its file is closed, and then the close is tried once more.
        if (Recording.InHand)
        {
            e.Cancel = true;

            if (waitingOnTake) return;

            waitingOnTake = true;
            await Recording.FinishAsync();
            waitingOnTake = false;

            Close();
            return;
        }

        // A count is not a take — nothing is being written yet — but it would
        // become one under the question below, which can stay up for as long as
        // it likes: the patch rewound and a file opened behind a dialog asking
        // whether to save. Closing calls it off whatever the answer (ADR-0090).
        Recording.CallOffCount();

        if (!unsaved.SomethingToLose) return;

        e.Cancel = true;

        if (!await unsaved.MayReplaceThePatchAsync()) return;

        unsaved.Leave();
        Close();
    }

    /// <summary>
    /// The one place anything is said to the user. <paramref name="detail"/> is for
    /// what will not fit on a status bar — a list of missing plugins, say.
    /// </summary>
    /// <param name="detail"></param>
    /// <param name="progress">
    /// That this is the last message again with a new number in it, so the log keeps
    /// one entry for the run rather than one per update.
    /// </param>
    /// <param name="message"></param>
    internal void Report(string message, string? detail = null, bool progress = false) =>
        report.Say(message, detail, progress);

    protected override void OnClosed(EventArgs e)
    {
        // Before the device goes, and before anything else: a take whose header
        // was never patched is not a file, so a window closed mid-recording
        // waits here for it rather than abandoning it. OnClosing has normally
        // dealt with it already, and this is for the close that could not be
        // put off.
        Recording.FinishNow();

        // Whatever there was to lose has been asked about by now, and answered.
        keeper.Stop();

        base.OnClosed(e);
    }
    
    private void RememberLayout()
    {
        if (!shell.IsBuilt) return;

        layoutKeeper.Remember(() => shell.Capture(this));
    }
}
