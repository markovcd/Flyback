using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Flyback.App.Bars;
using Flyback.App.Canvas;
using Flyback.App.Capture;
using Flyback.App.Controls;
using Flyback.App.Files;
using Flyback.App.Gallery;
using Flyback.App.Inspect;
using Flyback.App.Knobs;
using Flyback.App.Midi;
using Flyback.App.Notices;
using Flyback.App.Settings;
using Flyback.Core;
using Flyback.Plugins.Hosting;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App.Windows;

/// <summary>
/// The editor's window: it is handed the hubs and the regions around them by its
/// container (ADR-0150), lays them out, and keeps its own keys and full screen, and
/// the close that asks the unsaved question (ADR-0148).
/// </summary>
internal sealed class MainWindow : Window
{
    private readonly OutputSections outputSections;
    private readonly OutputSettingsUse outputSettingsUse;
    private readonly SettingsSession settingsSession;
    private readonly IDialog dialog;
    private readonly Document document;
    private readonly PatchOpening patchOpening;
    private readonly PresetSlot presets;
    private readonly PanelKnobs knobs;
    private readonly ReportLine report;
    private readonly ShellLayout shell;
    private readonly PluginCatalog plugins;
    private readonly Playback playback;
    private readonly EditorStart editorStart;
    private readonly MidiHub midi;
    private readonly UnsavedWork unsaved;
    private readonly WorkKeeper keeper;
    private readonly WindowLayoutKeeper layoutKeeper;
    private readonly FullScreenPreview fullScreen;
    private readonly TransportControls transport;
    private readonly EditState editState;
    private readonly RecordingState recordingState;
    private readonly Reactions reactions;

    private bool started;
    
    public MainWindow(
        Document document,
        PatchOpening patchOpening,
        UnsavedWork unsaved,
        ReportLine report,
        PluginCatalog plugins,
        MidiHub midi,
        Playback playback,
        OutputSections outputSections,
        PanelKnobs knobs,
        PresetSlot presets,
        TakeRecording recording,
        RecordingState recordingState,
        WorkKeeper keeper,
        Reactions reactions,
        EditorOpened editorOpened,
        EditorStart editorStart,
        WindowLayoutKeeper layoutKeeper,
        FullScreenPreview fullScreen,
        TransportControls transport,
        ShellLayout shell,
        EditState editState,
        OutputSettingsUse outputSettingsUse,
        SettingsSession settingsSession,
        IDialog dialog)
    {
        this.document = document;
        this.patchOpening = patchOpening;
        this.unsaved = unsaved;
        this.report = report;
        this.plugins = plugins;
        this.midi = midi;
        this.playback = playback;
        this.editorStart = editorStart;
        this.outputSections = outputSections;
        this.outputSettingsUse = outputSettingsUse;
        this.settingsSession = settingsSession;
        this.knobs = knobs;
        this.presets = presets;
        this.keeper = keeper;
        this.layoutKeeper = layoutKeeper;
        this.fullScreen = fullScreen;
        this.transport = transport;
        this.shell = shell;
        this.editState = editState;
        this.dialog = dialog;
        this.recordingState = recordingState;
        this.reactions = reactions;

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
        MinWidth = 860;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = new SolidColorBrush(Colors.Window);
        layoutKeeper.Track(this);

        // Before the layout, because these are live from the moment the window
        // is: the preview needs its resolution and its backend whether or not
        // anybody has selected the Output to look at them.
        InitializeOutputControls();

        // Live from construction rather than from Loaded: a drop arriving
        // before the window has finished laying out is still a drop.
        WireFileDrop();

        Content = BuildLayout();

        // Dialogs and the storage provider are ready only once the window is open.
        Opened += async (_, _) => await editorOpened.RunAsync();
    }
    
    /// <summary>The take this window is recording, counting in, or about to.</summary>
    internal TakeRecording Recording { get; }

    /// <summary>
    /// Brings the editor up: the saved layout, the first patch and everything that
    /// compiling it starts. Once, before the window is shown; the constructor only wires.
    /// </summary>
    public void Start()
    {
        if (started) return;
        started = true;

        editState.Refresh();
        editorStart.Start(this);
    }
    
    private Control BuildLayout()
    {
        // The popups behind the report and a module's name hang off the window
        // rather than off the control, so what they look like is said here.
        Styles.Add(ReportLine.Trim());
        Styles.Add(ModulePlate.Naming());
        Styles.Add(ModulePalette.Trim());
        return shell.Build();
    }

    /// <summary>
    /// Takes the selection off the preset list, for a document that arrived by some
    /// other route — a patch, a bundle, or a source file.
    /// </summary>
    /// <remarks>
    /// A preset left highlighted would claim the canvas still held it. Setting the
    /// index to -1 is enough: the picker's own handler returns on a negative index
    /// before it asks what "wanted" means.
    /// </remarks>
    internal void ClearPresetSelection() => presets.Clear();
    
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
    /// Undo and redo, from wherever the focus happens to be. Handled on the window
    /// rather than on the canvas because an edit is as likely to have been made in
    /// the inspector, and anything that already dealt with the key keeps it — a
    /// text box undoing its own typing is doing the same job at its own scale.
    /// </summary>
    /// <remarks>
    /// Command as well as Control, so the shortcut is the one the machine uses.
    /// Both are accepted everywhere rather than asked which platform this is, since
    /// neither is a gesture anything else here claims.
    /// </remarks>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // A dialog lets the keys typed into its own boxes through unhandled, so
        // whatever it is over must not act on them.
        if (e.Handled || dialog.IsShowing) return;

        // Before the modifier check, because Escape carries none. Only while the
        // picture is full screen: everywhere else Escape belongs to the module
        // filter, which handles its own before this is ever reached.
        if (e.Key == Key.Escape && fullScreen.IsAway)
        {
            fullScreen.Leave();
            e.Handled = true;
            return;
        }

        if (e is { Key: Key.F3, KeyModifiers: KeyModifiers.None } && fullScreen.IsAway)
        {
            transport.ToggleStats(fullScreen.IsFullScreen);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && knobs.StopModes())
        {
            Report("Done.");
            e.Handled = true;
            return;
        }

        // Before the instrument, because F2 is not a note and never will be:
        // the keyboard-as-instrument maps letters, and a function key is free
        // for the shell in a way no letter is any more.
        if (e.Key == Key.F2)
        {
            reactions.Raise(new CodeAsked(!document.ShowingCode));
            e.Handled = true;
            return;
        }

        // The computer's keyboard as an instrument. A note is a bare keystroke
        // and nothing else, so a key carrying a command modifier is left for
        // whatever claimed it: Ctrl+Z is undo, and it stays undo in a patch
        // being played with a hand on the Z. Only while something is actually
        // listening, so a patch with no MIDI In in it types the way it always
        // did.
        if (Bare(e.KeyModifiers) && Playing && PlayKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0) return;

        var again = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        switch (e.Key)
        {
            // All three go to whichever view is showing. Reached only where the
            // view did not want the keystroke itself: the code editor handles
            // its own undo, and this is the end of the bubble.
            case Key.Z:
                if (again) reactions.Raise(new RedoAsked());
                else reactions.Raise(new UndoAsked());
                e.Handled = true;
                break;

            // The other half of the convention Windows carries: Ctrl+Y is redo
            // where Ctrl+Shift+Z is, and somebody who reaches for one is not
            // going to enjoy discovering which this program wanted.
            case Key.Y:
                reactions.Raise(new RedoAsked());
                e.Handled = true;
                break;

            // Lay out. Beside the two above because it is the same kind of
            // thing: an edit that Ctrl+Z takes off again — the modules across
            // the canvas, or the lines down the page. With Shift, only the
            // selected modules move (ADR-0110).
            case Key.L:
                reactions.Raise(new TidyAsked(OnlySelected: again));
                e.Handled = true;
                break;

            // Not an edit — nothing here is on either undo stack — but routed
            // through the same dispatch as the rest of the toolbar's
            // shortcuts, and guarded the same way a click on a disabled
            // button already is: see TakeRecording.ToggleAsync.
            case Key.R:
                reactions.Raise(new RecordAsked());
                e.Handled = true;
                break;

            // The panel has no room while the picture has the window.
            case Key.K:
                if (!fullScreen.IsFullScreen) reactions.Raise(new KnobsAsked(!knobs.View.IsVisible));

                e.Handled = true;
                break;

            // With Ctrl because the bare letter is a note, and Space adds a module.
            case Key.P:
                reactions.Raise(new PauseAsked());
                e.Handled = true;
                break;

            // The document itself, on the letters every program uses for it.
            // Both were the toolbar's alone, and the hand that has just
            // finished an edit is on the keyboard rather than the pointer.
            // Saving is one gesture here — the picker is where a name is
            // chosen — so there is no second key for saving under another one.
            case Key.O:
                reactions.Raise(new OpenAsked());
                e.Handled = true;
                break;

            case Key.S:
                reactions.Raise(new SaveAsked());
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Lets a note go, whatever else is going on.
    /// </summary>
    /// <remarks>
    /// None of the guards that stand in front of pressing a key stand here, and
    /// that asymmetry is the point: a key going down can start something, and one
    /// coming up can only ever stop one. Every guard is a way for a release to be
    /// missed, and a missed release is a note that sounds for the rest of the
    /// session. Releasing one that was never played does nothing, which is what
    /// makes ignoring the guards safe.
    /// </remarks>
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        midi.KeyUp(e.Key);
    }

    /// <summary>
    /// Whether a keystroke is a plain one, with nothing held that turns a letter
    /// into a command.
    /// </summary>
    /// <remarks>
    /// Shift is deliberately not one of them: it is part of typing a letter, and no
    /// gesture in the shell is Shift and a letter, so a capital Z still plays.
    /// </remarks>
    private static bool Bare(KeyModifiers modifiers) =>
        (modifiers & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Alt)) == 0;

    /// <summary>Whether the computer's keyboard is an instrument right now, and nothing is being typed.</summary>
    private bool Playing => !Typing && playback.Keyed;

    /// <summary>
    /// Whether the keystroke belongs to something being typed into rather than to
    /// the instrument.
    /// </summary>
    /// <remarks>
    /// The whole reason the notes can be on bare letters: a text box does not mark
    /// an ordinary key press handled, so without this, naming a patch would play a
    /// tune. AvalonEdit is not a <see cref="TextBox"/> and the focus check never
    /// sees it, so it is asked about separately — and only where the text is the
    /// document, since a printing (ADR-0068) is a reading rather than a place
    /// anybody is composing.
    /// </remarks>
    private bool Typing =>
        FocusManager.GetFocusedElement() is TextBox
        || document is { ShowingCode: true, Owned: true };

    /// <summary>
    /// One key, as either a note or the pair that moves the two rows. Null-ish by
    /// design: anything that is neither is left alone and goes on meaning
    /// whatever it meant.
    /// </summary>
    private bool PlayKey(Key key)
    {
        if (midi.Shift(key) is { } moved)
        {
            Report(moved);
            return true;
        }

        return midi.KeyDown(key);
    }
    
    /// <summary>
    /// Lets a patch, a bundle or a text file be opened by dropping it in from
    /// the file explorer — the same three kinds the picker offers, arriving
    /// without one.
    /// </summary>
    private void WireFileDrop()
    {
        DragDrop.SetAllowDrop(this, true);

        // Refused under a dialog, and shown as refused, for the reason
        // OpenActivatedFileAsync gives.
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File) && !dialog.IsShowing
                ? DragDropEffects.Copy
                : DragDropEffects.None);

        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            // Only the first: one window holds one patch, and a picker never
            // offers more than that either.
            if (e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().FirstOrDefault() is not { } file) return;

            e.Handled = true;

            await patchOpening.OpenActivatedFileAsync(file);
        });
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
    
    /// <summary>
    /// Called once, from the constructor, rather than when the settings window
    /// opens: what was last saved has to be in force before anybody has looked.
    /// </summary>
    private void InitializeOutputControls()
    {
        knobs.BuildMidiSection(plugins, outputSections.Takeover, outputSections.KeyboardLayout);

        // Quietly, because nobody asked for anything yet: a saved answer is
        // what the program starts in, not a change to report.
        outputSections.Show();
        outputSettingsUse.ApplyCurrent();
    }
    
    private void RememberLayout()
    {
        if (!shell.IsBuilt) return;

        layoutKeeper.Remember(() => shell.Capture(this));
    }
}
