using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Flyback.App.Assist;
using Flyback.App.Bars;
using Flyback.App.Canvas;
using Flyback.App.Capture;
using Flyback.App.Controls;
using Flyback.App.Files;
using Flyback.App.Gallery;
using Flyback.App.Inspect;
using Flyback.App.Knobs;
using Flyback.App.Midi;
using Flyback.App.PluginPackages;
using Flyback.App.Settings;
using Flyback.App.Statistics;
using Flyback.App.Updates;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Hosting;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App;

/// <summary>
/// The editor's window: it is handed the hubs and the regions around them by its
/// container (ADR-0150), lays them out, and keeps its own keys and full screen, and
/// the close that asks the unsaved question (ADR-0148).
/// </summary>
[SuppressMessage("Design", "CA1001", Justification = "Torn down in OnClosed; a window is closed, not disposed.")]
internal sealed class MainWindow : Window
{
    /// <summary>The Graphics, Recording and Sound sections, and what they were last saved as.</summary>
    private readonly OutputSections outputSections;

    private readonly OutputSettingsUse outputSettingsUse;

    private readonly SettingsSession settingsSession;

    private readonly IDialog dialog;

    /// <summary>Which of the canvas and the text owns the patch.</summary>
    private readonly Document document;

    /// <summary>Which file the patch is, and opening and saving it.</summary>
    private readonly PatchFiles files;

    private readonly PatchOpening patchOpening;

    private readonly PreviewHost preview;

    /// <summary>The preset slot on the toolbar, and the gallery it opens.</summary>
    private readonly PresetSlot presets;

    /// <summary>The bar along the top.</summary>
    private readonly Toolbar toolbar;

    /// <summary>The panel knobs, their learning and the knobs over the picture.</summary>
    private readonly PanelKnobs knobs;

    /// <summary>The panel on the right, under the preview.</summary>
    private readonly Inspector inspector;

    /// <summary>
    /// The one line anything is said on, and the log of what has been said. See
    /// <see cref="Report"/>, which is the only thing that writes to it.
    /// </summary>
    private readonly ReportLine report;

    private readonly ShellLayout shell;

    /// <summary>
    /// Read before this window existed, and already installed. Nothing here
    /// knows which backends or modules there are, or what they are called.
    /// </summary>
    private readonly PluginCatalog plugins;

    /// <summary>The patch compiled and played, paused or muted.</summary>
    private readonly Playback playback;

    private readonly EditorStart editorStart;

    /// <summary>
    /// Everything that plays the patch from outside it. The mirror of
    /// <see cref="audio"/>, which takes what the patch makes to a device. Assigned
    /// in the constructor because it is handed the MIDI backend the plugins
    /// offered, which is declared below it.
    /// </summary>
    private readonly MidiHub midi;

    /// <summary>What unsaved work there is, and the question closing it asks.</summary>
    private readonly UnsavedWork unsaved;

    public MainWindow(
        Document document,
        PatchFiles files,
        PatchOpening patchOpening,
        UnsavedWork unsaved,
        PreviewHost preview,
        ReportLine report,
        PluginCatalog plugins,
        MidiHub midi,
        Playback playback,
        OutputSections outputSections,
        PanelKnobs knobs,
        Inspector inspector,
        PresetSlot presets,
        Toolbar toolbar,
        TakeRecording recording,
        AssistantPanel assistant,
        WorkKeeper keeper,
        EditorWiring editorWiring,
        EditorOpened editorOpened,
        EditorStart editorStart,
        WindowLayoutKeeper layoutKeeper,
        FullScreenPreview fullScreen,
        TransportControls transport,
        ShellLayout shell,
        OutputSettingsUse outputSettingsUse,
        SettingsSession settingsSession,
        IDialog dialog)
    {
        this.document = document;
        this.files = files;
        this.patchOpening = patchOpening;
        this.unsaved = unsaved;
        this.preview = preview;
        this.report = report;
        this.plugins = plugins;
        this.midi = midi;
        this.playback = playback;
        this.editorStart = editorStart;
        this.outputSections = outputSections;
        this.outputSettingsUse = outputSettingsUse;
        this.settingsSession = settingsSession;
        this.knobs = knobs;
        this.inspector = inspector;
        this.presets = presets;
        this.toolbar = toolbar;
        this.keeper = keeper;
        this.layoutKeeper = layoutKeeper;
        this.fullScreen = fullScreen;
        this.transport = transport;
        this.shell = shell;
        this.dialog = dialog;

        Recording = recording;

        // Cross-service event edges are kept together so they remain visible
        // without becoming constructor dependencies between the services.
        editorWiring.Wire(RefreshEditState, ShowOwnership);

        // Everything let go when this stops being the window you are typing
        // into. A key released over another program is a key this never hears
        // about, and the note would hang until something else happened to move
        // it — alt-tabbing away mid-chord should not leave a drone behind.
        Deactivated += (_, _) => midi.AllOff();

        // The other half of Attention.Request: a blink some window managers
        // would otherwise leave lit after the window it was about is the one
        // in front.
        Activated += (_, _) => Attention.Clear(this);

        Title = BaseTitle;
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

    /// <summary>
    /// Brings the editor up: the saved layout, the first patch and everything that
    /// compiling it starts. Once, before the window is shown; the constructor only wires.
    /// </summary>
    public void Start()
    {
        if (started) return;
        started = true;

        editorStart.Start(this);
    }

    private bool started;

    private Control BuildLayout()
    {
        RefreshEditState();
        // The popups behind the report and a module's name hang off the window
        // rather than off the control, so what they look like is said here.
        Styles.Add(ReportLine.Trim());
        Styles.Add(ModulePlate.Naming());
        Styles.Add(ModulePalette.Trim());
        return shell.Build(RefreshEditState);
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

    #region Keys, undo and the unsaved question

    // The editing session as opposed to the patch: undo and redo from wherever the
    // focus is, what the title bar says about unsaved work, and the window's own close.
    // The question every route out of a patch asks is UnsavedWork's; what is here is
    // the close that asks it.

    /// <summary>The window title, before anything is said about the patch in it.</summary>
    private const string BaseTitle = GlobalConstants.ApplicationName;

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

        if (e.Key == Key.F3 && e.KeyModifiers == KeyModifiers.None && fullScreen.IsAway)
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
            document.ShowCode(!document.ShowingCode);
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
                if (again) document.Redo();
                else document.Undo();
                e.Handled = true;
                break;

            // The other half of the convention Windows carries: Ctrl+Y is redo
            // where Ctrl+Shift+Z is, and somebody who reaches for one is not
            // going to enjoy discovering which this program wanted.
            case Key.Y:
                document.Redo();
                e.Handled = true;
                break;

            // Lay out. Beside the two above because it is the same kind of
            // thing: an edit that Ctrl+Z takes off again — the modules across
            // the canvas, or the lines down the page. With Shift, only the
            // selected modules move (ADR-0110).
            case Key.L:
                document.Tidy(again);
                e.Handled = true;
                break;

            // Not an edit — nothing here is on either undo stack — but routed
            // through the same dispatch as the rest of the toolbar's
            // shortcuts, and guarded the same way a click on a disabled
            // button already is: see TakeRecording.ToggleAsync.
            case Key.R:
                _ = Recording.ToggleAsync();
                e.Handled = true;
                break;

            // The panel has no room while the picture has the window.
            case Key.K:
                if (!fullScreen.IsFullScreen) shell.ShowControls(!knobs.View.IsVisible);

                e.Handled = true;
                break;

            // With Ctrl because the bare letter is a note, and Space adds a module.
            case Key.P:
                transport.TogglePause();
                e.Handled = true;
                break;

            // The document itself, on the letters every program uses for it.
            // Both were the toolbar's alone, and the hand that has just
            // finished an edit is on the keyboard rather than the pointer.
            // Saving is one gesture here — the picker is where a name is
            // chosen — so there is no second key for saving under another one.
            case Key.O:
                _ = patchOpening.PickAndOpenAsync();
                e.Handled = true;
                break;

            case Key.S:
                _ = unsaved.SavePatchAsync();
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
        || (document.ShowingCode && document.Owned);

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
    /// Grays the two out when there is nothing behind or ahead — the same
    /// question a button would answer by doing nothing, asked where it can be
    /// seen instead — and says in the title what the patch is and whether there
    /// is unsaved work in it.
    /// </summary>
    private void RefreshEditState()
    {
        // Literally the answer the gesture gives, rather than a second statement of
        // the same rule — see UndoLandsOn.
        toolbar.Undo.IsEnabled = document.CanUndo;
        toolbar.Redo.IsEnabled = document.CanRedo;

        // The name first and the program second, which is the way round every
        // other window on the machine says it: what is on screen is the patch,
        // and which program is drawing it is the thing already known.
        var named = files.Name is null ? BaseTitle : $"{files.Name} — {BaseTitle}";

        // A dot rather than the word, because the title bar is read at a glance
        // and the question it answers is only whether there is anything to lose.
        Title = unsaved.SomethingToLose ? named + " •" : named;
    }

    #endregion

    #region Opening and saving

    // The routes into and out of PatchFiles: the Open and Save gestures,
    // a drop, an activation and a path named on the command line, each asking first
    // whatever has to be asked.

    /// <inheritdoc cref="PatchFiles.Became"/>
    internal void Became(string? name, string? beside, BundleFiles? files = null) => this.files.Became(name, beside, files);

    /// <summary>
    /// Whether the document is a bundle, which is what the next save offers first.
    /// Readable from the tests, as <see cref="Became"/> is callable from them:
    /// every route that opens a document is behind a file picker the headless
    /// platform does not put up.
    /// </summary>
    internal bool IsBundle => files.IsBundle;

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
    /// Opens a file handed to the program from outside a picker or a drop —
    /// which on macOS is how "open this file" arrives at all: Finder delivers
    /// it as an activation rather than as a command-line argument, whether
    /// that launches the program or lands on its Dock icon while it is
    /// already running. See <see cref="FlybackApp.OnFrameworkInitializationCompleted"/>.
    /// </summary>
    /// <remarks>
    /// Not while a dialog is up. A dialog stops the pointer and the keyboard and
    /// neither of these arrives by them, so with nothing unsaved to ask about the
    /// document would be replaced behind the sheet — and a text one takes the
    /// focus with it, out of a dialog that then no longer hears Escape.
    /// </remarks>
    internal Task OpenActivatedFileAsync(IStorageFile file) => patchOpening.OpenActivatedFileAsync(file);

    #endregion

    #region The document's buttons and write-back gestures

    // What the window shows of the Document: the code button, the tidy
    // button, and the panel's gestures that end in a write-back.

    /// <summary>
    /// Puts the tidy button, the panel and the edit state in step with who owns the
    /// patch and which view is showing.
    /// </summary>
    private void ShowOwnership()
    {
        // Laying out is off only where it would not last: a locked canvas is
        // re-laid on the next evaluation, so tidying one is work thrown away.
        // Showing the text, the same button folds the lines instead.
        toolbar.Tidy.IsEnabled = document.ShowingCode || !document.Owned;

        ToolTip.SetTip(toolbar.Tidy, document.ShowingCode
            ? "Fold the long lines so the patch reads down the page  (Ctrl+L)"
            : document.Owned
                ? "The text is the document, so the canvas is laid out from it on every "
                  + "apply. Fold the text instead."
                : Toolbar.TidyTip);

        // What the empty panel says is a list of gestures, and half of them
        // have just been switched off or back on.
        inspector.Build();
        RefreshEditState();

        ToolTip.SetTip(
            inspector.Panel,
            document.Owned
                ? "The text is the document. A knob turned here is written back into it "
                  + "where it already says it."
                : null);
    }

    #endregion

    #region Playback, reporting and closing down

    // What the window shows of Playback, and the one place anything is
    // said to the user.

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

    /// <summary>
    /// The same, for everything a compile found at once. Each is a line of its
    /// own in the log; the bar joins them, having only the one line.
    /// </summary>
    private void Report(IReadOnlyList<string> messages) => report.Say(messages);

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

    #endregion

    #region Output settings

    // The window wires output controls and reacts to changes; OutputSettingsUse
    // applies and saves what the sections show.

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

    #endregion

    #region Layout

    private void RememberLayout()
    {
        if (!shell.IsBuilt) return;

        layoutKeeper.Remember(() => shell.Capture(this));
    }

    #endregion

    #region Recording

    // The window and keyboard send the Record gesture to the take service.

    /// <summary>The take this window is recording, counting in, or about to.</summary>
    internal TakeRecording Recording { get; }

    #endregion

    #region Recovery

    // What a crash would lose, and putting it back at the next start — ADR-0103.
    // What is handed to WorkKeeper is the document as the unsaved question
    // sees it — the patch, the text where the text is the document, what a bundle carried
    // and the conversation — so that what comes back is what that question would have
    // offered to save.

    /// <summary>Unsaved work kept against a crash, or null where none is kept — which is every test.</summary>
    private readonly WorkKeeper keeper;
    private readonly WindowLayoutKeeper layoutKeeper;
    private readonly FullScreenPreview fullScreen;
    private readonly TransportControls transport;

    #endregion

}
