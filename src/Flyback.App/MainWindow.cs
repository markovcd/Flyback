using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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
    /// <summary>Takes the picture and the sound back to zero seconds.</summary>
    private void RewindToZero() => playback.Rewind();

    /// <summary>The Graphics, Recording and Sound sections, and what they were last saved as.</summary>
    private readonly OutputSections outputSections;

    private readonly OutputSettingsUse outputSettingsUse;

    private readonly SettingsSession settingsSession;

    private readonly OutputSettingRepository outputSettingRepository;

    private readonly CanvasSection canvasSection;

    private readonly UpdatesSection updatesSection;

    private readonly UsageSection usageSection;

    private readonly FilesSection filesSection;

    private readonly NodeEditor editor;

    private readonly SourceView source;

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

    /// <summary>Installing and removing plugins, and the plugins window.</summary>
    private readonly PluginInstalls pluginInstalls;

    /// <summary>The panel knobs, their learning and the knobs over the picture.</summary>
    private readonly PanelKnobs knobs;

    /// <summary>The panel on the right, under the preview.</summary>
    private readonly Inspector inspector;

    /// <summary>
    /// The one line anything is said on, and the log of what has been said. See
    /// <see cref="Report"/>, which is the only thing that writes to it.
    /// </summary>
    private readonly ReportLine report;

    private readonly AssistantPanel assistant;
    private readonly ShellLayout shell;

    /// <summary>
    /// Read before this window existed, and already installed. Nothing here
    /// knows which backends or modules there are, or what they are called.
    /// </summary>
    private readonly PluginCatalog plugins;

    /// <summary>The patch compiled and played, paused or muted.</summary>
    private readonly Playback playback;

    private readonly PlaybackControls playbackControls;

    private readonly EditorOpened editorOpened;

    /// <summary>
    /// What runs the processor's programs as machine code once they are built —
    /// the sound always, and the picture while the processor is drawing it. See
    /// ADR-0076.
    /// </summary>
    private readonly IlCompiler compiler;

    /// <summary>
    /// Everything that plays the patch from outside it. The mirror of
    /// <see cref="audio"/>, which takes what the patch makes to a device. Assigned
    /// in the constructor because it is handed the MIDI backend the plugins
    /// offered, which is declared below it.
    /// </summary>
    private readonly MidiHub midi;

    /// <summary>What unsaved work there is, and the question closing it asks.</summary>
    private readonly UnsavedWork unsaved;

    /// <param name="setup">Where this machine keeps things and what this launch asked for.</param>
    public MainWindow(
        EditorSetup setup,
        NodeEditor editor,
        SourceView source,
        Document document,
        PatchFiles files,
        PatchOpening patchOpening,
        UnsavedWork unsaved,
        PreviewHost preview,
        ReportLine report,
        PluginCatalog plugins,
        Usage usage,
        IlCompiler compiler,
        MidiHub midi,
        Playback playback,
        OutputSections outputSections,
        CanvasSection canvasSection,
        UpdatesSection updatesSection,
        UsageSection usageSection,
        FilesSection filesSection,
        PanelKnobs knobs,
        Inspector inspector,
        PluginInstalls pluginInstalls,
        PresetSlot presets,
        Toolbar toolbar,
        TakeRecording recording,
        AssistantPanel assistant,
        WorkKeeper keeper,
        WorkRecovery workRecovery,
        PlaybackControls playbackControls,
        EditorOpened editorOpened,
        WindowLayoutKeeper layoutKeeper,
        FullScreenPreview fullScreen,
        TransportControls transport,
        ShellLayout shell,
        OutputSettingRepository outputSettingRepository,
        OutputSettingsUse outputSettingsUse,
        SettingsSession settingsSession,
        IDialog dialog)
    {
        this.editor = editor;
        this.source = source;
        this.document = document;
        this.files = files;
        this.patchOpening = patchOpening;
        this.unsaved = unsaved;
        this.preview = preview;
        this.report = report;
        this.plugins = plugins;
        this.usage = usage;
        this.compiler = compiler;
        this.midi = midi;
        this.playback = playback;
        this.playbackControls = playbackControls;
        this.editorOpened = editorOpened;
        this.outputSections = outputSections;
        this.outputSettingsUse = outputSettingsUse;
        this.settingsSession = settingsSession;
        this.canvasSection = canvasSection;
        this.updatesSection = updatesSection;
        this.usageSection = usageSection;
        this.filesSection = filesSection;
        this.knobs = knobs;
        this.inspector = inspector;
        this.pluginInstalls = pluginInstalls;
        this.presets = presets;
        this.toolbar = toolbar;
        this.assistant = assistant;
        this.keeper = keeper;
        this.workRecovery = workRecovery;
        this.layoutKeeper = layoutKeeper;
        this.fullScreen = fullScreen;
        this.transport = transport;
        this.shell = shell;
        this.outputSettingRepository = outputSettingRepository;
        this.dialog = dialog;

        // Closed until the toolbar opens it.
        assistant.IsVisible = false;

        Recording = recording;

        pluginInstalls.RestartRequested += async (_, request) =>
        {
            try
            {
                request.Complete(await unsaved.RelaunchAsync(request.Reopen, recording.InHand));
            }
            catch (Exception ex)
            {
                request.Fail(ex);
            }
        };

        setupOfLaunch = setup;

        // Where the last document's knobs were left says nothing about this one's.
        files.Arrived += (_, _) => knobs.Hub.Forget();
        files.Saved += (_, _) => ClearPresetSelection();

        // A take running takes Pause away, and finishing gives it back.
        recording.Marked += (_, _) => transport.Sync();

        playbackControls.Wire();

        // A key going down while the clock is stopped changes the picture and
        // moves nothing else, so the preview has to be told there is a new frame
        // to draw. Everything else it redraws for, it can see for itself.
        midi.Played += () => preview.Refresh();

        // A device that would not open. The patch goes on naming it and goes on
        // being silent, and this line is the only thing that would say why.
        midi.Trouble += message => Report(message);

        // That an instrument was played at all, counted for the end of the run
        // and nothing about what was played on it (ADR-0103).
        midi.Heard += () => usage.Count(Used.Instrument);

        WireControls();

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

        editor.History.PatchChanged += (_, _) =>
        {
            playback.Recompile(files.Sounds, files.Pictures, () => Recording.Running, opened: editor.History.Opening);
            // Patching an input takes its knob away and unpatching gives it
            // back, and neither is a selection change — so the panel is asked
            // here as well, and answers only when a wire actually moved.
            inspector.Sync();
        };

        editor.Selection.Changed += (_, _) =>
        {
            inspector.Build();
            playback.ProbeSelectionChanged(files.Sounds, files.Pictures, () => Recording.Running);
        };
        editor.History.HistoryChanged += (_, _) => RefreshEditState();

        // Asked again rather than simply put away: the wire may have been dropped
        // back onto 'color', and then there is nothing to put back.
        editor.Gestures.GestureFinished += (_, _) =>
        {
            if (shell.PreviewHideWaiting) shell.ShowPreview(playback.HasPicture);
        };

        // What the canvas has to say goes on the one line everything is said on.
        editor.Report.Said += (_, message) => Report(message);

        // The other copy of everything said: a status bar is written over by the
        // next compile and the log behind it is five deep, so a run watched from a
        // terminal would keep no account of itself. Trace rather than the console
        // directly — Program.Main decides whether there is a terminal worth writing
        // to, and a second destination is a second listener.
        report.Said += (_, message) =>
            Trace.WriteLine($"{DateTime.Now:HH:mm:ss}  {message}");

        // Before the layout, because these are live from the moment the window
        // is: the preview needs its resolution and its backend whether or not
        // anybody has selected the Output to look at them.
        WireOutputControls();

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

        keeper.Start();

        layoutKeeper.Load();
        layoutKeeper.Apply(this);

        // Before anything is compiled and before a panel is drawn, because a
        // MIDI In asks this what there is to listen to as soon as either
        // happens. The list is the window's: the computer's keyboard is only an
        // instrument while there is a window for it to be typed into.
        MidiSources.Install(() => [.. midi.Sources.Select(source => source with { Conducts = knobs.Instruments.For(source)?.Conducts == true })]);

        // Here rather than at the launch, because what a run started as includes
        // which backend actually opened, and that is only known once one has been
        // asked for.
        usage.Started(plugins.Plugins.Select(plugin => plugin.Info.Id), playback.Sound.Output?.Id, ScreenHeights());

        // The preset the box opens on: whichever the Graphics section's "Startup
        // patch" is set to, or the first of the list for a name it no longer
        // offers — said here so the title and the toolbar's own selection agree
        // with the canvas from the first frame (ADR-0093).
        presets.StartOn(outputSettingRepository.Current.DefaultPreset);

        // No manual switch any more — Volume is the one now, and the Recompile
        // that patch assignment just ran already brought sound up to match its
        // default (ADR-0079). What is left to say only where turning it up would
        // not help: nothing was there to open it with.
        if (playback.Sound.Output is null)
            Report("No sound backend is installed, so Volume will do nothing. "
                + "See About for where plugins are looked for.");

        // Said once, because nothing else on screen shows it, and a run that is
        // slower for a reason should say which.
        if (setupOfLaunch.Interpreted)
            Report($"Running interpreted ({Startup.InterpretedFlag}): the CPU's programs are not compiled this run.");

        // Last, so it is what the bar is showing when the window first appears.
        if (setupOfLaunch.WhatsNew is null && setupOfLaunch.OpeningNote is not null) Report(setupOfLaunch.OpeningNote);

        shell.ApplyPanelLayout();
    }

    private readonly EditorSetup setupOfLaunch;

    private bool started;

    private Control BuildLayout()
    {
        WireToolbar();
        // The popups behind the report and a module's name hang off the window
        // rather than off the control, so what they look like is said here.
        Styles.Add(ReportLine.Trim());
        Styles.Add(ModulePlate.Naming());
        Styles.Add(ModulePalette.Trim());
        return shell.Build(RefreshEditState);
    }

    /// <summary>What each button on the toolbar does. Called once, as the window is built.</summary>
    private void WireToolbar()
    {
        toolbar.Open.Click += async (_, _) => await patchOpening.PickAndOpenAsync();
        toolbar.Save.Click += async (_, _) => await unsaved.SavePatchAsync();

        // All three go to whichever view is showing — see Document.
        toolbar.Undo.Click += (_, _) => document.Undo();
        toolbar.Redo.Click += (_, _) => document.Redo();
        toolbar.Tidied += (_, onlySelected) => document.Tidy(onlySelected);

        toolbar.Swap.IsCheckedChanged += (_, _) => shell.SwapPreview(toolbar.Swap.IsChecked == true);

        toolbar.Pause.Click += (_, _) => transport.TogglePause();
        toolbar.Rewind.Click += (_, _) => RewindToZero();
        toolbar.Record.Click += async (_, _) => await Recording.ToggleAsync();

        toolbar.Assistant.IsCheckedChanged += (_, _) => shell.ShowAssistant(toolbar.Assistant.IsChecked == true);
        toolbar.Settings.Click += async (_, _) => await ShowSettingsAsync();
        toolbar.Plugins.Click += async (_, _) => await pluginInstalls.ShowAsync();
        toolbar.About.Click += async (_, _) => await ShowAboutAsync();

        WireDocument();
        RefreshEditState();
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

    private Task ShowSettingsAsync() => settingsSession.ShowAsync();

    /// <summary>
    /// The About window. Its contents are built fresh each time rather than kept
    /// like the settings section: nothing in it is a control anybody has typed
    /// into, so there is nothing to carry from one opening to the next.
    /// </summary>
    private Task ShowAboutAsync() => dialog.Show("About", About.View());

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

    /// <inheritdoc cref="UnsavedWork.SaveToAsync"/>
    internal Task<bool> SaveToAsync(IStorageFile file) => unsaved.SaveToAsync(file);

    #endregion

    #region The document's buttons and write-back gestures

    // What the window shows of the Document: the code button, the tidy
    // button, and the panel's gestures that end in a write-back.

    /// <summary>Called once, as the window is built.</summary>
    private void WireDocument()
    {
        document.EditStateChanged += (_, _) => RefreshEditState();
        document.PanelStale += (_, _) => inspector.Build();
        document.OwnershipChanged += (_, _) => ShowOwnership();
        document.ViewChanged += (_, _) =>
        {
            if (toolbar.Code.IsChecked != document.ShowingCode) toolbar.Code.IsChecked = document.ShowingCode;
        };

        toolbar.Code.IsCheckedChanged += (_, _) => document.ShowCode(toolbar.Code.IsChecked == true);

        source.EditorFontSize = canvasSection.EditorFontSize;
        source.EditorFontSizeChanged += (_, size) => canvasSection.SaveEditorFontSize(size);

        // The buffer is emptied by the handover and written nowhere on the way, so
        // typing not on disk yet is asked about as it is when a document is closed over.
        source.HandBackRequested += async (_, _) =>
        {
            if (document.Owned && await unsaved.MayLoseTheTextAsync()) document.HandBack();
        };

        // Caught on the way up and after whoever handled it, because a slider
        // captures the pointer: letting go halfway across the window is still
        // letting go of the slider, and the value written should be the one the
        // control finished on.
        inspector.Panel.AddHandler(
            PointerReleasedEvent,
            (_, _) => document.HandCameOff(),
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        // A number typed rather than dragged has no gesture to wait for, and the
        // focus going is the surest end of one.
        inspector.Panel.AddHandler(LostFocusEvent, (_, _) => document.HandCameOff(), RoutingStrategies.Bubble);

        // And a key let go of, because a number box takes what is typed as it is
        // typed. On the way up rather than down, because the character is taken
        // between the two; any key, since an arrow steps the value and a backspace
        // clears it without giving up the focus.
        inspector.Panel.AddHandler(
            KeyUpEvent,
            (_, _) => document.HandCameOff(),
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        // And a notch of the wheel, the one way a number box moves that touches
        // neither the pointer's button nor the focus. Each notch is finished the
        // moment it lands.
        inspector.Panel.AddHandler(
            PointerWheelChangedEvent,
            (_, _) => document.HandCameOff(),
            RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

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
    private void WireOutputControls()
    {
        var gpu = outputSections.Gpu;

        compiler.Failed += message => Dispatcher.UIThread.Post(() => Report(message));

        preview.BackendChanged += message =>
        {
            // The choice rather than what is running: a patch the shader cannot
            // draw puts the picture on the processor without anybody having
            // asked, and a box that put itself back to CPU would then be read as
            // the setting having changed — and would be saved as changed the next
            // time anybody pressed Save.
            gpu.SelectedIndex = preview.Wanted == PreviewBackend.Gpu ? 0 : 1;
            gpu.IsEnabled = preview.GpuAvailable;
            ToolTip.SetTip(gpu, preview.GpuAvailable ? OutputSections.GpuTip : message);
            Report(message);
        };

        // The picture a take was reading has gone. Finishing the file is the only
        // useful thing left to do with it — what is already written is a
        // recording, and what would follow is the same frame for ever.
        preview.CaptureLost += Recording.Stop;

        knobs.BuildMidiSection(plugins, outputSections.Takeover, outputSections.KeyboardLayout);

        // Quietly, because nobody asked for anything yet: a saved answer is
        // what the program starts in, not a change to report.
        outputSections.Show();
        outputSettingsUse.ApplyCurrent();
    }

    #endregion

    #region Wiring the knobs

    private void WireControls()
    {
        toolbar.Knobs.IsCheckedChanged += (_, _) => shell.ShowControls(toolbar.Knobs.IsChecked == true);

        knobs.Wanted += (_, _) => shell.ShowControls(true);

        // A socket's own knob on the canvas: heard as it turns, written into the
        // text and the panel when the hand comes off it.
        editor.Dial.InputTurned += (_, pick) => document.Turned(pick.Node, pick.Port);
        editor.Dial.InputLetGo += (_, pick) =>
        {
            document.HandCameOff();
            if (editor.Selection.Focused?.Id == pick.Node || editor.Selection.Group?.Members.Contains(pick.Node) == true) inspector.Build();
        };
    }

    #endregion

    #region Layout

    internal void ShowPictureOn(Screen screen) => fullScreen.ShowPictureOn(screen);

    private void RememberLayout()
    {
        if (!shell.IsBuilt) return;

        layoutKeeper.Remember(() => shell.Capture(this));
    }

    #endregion

    #region Transport

    // Play and pause, and the mute that goes with them, on the toolbar and on the
    // full-screen preview.

    internal bool Paused => playback.Paused;

    internal bool Muted => playback.Muted;

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
    private readonly WorkRecovery workRecovery;
    private readonly WindowLayoutKeeper layoutKeeper;
    private readonly FullScreenPreview fullScreen;
    private readonly TransportControls transport;

    internal bool Recover(RecoveredWork work) => workRecovery.Restore(work);

    #endregion

    #region Usage

    /// <summary>
    /// What this run says about itself, which for every test and for a build with
    /// nowhere to send anything is nothing at all.
    /// </summary>
    private readonly Usage usage;

    /// <summary>
    /// How tall each screen is in pixels, for what a run started as to put in a band;
    /// empty where the platform will not say.
    /// </summary>
    private IReadOnlyList<int> ScreenHeights()
    {
        try
        {
            return Screens.All.Select(screen => screen.Bounds.Height).ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    #endregion
}
