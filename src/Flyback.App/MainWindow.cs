using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Flyback.App.Audio;
using Flyback.App.Controls;
using Flyback.App.Midi;
using Flyback.App.PluginPackages;
using Flyback.App.Statistics;
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

    private readonly CanvasSection canvasSection;

    private readonly UpdatesSection updatesSection;

    private readonly UsageSection usageSection;

    private readonly FilesSection filesSection;

    private readonly NodeEditor editor;

    private readonly SourceView source;

    /// <summary>Which of the canvas and the text owns the patch.</summary>
    private readonly Document document;

    /// <summary>Which file the patch is, and opening and saving it.</summary>
    private readonly PatchFiles files;

    private readonly PreviewHost preview;

    /// <summary>
    /// The pieces of the shell the fullscreen preview puts away and has to bring
    /// back. Nullable only because the layout is built after the fields are, and
    /// never null once <see cref="BuildLayout"/> has run.
    /// </summary>
    private Grid? columns;
    private Border? previewBox;

    /// <summary>The preset slot on the toolbar, and the gallery it opens.</summary>
    private readonly PresetSlot presets;

    /// <summary>
    /// The preview's own row and the splitter below it, put away when the patch
    /// has nothing wired into the Output's 'color' — see <see cref="ShowPreview"/>.
    /// </summary>
    private RowDefinition? previewRow;
    private GridSplitter? previewSplitter;

    /// <summary>The preview row's height while it was last shown, kept across a hide.</summary>
    private GridLength previewShare = new(1, GridUnitType.Star);

    /// <summary>
    /// The canvas and the text, over the knobs or the inspector: what trades
    /// places with the preview — see <see cref="SwapPreview"/>. Null only before
    /// <see cref="BuildLayout"/> has run.
    /// </summary>
    private Grid? patchPane;

    /// <summary>The inspector's panel, which goes under the canvas while swapped.</summary>
    private Border? inspectorBox;

    /// <summary>The columns of <see cref="columns"/> the patch and the preview trade.</summary>
    private const int WideColumn = 2;
    private const int SideColumn = 4;

    /// <summary>
    /// Set while the patch has lost its picture during a gesture on the swapped
    /// canvas, and the layout is waiting for the gesture to end to go back.
    /// </summary>
    private bool previewHideWaiting;

    /// <summary>The bar along the bottom.</summary>
    private readonly StatusBar statusBar;

    /// <summary>The bar along the top.</summary>
    private readonly Toolbar toolbar;

    /// <summary>The palette, opened at the pointer (ADR-0046).</summary>
    private readonly Palette palette;

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
    private ColumnDefinition? assistantColumn;
    private GridSplitter? assistantSplitter;

    /// <summary>
    /// How wide the assistant was when it was last open. A pixel width, not a
    /// share of the window — resizing the window resizes the patch beside it,
    /// not the conversation.
    /// </summary>
    private GridLength assistantShare = new(320, GridUnitType.Pixel);

    /// <summary>
    /// Read before this window existed, and already installed. Nothing here
    /// knows which backends or modules there are, or what they are called.
    /// </summary>
    private readonly PluginCatalog plugins;

    private readonly AudioEngine audio;

    /// <summary>The patch compiled and played, paused or muted.</summary>
    private readonly Playback playback;

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
        Shell shell,
        NodeEditor editor,
        SourceView source,
        Document document,
        PatchFiles files,
        UnsavedWork unsaved,
        PreviewHost preview,
        ReportLine report,
        PluginCatalog plugins,
        Usage usage,
        IlCompiler compiler,
        AudioEngine audio,
        MidiHub midi,
        Playback playback,
        OutputSections outputSections,
        CanvasSection canvasSection,
        UpdatesSection updatesSection,
        UsageSection usageSection,
        FilesSection filesSection,
        PanelKnobs knobs,
        Palette palette,
        Inspector inspector,
        PluginInstalls pluginInstalls,
        PresetSlot presets,
        Toolbar toolbar,
        StatusBar statusBar,
        TakeRecording recording,
        AssistantPanel assistant,
        WorkKeeper? keeper)
    {
        // First, so every region can reach the window it is in.
        shell.Attach(this);

        this.editor = editor;
        this.source = source;
        this.document = document;
        this.files = files;
        this.unsaved = unsaved;
        this.preview = preview;
        this.report = report;
        this.plugins = plugins;
        this.usage = usage;
        this.compiler = compiler;
        this.audio = audio;
        this.midi = midi;
        this.playback = playback;
        this.outputSections = outputSections;
        this.canvasSection = canvasSection;
        this.updatesSection = updatesSection;
        this.usageSection = usageSection;
        this.filesSection = filesSection;
        this.knobs = knobs;
        this.palette = palette;
        this.inspector = inspector;
        this.pluginInstalls = pluginInstalls;
        this.presets = presets;
        this.toolbar = toolbar;
        this.statusBar = statusBar;
        this.assistant = assistant;
        this.keeper = keeper;

        Recording = recording;

        outputSettingsPath = setup.OutputSettingsPath;

        layoutPath = setup.LayoutPath;
        if (setup.LayoutPath is not null) layout = WindowLayout.Load(setup.LayoutPath);

        // Here rather than at the launch, because what a run started as includes
        // which backend actually opened, and that is only known once one has been
        // asked for.
        usage.Started(plugins.Plugins.Select(plugin => plugin.Info.Id), playback.Sound.Output?.Id, ScreenHeights());

        // Where the last document's knobs were left says nothing about this one's.
        files.Arrived += (_, _) => knobs.Hub.Forget();
        files.Saved += (_, _) => ClearPresetSelection();

        // A take running takes Pause away, and finishing gives it back.
        recording.Marked += (_, _) => SyncTransport();

        WirePlayback();

        // Before anything is compiled and before a panel is drawn, because a
        // MIDI In asks this what there is to listen to as soon as either
        // happens. Installed here rather than in Startup because the list is the
        // window's — the computer's keyboard is only an instrument while there
        // is a window for it to be typed into.
        MidiSources.Install(() => [.. midi.Sources.Select(source => source with { Conducts = knobs.Instruments.For(source)?.Conducts == true })]);

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
        ApplyWindowLayout();

        editor.History.PatchChanged += (_, _) =>
        {
            playback.Recompile(opened: editor.History.Opening);
            if (editor.History.Opening) statusBar.Compiling.Watch(() => playback.Starting);

            // Patching an input takes its knob away and unpatching gives it
            // back, and neither is a selection change — so the panel is asked
            // here as well, and answers only when a wire actually moved.
            inspector.Sync();
        };

        editor.Selection.Changed += (_, _) =>
        {
            inspector.Build();
            playback.ProbeSelectionChanged();
        };
        editor.History.HistoryChanged += (_, _) => RefreshEditState();

        // Asked again rather than simply put away: the wire may have been dropped
        // back onto 'color', and then there is nothing to put back.
        editor.Gestures.GestureFinished += (_, _) =>
        {
            if (previewHideWaiting) ShowPreview(playback.HasPicture);
        };

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

        // The preset the box opens on: whichever the Graphics section's "Startup
        // patch" is set to, or the first of the list for a name it no longer
        // offers — said here so the title and the toolbar's own selection agree
        // with the canvas from the first frame (ADR-0093).
        presets.StartOn(outputSections.Saved.DefaultPreset);

        // No manual switch any more — Volume is the one now, and the Recompile
        // that patch assignment just ran already brought sound up to match its
        // default (ADR-0079). What is left to say only where turning it up would
        // not help: nothing was there to open it with.
        if (playback.Sound.Output is null)
            Report("No sound backend is installed, so Volume will do nothing. "
                + "See About for where plugins are looked for.");

        // Said once, because nothing else on screen shows it, and a run that is
        // slower for a reason should say which.
        if (setup.Interpreted)
            Report($"Running interpreted ({Startup.InterpretedFlag}): the CPU's programs are not compiled this run.");

        // Last, so it is what the bar is showing when the window first appears.
        if (setup.WhatsNew is null && setup.OpeningNote is not null) Report(setup.OpeningNote);

        ApplyPanelLayout();

        // Opened rather than called straight away: there is nothing to put a
        // dialog over before, and the platform window behind this one — and the
        // storage provider that comes with it — is not guaranteed to exist until
        // then, which OpenPathAsync cannot wait for a click to find out. One
        // handler, so the three come one after another: what changed, then what
        // a crash left, then the file this launch was for, which asks about
        // unsaved work like any other and so about work just restored.
        Opened += async (_, _) =>
        {
            if (setup.WhatsNew is not null) await this.ShowDialog(WhatsNew.Title(setup.WhatsNew), WhatsNew.View(setup.WhatsNew));

            keeper?.Restore(Recover);

            // A plugin package replaces nothing, so it asks about nothing unsaved.
            if (setup.OpenPath is { } path && (PluginPackage.Named(path) || await unsaved.MayReplaceThePatchAsync()))
                await OpenPathAsync(path);

            if (setup.OpenShared is { Length: > 0 } id) await presets.OpenSharedAgainAsync(id);
        };
    }

    private Control BuildLayout()
    {
        var root = new DockPanel();


        // A conversation is saved with the patch, so one with a turn nobody has
        // saved is something the title and the close have to know about.
        assistant.ConversationChanged += (_, _) => RefreshEditState();

        // Which modules the assistant is not told about is a question about the
        // catalog and the settings, so it moves only when settings are saved.
        editor.Tags.Types = assistant.Undescribed;
        assistant.UndescribedChanged += (_, _) =>
        {
            editor.Tags.Types = assistant.Undescribed;
            inspector.Build();
        };

        WireToolbar();
        // The popups behind the report and a module's name hang off the window
        // rather than off the control, so what they look like is said here.
        Styles.Add(ReportLine.Trim());
        Styles.Add(ModulePlate.Naming());
        DockPanel.SetDock(toolbar.View, Dock.Top);
        DockPanel.SetDock(statusBar.View, Dock.Bottom);

        // The two flexible columns are star-sized: GridSplitter redistributes
        // star weights, and a fixed-pixel column next to one just gets squeezed.
        // The assistant's is a pixel width, so a resize of the window goes to the
        // patch and leaves the conversation the width it was left at. Leftmost and
        // at full height, since what it talks about is the patch (ADR-0087).
        //
        // The rows belong to whichever column the preview is in — preview,
        // splitter, then the inspector or, swapped, the knobs — and the patch
        // spans all three in the other. One grid, so the preview and the canvas
        // trade places by changing cells: the preview is never taken off its
        // parent, which would tear its GPU context down (see ShowFullScreenPreview).
        columns = new Grid
        {
            // Named because the fullscreen preview's test has to find exactly
            // this grid, and counting its columns stopped telling it apart from
            // the toolbar's the moment the palette left the layout.
            Name = "columns",
            ColumnDefinitions =
            [
                new ColumnDefinition(assistantShare),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(3, GridUnitType.Star)) { MinWidth = 280 },
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(1.6, GridUnitType.Star)) { MinWidth = 300 },
            ],
            RowDefinitions =
            [
                new RowDefinition(new GridLength(1, GridUnitType.Star)) { MinHeight = 140 },
                new RowDefinition(GridLength.Auto),
                new RowDefinition(new GridLength(1.1, GridUnitType.Star)) { MinHeight = 120 },
            ],
        };

        assistantColumn = columns.ColumnDefinitions[0];
        assistantSplitter = new GridSplitter { Background = Brushes.Transparent, Width = 5 };

        // The canvas over whatever the preview's column is not holding: the
        // knobs, or swapped, the inspector.
        var patch = patchPane = new Grid
        {
            RowDefinitions =
            [
                new RowDefinition(new GridLength(2.2, GridUnitType.Star)) { MinHeight = 160 },
                new RowDefinition(GridLength.Auto),
                new RowDefinition(new GridLength(0)),
            ],
        };

        // The text sits in the canvas's own row rather than under it: they are
        // two views of one patch and only ever one of them shows, so putting
        // them side by side would halve the room for each and invite the
        // question ADR-0068 exists to answer — which one is being edited.
        Grid.SetRow(editor, 0);
        Grid.SetRow(source, 0);
        Grid.SetRow(controlsSplitter, 1);
        Grid.SetRow(knobs.View, 2);

        patch.Children.Add(editor);
        patch.Children.Add(source);
        patch.Children.Add(controlsSplitter);
        patch.Children.Add(knobs.View);

        Styles.Add(ModulePalette.Trim());

        foreach (var (child, column) in new (Control, int)[]
                 {
                     (assistant, 0),
                     (assistantSplitter, 1),
                     (patch, WideColumn),
                     (new GridSplitter { Width = 5, Background = Brushes.Transparent }, 3),
                 })
        {
            Grid.SetColumn(child, column);
            Grid.SetRowSpan(child, 3);
            columns.Children.Add(child);
        }

        BuildRightPanel(columns, SideColumn);

        // Hidden costs nothing, which is why this needs no dialog — and this
        // application has none.
        ShowAssistant(false);

        root.Children.Add(toolbar.View);
        root.Children.Add(statusBar.View);
        root.Children.Add(columns);

        return root;
    }

    /// <summary>
    /// Opens or closes the assistant, and gives its pixel width back when it
    /// closes.
    /// </summary>
    /// <remarks>
    /// A pixel column keeps its width whether or not anything in it is visible, so
    /// hiding the panel alone would leave that many pixels empty. The width is
    /// kept rather than recomputed, and the column's minimum has to go with it,
    /// since a minimum outranks a width of zero.
    /// </remarks>
    private void ShowAssistant(bool shown)
    {
        if (assistant is null || assistantColumn is null || assistantSplitter is null) return;

        if (!shown && assistant.IsVisible) assistantShare = assistantColumn.Width;

        assistant.IsVisible = shown;
        assistantSplitter.IsVisible = shown;

        assistantColumn.MinWidth = shown ? 280d : 0d;
        assistantColumn.Width = shown ? assistantShare : new GridLength(0);
    }

    /// <summary>
    /// Puts the preview away when the patch has nothing wired into the Output's
    /// 'color', so the inspector takes the row rather than sitting under a box that
    /// could only show black.
    /// </summary>
    /// <remarks>
    /// The same shape as <see cref="ShowAssistant"/> and for the same reason. Left
    /// alone while the preview has the window, since
    /// <see cref="ShowFullScreenPreview"/> is already driving these rows and the
    /// two would fight over what a height of zero means.
    /// </remarks>
    private void ShowPreview(bool shown)
    {
        // Pulling the wire off 'color' swapped back would move the canvas out from
        // under the hand still holding that wire, so nothing moves until the
        // button comes up — see the editor's GestureFinished, which asks again.
        previewHideWaiting = !shown && toolbar.Swap.IsChecked == true && editor.Gestures.Gesturing;
        if (previewHideWaiting) return;

        // Ahead of the full screen guard, so the button is right by the time the
        // toolbar comes back.
        toolbar.Swap.IsEnabled = shown;
        ToolTip.SetTip(toolbar.Swap, shown ? Toolbar.SwapTip : Toolbar.NoPictureToSwapTip);

        if (previewIsFullScreen) return;
        if (previewBox is null || previewRow is null || previewSplitter is null) return;

        // A picture that has gone gives the canvas its column back before the row
        // it would stand in is put away. Unticking is what moves it — see the
        // button's handler in WireToolbar.
        if (!shown) toolbar.Swap.IsChecked = false;

        if (!shown && previewBox.IsVisible) previewShare = previewRow.Height;

        previewBox.IsVisible = shown;
        previewSplitter.IsVisible = shown;

        previewRow.MinHeight = shown ? 140d : 0d;
        previewRow.Height = shown ? previewShare : new GridLength(0);
    }

    /// <summary>
    /// Puts the preview in the wide column over the knobs, and the canvas in the
    /// narrow one over the inspector — or puts both back.
    /// </summary>
    /// <remarks>
    /// The columns keep their widths, and the assistant keeps its place beside
    /// the wide one. The knobs and the inspector trade grids, and the sizes of the
    /// rows they stand in trade with them.
    /// </remarks>
    private void SwapPreview(bool swapped)
    {
        if (columns is null || previewBox is null || patchPane is null
            || inspectorBox is null || previewSplitter is null) return;

        if (swapped == (Grid.GetColumn(previewBox) == WideColumn)) return;

        var (pictureColumn, patchColumn) = swapped ? (WideColumn, SideColumn) : (SideColumn, WideColumn);

        Grid.SetColumn(previewBox, pictureColumn);
        Grid.SetColumn(patchPane, patchColumn);

        Hang(swapped ? controlsSplitter : previewSplitter, columns, pictureColumn, 1);
        Hang(swapped ? knobs.View : inspectorBox, columns, pictureColumn, 2);
        Hang(swapped ? previewSplitter : controlsSplitter, patchPane, 0, 1);
        Hang(swapped ? inspectorBox : knobs.View, patchPane, 0, 2);

        var outer = columns.RowDefinitions[2];
        var inner = patchPane.RowDefinitions[2];

        (outer.MinHeight, inner.MinHeight) = (inner.MinHeight, outer.MinHeight);
        (outer.Height, inner.Height) = (inner.Height, outer.Height);

        if (swapped) usage.Count(Used.Swapped);

        static void Hang(Control child, Grid grid, int column, int row)
        {
            if (child.Parent != grid)
            {
                (child.Parent as Panel)?.Children.Remove(child);
                grid.Children.Add(child);
            }

            Grid.SetColumn(child, column);
            Grid.SetRow(child, row);
        }
    }

    /// <summary>What each button on the toolbar does. Called once, as the window is built.</summary>
    private void WireToolbar()
    {
        toolbar.Open.Click += async (_, _) => await OpenAnotherPatchAsync();
        toolbar.Save.Click += async (_, _) => await unsaved.SavePatchAsync();

        // All three go to whichever view is showing — see Document.
        toolbar.Undo.Click += (_, _) => document.Undo();
        toolbar.Redo.Click += (_, _) => document.Redo();
        toolbar.Tidied += (_, onlySelected) => document.Tidy(onlySelected);

        toolbar.Swap.IsCheckedChanged += (_, _) => SwapPreview(toolbar.Swap.IsChecked == true);

        toolbar.Pause.Click += (_, _) => TogglePause();
        toolbar.Rewind.Click += (_, _) => RewindToZero();
        toolbar.Record.Click += async (_, _) => await ToggleRecordAsync();

        toolbar.Assistant.IsCheckedChanged += (_, _) => ShowAssistant(toolbar.Assistant.IsChecked == true);
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

    /// <summary>
    /// Set while the settings window is up, so the app is not closed under it.
    /// </summary>
    /// <remarks>
    /// The window is a panel over this one, so the frame's cross stays live
    /// underneath it. Closing through it would leave the settings neither saved
    /// nor discarded — the one answer the window exists to get — so the close is
    /// refused until Save or Cancel has given it.
    /// </remarks>
    private bool settingsAreUp;

    /// <summary>
    /// The settings window. One button on the toolbar rather than one per thing
    /// that has settings, so what it holds can grow without the bar doing the
    /// same. A tab a section: the agent, the picture, recording and sound
    /// (ADR-0082).
    /// </summary>
    private async Task ShowSettingsAsync()
    {
        if (assistant is not { } panel) return;

        // Asked as the window opens rather than kept from the last time, and not
        // awaited: the window opens while the search runs and the note fills itself in.
        _ = outputSections.ShowFfmpegAsync();

        bool saved;

        settingsAreUp = true;

        usage.Count(Used.Settings);

        try
        {
            saved = await SettingsDialog.ShowAsync(this,
            [
                ("Graphics", outputSections.Graphics),
                ("Canvas", canvasSection.View),
                ("Recording", outputSections.Recording),
                ("Sound", outputSections.Sound),
                ("MIDI", knobs.MidiSection),
                ("Assistant", panel.SettingsSection()),
                ("Files", filesSection.View),
                ("Updates", updatesSection.View),
                ("Usage", usageSection.View),
            ]);
        }
        finally
        {
            settingsAreUp = false;
        }

        if (saved)
        {
            panel.SaveSettings();
            SaveOutputSettings();
            updatesSection.Save();
            usageSection.Save();
            canvasSection.Save();
            filesSection.Save();

            return;
        }

        // Whatever was typed or picked since it opened belongs to that window, and
        // only Save is allowed to keep it.
        panel.DiscardSettings();
        outputSections.Show(outputSections.Saved);
        updatesSection.Show();
        usageSection.Show();
        canvasSection.Show();
        filesSection.Show();
    }

    /// <summary>
    /// The About window. Its contents are built fresh each time rather than kept
    /// like the settings section: nothing in it is a control anybody has typed
    /// into, so there is nothing to carry from one opening to the next.
    /// </summary>
    private async Task ShowAboutAsync() =>
        await this.ShowDialog("About", About.View());

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
        if (unsaved.Asking || settingsAreUp)
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
        if (e.Handled || this.HasDialogUp) return;

        // Before the modifier check, because Escape carries none. Only while the
        // picture is full screen: everywhere else Escape belongs to the module
        // filter, which handles its own before this is ever reached.
        if (e.Key == Key.Escape && (previewIsFullScreen || pictureWindow is not null))
        {
            LeaveFullScreen();
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
            // button already is: see ToggleRecordAsync.
            case Key.R:
                _ = ToggleRecordAsync();
                e.Handled = true;
                break;

            // The panel has no room while the picture has the window.
            case Key.K:
                if (!previewIsFullScreen) ShowControls(!knobs.View.IsVisible);

                e.Handled = true;
                break;

            // With Ctrl because the bare letter is a note, and Space adds a module.
            case Key.P:
                TogglePause();
                e.Handled = true;
                break;

            // The document itself, on the letters every program uses for it.
            // Both were the toolbar's alone, and the hand that has just
            // finished an edit is on the keyboard rather than the pointer.
            // Saving is one gesture here — the picker is where a name is
            // chosen — so there is no second key for saving under another one.
            case Key.O:
                _ = OpenAnotherPatchAsync();
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

    /// <summary>
    /// Whether the computer's keyboard is an instrument right now — whether either
    /// of the running programs is reading it.
    /// </summary>
    /// <remarks>
    /// Asked of the compiled programs rather than of the patch, which is what makes
    /// it exact: a MIDI In wired to nothing is read by neither and should not take
    /// keystrokes from the editor, and one wired only to the speakers should. Dead
    /// -code elimination has already answered both (ADR-0022).
    /// </remarks>
    private bool Playing =>
        !Typing
        && (Reads(preview.Program.LiveInputs) || Reads(audio.Live.Keys));

    private static bool Reads(IReadOnlyList<string> inputs) =>
        inputs.Any(key => key.StartsWith(MidiSources.Keyboard + "/", StringComparison.Ordinal));

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
    /// The Open gesture whole: what is unsaved is asked about, and then the
    /// picker. The toolbar's button and Ctrl+O both come through here, so the
    /// question cannot be stepped round by reaching for the keyboard.
    /// </summary>
    private async Task OpenAnotherPatchAsync()
    {
        if (await unsaved.MayReplaceThePatchAsync() && await files.PickOpenAsync() is { } file) await OpenFileAsync(file);
    }

    /// <summary>
    /// Opens a file handed back by any of the routes that produce one — a
    /// picker, a drop from the file explorer, a path named on the command
    /// line, or a file macOS hands the program through an activation — so the
    /// extension decides which kind it is exactly as it does for the picker.
    /// </summary>
    private async Task OpenFileAsync(IStorageFile file)
    {
        if (PluginPackage.Named(file.Name)) await pluginInstalls.InstallAsync(file);
        else await files.OpenFileAsync(file);
    }

    /// <summary>
    /// Opens a file already sitting on disk rather than one a picker handed
    /// back — named on the command line when the program started, or resolved
    /// from a plain path some other way.
    /// </summary>
    private async Task OpenPathAsync(string path)
    {
        IStorageFile? file;

        try
        {
            file = await StorageProvider.TryGetFileFromPathAsync(path);
        }
        catch (Exception ex)
        {
            Report($"Could not open {Path.GetFileName(path)}: {ex.Message}");
            return;
        }

        if (file is null)
        {
            Report($"Could not open {Path.GetFileName(path)}.");
            return;
        }

        await OpenFileAsync(file);
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
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File) && !this.HasDialogUp
                ? DragDropEffects.Copy
                : DragDropEffects.None);

        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            // Only the first: one window holds one patch, and a picker never
            // offers more than that either.
            if (e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().FirstOrDefault() is not { } file) return;

            e.Handled = true;

            await OpenActivatedFileAsync(file);
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
    internal async Task OpenActivatedFileAsync(IStorageFile file)
    {
        if (this.HasDialogUp)
        {
            Report($"{file.Name} was not opened: there is a dialog to answer first.");
            return;
        }

        if (PluginPackage.Named(file.Name) || await unsaved.MayReplaceThePatchAsync()) await OpenFileAsync(file);
    }

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

    /// <summary>Called once, before anything compiles.</summary>
    private void WirePlayback()
    {
        playback.Compiled += (_, _) =>
        {
            ShowPreview(playback.HasPicture);
            knobs.Refresh();
            Recording.Mark();
        };

        playback.TransportChanged += (_, _) => SyncTransport();

        // A patch saved somewhere new reads what it names from there.
        files.Moved += (_, _) => playback.Recompile();

        // The patch is playing, which is the moment what is in it is worth
        // counting (ADR-0094). Not at a compile: a patch is recompiled on every
        // knob frame, and what it is made of is only interesting where somebody
        // is listening to it.
        playback.Started += (_, _) => usage.Played(
            editor.History.Patch.Nodes.Select(node => node.TypeId),
            editor.History.Patch.Connections.Count,
            presets.Showing?.Name);
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
        keeper?.Stop();

        audio.Dispose();
        compiler.Dispose();

        // And the instruments, which are hardware somebody else may want back. A
        // port left open outlives the window that was reading it.
        midi.Dispose();

        base.OnClosed(e);
    }

    #endregion

    #region Output settings

    // What the window does with the settings OutputSections shows: puts
    // them in force, and writes them out.

    /// <summary>Where the output settings are kept, or null to keep them nowhere.</summary>
    private readonly string? outputSettingsPath;

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
            // The picture's program is only worth compiling while the processor is
            // the one drawing it; the shader has code of its own.
            if (preview.Backend == PreviewBackend.Cpu) compiler.Submit(preview.Program, IlLane.Picture);

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
        outputSections.Show(outputSections.Saved);
        UseOutputSettings(outputSections.Saved);
    }

    /// <summary>
    /// Hands <paramref name="settings"/> to the preview and the sound. The only way
    /// anything in the Graphics section reaches either.
    /// </summary>
    private void UseOutputSettings(OutputSettings settings)
    {
        var size = OutputSections.SizeOf(settings);

        preview.Resolution = size;
        preview.Use(settings.Gpu ? PreviewBackend.Gpu : PreviewBackend.Cpu);
        preview.FrameRate = settings.PreviewFrameRate;

        // What a live Scan reaches with Coordinates' aspect (ADR-0077) — kept in
        // step with the preview rather than fixed, now that the size list is not
        // all one shape.
        audio.Aspect = SynthRenderer.AspectOf(size.Width, size.Height);

        knobs.Hub.Takeover = settings.Takeover;
    }

    /// <summary>
    /// Takes what the sections hold as the settings, puts them in force, and writes
    /// them out when there is somewhere to. A failure to write is said, not thrown:
    /// they are in force for this run regardless.
    /// </summary>
    private void SaveOutputSettings()
    {
        var before = outputSections.Saved;
        var saved = outputSections.Saved = outputSections.Read(before);

        UseOutputSettings(saved);

        if (saved.LatencyMilliseconds != before.LatencyMilliseconds || outputSections.SoundChanged(before, saved))
            playback.ReopenAudio(saved);

        if (outputSettingsPath is null) return;

        try
        {
            saved.Save(outputSettingsPath);
        }
        catch (Exception ex)
        {
            Report($"Could not save the output settings: {ex.Message}", outputSettingsPath);
        }
    }

    #endregion

    #region The right-hand column

    // The column on the right: the preview over the Inspector.

    /// <summary>
    /// The preview, the splitter under it and the inspector, down one column of
    /// <paramref name="grid"/>, whose three rows are theirs.
    /// </summary>
    private void BuildRightPanel(Grid grid, int column)
    {
        previewBox = new Border
        {
            Background = Brushes.Black,
            Child = preview,
        };

        // Double-click the picture and it takes the window; double-click it or
        // press Escape to put everything back. The gesture every video player
        // already has, on the one control here that is a video.
        previewBox.DoubleTapped += (_, e) =>
        {
            ToggleFullScreenPreview();
            e.Handled = true;
        };

        Grid.SetColumn(previewBox, column);
        Grid.SetRow(previewBox, 0);

        previewRow = grid.RowDefinitions[0];

        var splitter = previewSplitter = new GridSplitter { Background = Brushes.Transparent, Height = 5 };
        Grid.SetColumn(splitter, column);
        Grid.SetRow(splitter, 1);

        // The plate is docked rather than scrolled: what a block is and the buttons
        // that act on it are wanted wherever the reading has been scrolled to.
        var reading = new DockPanel();

        var plateHost = inspector.PlateHost;
        var wash = inspector.Wash;

        DockPanel.SetDock(plateHost, Dock.Top);

        reading.Children.Add(plateHost);
        reading.Children.Add(new ScrollViewer
        {
            Content = inspector.Panel,

            // Explicitly transparent: a theme that gave the scroll viewer a
            // background would paint straight over the wash and the mark.
            Background = Brushes.Transparent,
        });

        // The block's face sits behind the inspector rather than beside it, and
        // never takes a click.
        var inspectorBorder = inspectorBox = new Border
        {
            Background = new SolidColorBrush(Colors.Panel),
            Child = new Panel { Children = { wash, reading } },
        };

        // The mark starts under the plate, whatever height the name and the buttons
        // have left it at.
        // The band and the mark are drawn on the wash, so it is told how deep the
        // name's row is and how far down the plate reaches.
        plateHost.PropertyChanged += (_, e) =>
        {
            if (e.Property != BoundsProperty) return;

            wash.Below = plateHost.Bounds.Height;
            wash.BandHeight = (plateHost.Content as ModulePlate)?.Band ?? 0;
        };
        Grid.SetColumn(inspectorBorder, column);
        Grid.SetRow(inspectorBorder, 2);

        // Over the preview's own cell while it has the window, and nowhere otherwise.
        var overlay = transportOverlay = new TransportOverlay() { IsVisible = false };

        overlay.PauseClicked += TogglePause;
        overlay.MuteClicked += playback.ToggleMute;
        overlay.RewindClicked += RewindToZero;

        grid.Children.Add(previewBox);
        grid.Children.Add(knobs.Stage);
        grid.Children.Add(overlay);
        grid.Children.Add(splitter);
        grid.Children.Add(inspectorBorder);
    }

    #endregion

    #region The knob panel's row

    // Where the PanelKnobs stand: the row under the canvas, the edge
    // above it, and the toolbar button that shows it.

    /// <summary>The edge above the panel, dragged to give it more rows or fewer.</summary>
    private readonly GridSplitter controlsSplitter = new()
    {
        Name = "controls-splitter",
        Background = Brushes.Transparent,
        Height = 5,
        IsVisible = false,
    };

    /// <summary>The row the panel stands in, under the canvas or, swapped, under the preview.</summary>
    private RowDefinition? ControlsRow => knobs.View.Parent is Grid grid ? grid.RowDefinitions[2] : null;

    /// <summary>
    /// The panel's height, kept while it is hidden. One row of knobs to start with;
    /// more rows wrap in beneath once it is dragged taller.
    /// </summary>
    private GridLength controlsShare = new(118);

    private void WireControls()
    {
        toolbar.Knobs.IsCheckedChanged += (_, _) => ShowControls(toolbar.Knobs.IsChecked == true);

        knobs.Wanted += (_, _) => ShowControls(true);

        // A socket's own knob on the canvas: heard as it turns, written into the
        // text and the panel when the hand comes off it.
        editor.Dial.InputTurned += (_, pick) => document.Turned(pick.Node, pick.Port);
        editor.Dial.InputLetGo += (_, pick) =>
        {
            document.HandCameOff();
            if (editor.Selection.Focused?.Id == pick.Node || editor.Selection.Group?.Members.Contains(pick.Node) == true) inspector.Build();
        };
    }

    /// <summary>Shows or hides the panel, keeping the toolbar button in step.</summary>
    private void ShowControls(bool shown)
    {
        // The full screen preview owns every row, the knobs' too while swapped.
        if (previewIsFullScreen) return;

        var panel = knobs.View;

        // Only on a change: showing a panel already shown would put back the height
        // it had when last hidden, over whatever it has been dragged to since.
        if (ControlsRow is { } controlsRow && shown != panel.IsVisible)
        {
            // A pixel row rather than an auto one, so the splitter has a height to
            // change; zeroed while hidden, with its minimum, the way the assistant's is.
            if (!shown && panel.IsVisible) controlsShare = controlsRow.Height;

            controlsRow.MinHeight = shown ? 60d : 0d;
            controlsRow.Height = shown ? controlsShare : new GridLength(0);
        }

        panel.IsVisible = shown;
        controlsSplitter.IsVisible = shown;

        if (toolbar.Knobs.IsChecked != shown) toolbar.Knobs.IsChecked = shown;

        if (!shown) knobs.Link(null);
    }

    #endregion

    #region Full screen

    // The preview taking the whole window, or a whole monitor of its own, and giving it back.
    // On the window's own monitor nothing is reparented: the preview stays where it is and
    // the shell around it is put away instead. The GPU surface is an OpenGlControlBase,
    // and moving one between parents tears its context down and builds it again — a picture
    // that blinked every time somebody wanted a closer look. On another monitor the move is
    // the point, so the preview goes to a window there and the renderer is built again once
    // each way (ADR-0129).

    /// <summary>
    /// What each track was set to before the preview took over, in order.
    /// </summary>
    /// <remarks>
    /// The sizes are copied out and restored, rather than swapping in a new grid
    /// definition. That keeps the dragged layout when the preview is toggled.
    /// </remarks>
    private (GridLength Size, double Minimum)[]? columnsBefore;
    private (GridLength Size, double Minimum)[]? rowsBefore;

    /// <summary>
    /// Whether the window was maximised, or merely open, before it went full
    /// screen — the state Escape has to put back, which is not always Normal.
    /// </summary>
    private WindowState stateBefore;

    /// <summary>Which of the grid's children were showing before the preview took over.</summary>
    private Dictionary<Control, bool>? visibleBefore;

    /// <summary>Whether the preview currently has the window.</summary>
    private bool previewIsFullScreen;

    /// <summary>
    /// A track of no width at all, for the columns and rows the preview is not in.
    /// </summary>
    /// <remarks>
    /// Hiding a child is not enough: a grid track holds the width it was given whether
    /// or not anything visible stands in it. Zeroed rather than removed, because Grid
    /// indexes its definitions directly — a child left pointing at column four of a
    /// grid that now has one throws out of <c>MeasureOverride</c>.
    /// </remarks>
    private static GridLength None => new(0, GridUnitType.Pixel);

    private static GridLength Everything => new(1, GridUnitType.Star);

    /// <summary>The window holding the preview on another monitor, while it is there.</summary>
    private PictureWindow? pictureWindow;

    /// <summary>Goes full screen on the monitor the Graphics section names, or comes back.</summary>
    private void ToggleFullScreenPreview()
    {
        if (previewIsFullScreen || pictureWindow is not null)
        {
            LeaveFullScreen();
            return;
        }

        if (MonitorPlacement.FullScreenTarget(this, outputSections.Saved.FullScreen, outputSections.Saved.FullScreenMonitor) is { } screen)
            ShowPictureOn(screen);
        else
            ShowFullScreenPreview(true);
    }

    private void LeaveFullScreen()
    {
        pictureWindow?.Close();
        ShowFullScreenPreview(false);
    }

    /// <summary>
    /// Moves the preview to a full-screen window on <paramref name="screen"/>, leaving
    /// the editor as it is with a note where the picture was.
    /// </summary>
    internal void ShowPictureOn(Screen screen)
    {
        if (previewBox is null || pictureWindow is not null || previewIsFullScreen) return;

        usage.Count(Used.FullScreen);

        previewBox.Child = new TextBlock
        {
            Name = "pictureAway",
            Text = $"The picture is on {screen.DisplayName ?? "another monitor"}. Double-click here or press Esc to bring it back.",
            FontSize = Text.Small,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16),
        };

        preview.Renew();

        var window = pictureWindow = new PictureWindow(screen, preview);

        knobs.Away = window.Knobs;
        window.Knobs.Show(editor.History.Patch);
        window.Knobs.Turning += knobs.Turn;
        window.Knobs.TurnEnded += document.LetGoOfKnob;

        window.Transport.PauseClicked += TogglePause;
        window.Transport.MuteClicked += playback.ToggleMute;
        window.Transport.RewindClicked += RewindToZero;

        window.PauseRequested += (_, _) => TogglePause();
        window.Closed += (_, _) => BringPictureBack(window);

        window.Show(this);
        SyncTransport();
        knobs.SyncStages();
    }

    private void BringPictureBack(PictureWindow window)
    {
        if (pictureWindow != window || previewBox is null) return;

        pictureWindow = null;
        knobs.Away = null;

        preview.Renew();
        previewBox.Child = preview;
    }

    /// <summary>Hands the window to the preview, or takes it back.</summary>
    private void ShowFullScreenPreview(bool full)
    {
        if (full == previewIsFullScreen) return;

        // All four arrive together when the layout is built, so this is one
        // question rather than four. Before that there is nothing to show.
        if (columns is null || previewBox is null) return;

        previewIsFullScreen = full;
        knobs.OverPicture = full;

        if (full) usage.Count(Used.FullScreen);

        if (full) Collapse();
        else Restore();

        toolbar.View.IsVisible = !full;
        statusBar.View.IsVisible = !full;

        // Remembered, since the assistant and the knobs stand in this grid and
        // are as often hidden as not.
        if (full) visibleBefore = columns.Children.ToDictionary(child => child, child => child.IsVisible);

        foreach (var child in columns.Children)
            child.IsVisible = full ? child == previewBox : visibleBefore?.GetValueOrDefault(child, true) ?? true;

        // The controls stand over whichever cell the preview is in.
        if (transportOverlay is { } overlay)
        {
            if (full)
            {
                Over(overlay);
                SyncTransport();
            }

            overlay.IsVisible = full;
        }

        Over(knobs.Stage);
        knobs.SyncStages();

        // ShowPreview stands aside while the preview has the window, and the patch
        // may have lost its picture meanwhile. Only ever put away here: the row has
        // just been given back the height it was dragged to.
        if (!full && !playback.HasPicture) ShowPreview(false);

        void Over(Control control)
        {
            if (!full) return;

            Grid.SetColumn(control, Grid.GetColumn(previewBox));
            Grid.SetRow(control, Grid.GetRow(previewBox));
            Grid.SetRowSpan(control, Grid.GetRowSpan(previewBox));
        }

        void Collapse()
        {
            stateBefore = WindowState;

            columnsBefore = [.. columns.ColumnDefinitions.Select(c => (c.Width, c.MinWidth))];
            rowsBefore = [.. columns.RowDefinitions.Select(r => (r.Height, r.MinHeight))];

            // Which track to leave standing is read off the layout rather than
            // written down here, since the preview is in the wide column while it
            // is swapped with the canvas and in the narrow one otherwise.
            var keepColumn = Grid.GetColumn(previewBox);
            var keepRow = Grid.GetRow(previewBox);

            for (var i = 0; i < columns.ColumnDefinitions.Count; i++)
            {
                var column = columns.ColumnDefinitions[i];

                // The minimum first: it outranks a width of nothing, and a column
                // zeroed while it still had one would hold that much of the shell
                // open across the picture.
                column.MinWidth = 0;
                column.Width = i == keepColumn ? Everything : None;
            }

            for (var i = 0; i < columns.RowDefinitions.Count; i++)
            {
                var row = columns.RowDefinitions[i];

                row.MinHeight = 0;
                row.Height = i == keepRow ? Everything : None;
            }

            WindowState = WindowState.FullScreen;
        }

        void Restore()
        {
            if (columnsBefore is { } savedColumns)
            {
                for (var i = 0; i < savedColumns.Length && i < columns.ColumnDefinitions.Count; i++)
                {
                    columns.ColumnDefinitions[i].Width = savedColumns[i].Size;
                    columns.ColumnDefinitions[i].MinWidth = savedColumns[i].Minimum;
                }
            }

            if (rowsBefore is { } savedRows)
            {
                for (var i = 0; i < savedRows.Length && i < columns.RowDefinitions.Count; i++)
                {
                    columns.RowDefinitions[i].Height = savedRows[i].Size;
                    columns.RowDefinitions[i].MinHeight = savedRows[i].Minimum;
                }
            }

            WindowState = stateBefore;
        }
    }

    #endregion

    #region The layout, kept between runs

    // Leaving the window as it was left: size, monitor, panels and views, kept in
    // WindowLayout (ADR-0121).

    /// <summary>Where the layout is kept, or null to keep it nowhere.</summary>
    private readonly string? layoutPath;

    /// <summary>The layout read at startup, then the one last written.</summary>
    private WindowLayout? layout;

    /// <summary>The last client size the window had while it was neither maximized nor full screen.</summary>
    private Size? normalSize;

    /// <summary>Size, state and monitor. Before the window is shown.</summary>
    private void ApplyWindowLayout()
    {
        // Only a drag of the frame: maximizing resizes the window too, and that is
        // not a size to come back to.
        Resized += (_, e) =>
        {
            if (e.Reason == WindowResizeReason.User && WindowState == WindowState.Normal) normalSize = e.ClientSize;
        };

        if (layout is not { } saved) return;

        if (saved.Width > 0 && saved.Height > 0)
        {
            Width = Math.Max(saved.Width, MinWidth);
            Height = Math.Max(saved.Height, MinHeight);
        }

        normalSize = new Size(Width, Height);

        if (saved.Maximized) WindowState = WindowState.Maximized;

        // The platform places the window, so which monitor it chose is only known
        // once it is up.
        Opened += (_, _) => MonitorPlacement.Return(this, saved.Monitor);
    }

    /// <summary>The panels and the views. After the first patch is on the canvas.</summary>
    private void ApplyPanelLayout()
    {
        if (layout is not { } saved || columns is null || previewRow is null || assistantColumn is null) return;

        columns.ColumnDefinitions[WideColumn].Width = new GridLength(saved.CanvasWeight, GridUnitType.Star);
        columns.ColumnDefinitions[SideColumn].Width = new GridLength(saved.SideWeight, GridUnitType.Star);

        previewShare = new GridLength(saved.PreviewWeight, GridUnitType.Star);
        if (previewBox is { IsVisible: true }) previewRow.Height = previewShare;
        columns.RowDefinitions[2].Height = new GridLength(saved.InspectorWeight, GridUnitType.Star);

        // A shown panel takes its width from the column and a hidden one from the
        // share, so both are set and the panel then put where it was.
        assistantShare = new GridLength(saved.AssistantWidth, GridUnitType.Pixel);
        if (assistant is { IsVisible: true }) assistantColumn.Width = assistantShare;
        toolbar.Assistant.IsChecked = saved.AssistantOpen && toolbar.Assistant.IsEnabled;

        controlsShare = new GridLength(saved.ControlsHeight, GridUnitType.Pixel);
        if (ControlsRow is { } controlsRow && knobs.View.IsVisible) controlsRow.Height = controlsShare;
        ShowControls(saved.ControlsOpen);

        // Only while there is a picture to swap in, which is the button's own rule.
        toolbar.Swap.IsChecked = saved.Swapped && toolbar.Swap.IsEnabled;

        if (saved.Code) document.ShowCode(true);
    }

    /// <summary>Writes the layout down. A settings file is not worth a failure to close.</summary>
    private void RememberLayout()
    {
        if (layoutPath is null || columns is null) return;

        try
        {
            layout = CaptureLayout();
            layout.Save(layoutPath);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Could not save the window layout: {ex.Message}");
        }
    }

    private WindowLayout CaptureLayout()
    {
        // Full screen collapses every track, so the ones it put away are the layout.
        var away = previewIsFullScreen;

        double Column(int index) => Weight(away && columnsBefore is not null
            ? columnsBefore[index].Size
            : columns!.ColumnDefinitions[index].Width);

        double Row(int index) => Weight(away && rowsBefore is not null
            ? rowsBefore[index].Size
            : columns!.RowDefinitions[index].Height);

        // The row a panel stands in, whichever grid it is in while swapped. Full
        // screen zeroes the outer grid's rows only.
        double Under(Control? panel, Func<GridLength, double> measure) =>
            panel?.Parent == columns && away && rowsBefore is not null
                ? measure(rowsBefore[2].Size)
                : measure((panel?.Parent as Grid)?.RowDefinitions[2].Height ?? new GridLength(1, GridUnitType.Star));

        var state = away ? stateBefore : WindowState;
        var size = WindowState == WindowState.Normal ? ClientSize : normalSize;

        // A splitter leaves star weights in pixels, far past what the file accepts.
        var (canvas, side) = Share(Column(WideColumn), Column(SideColumn),
            WindowLayout.DefaultCanvasWeight + WindowLayout.DefaultSideWeight);

        var (preview, inspector) = Share(
            previewBox is { IsVisible: true } || away ? Row(0) : Weight(previewShare),
            Under(inspectorBox, Weight),
            WindowLayout.DefaultPreviewWeight + WindowLayout.DefaultInspectorWeight);

        return new WindowLayout
        {
            Maximized = state == WindowState.Maximized,
            Width = size?.Width ?? layout?.Width ?? 0,
            Height = size?.Height ?? layout?.Height ?? 0,
            Monitor = MonitorPlacement.Describe(Screens.ScreenFromWindow(this)) ?? layout?.Monitor,

            CanvasWeight = canvas,
            SideWeight = side,
            PreviewWeight = preview,
            InspectorWeight = inspector,

            AssistantWidth = assistant is { IsVisible: true } ? assistantColumn!.Width.Value : assistantShare.Value,
            AssistantOpen = assistant?.IsVisible == true,

            ControlsHeight = knobs.View.IsVisible ? Under(knobs.View, length => length.Value) : controlsShare.Value,
            ControlsOpen = knobs.View.IsVisible,

            Code = document.ShowingCode,
            Swapped = toolbar.Swap.IsChecked == true,
        };

        static double Weight(GridLength length) => length.IsStar ? length.Value : 1;

        static (double, double) Share(double a, double b, double total) =>
            a + b > 0 ? (a / (a + b) * total, b / (a + b) * total) : (total / 2, total / 2);
    }

    #endregion

    #region Transport

    // Play and pause, and the mute that goes with them, on the toolbar and on the
    // full-screen preview.

    /// <summary>The dots and toolbar over a full-screen preview, or null before the layout is built.</summary>
    private TransportOverlay? transportOverlay;

    private bool pauseShowsPlay;

    internal bool Paused => playback.Paused;

    internal bool Muted => playback.Muted;

    private void TogglePause()
    {
        // A take is paced by the samples it is handed, so pausing under one would stop the file.
        if (Recording.InHand || Recording.Counting) return;

        if (playback.Paused) playback.Resume();
        else playback.Pause();
    }

    /// <summary>Puts the toolbar button and the full-screen overlay in step with the transport.</summary>
    private void SyncTransport()
    {
        // Recompiles call this on every knob frame, so the glyph is only swapped when it changes.
        var paused = playback.Paused;

        if (pauseShowsPlay != paused)
        {
            pauseShowsPlay = paused;
            toolbar.Pause.Content = paused ? Glyphs.Play() : Glyphs.Pause();
        }

        toolbar.Pause.IsEnabled = !Recording.InHand && !Recording.Counting;

        ToolTip.SetTip(toolbar.Pause, paused ? Toolbar.PlayTip : Toolbar.PauseTip);

        foreach (var overlay in Transports)
        {
            overlay.Paused = paused;
            overlay.Muted = playback.Muted;
            overlay.Sounding = playback.Audible;
        }
    }

    /// <summary>Every transport over a picture: the window's own, and the other monitor's while it has one.</summary>
    private IEnumerable<TransportOverlay> Transports =>
        new[] { transportOverlay, pictureWindow?.Transport }.OfType<TransportOverlay>();

    #endregion

    #region Recording

    // The window's side of a take: which button press means what, and where the file
    // goes. The take itself is TakeRecording.

    /// <summary>The take this window is recording, counting in, or about to.</summary>
    internal TakeRecording Recording { get; }

    /// <summary>
    /// What the toolbar button's press means: start a take, call off the count
    /// before one, or end the one running. The same control does all three — a
    /// take has no length, so stopping it is the only way it ever finishes.
    /// </summary>
    private async Task ToggleRecordAsync()
    {
        if (!toolbar.Record.IsEnabled) return;

        if (Recording.Counting)
        {
            Recording.CallOffCount();
            return;
        }

        if (Recording.Running)
        {
            Recording.Stop();
            return;
        }

        await RecordAsync();
    }

    /// <summary>Asks where the take goes, counts it in, and starts it.</summary>
    private async Task RecordAsync()
    {
        var kinds = Recording.Kinds();

        if (kinds.Count == 0)
        {
            Report(TakeRecording.NothingToRecord);
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Record",
            FileTypeChoices = kinds,
            SuggestedFileName = Takes.FileNameFor(files.Name),
            DefaultExtension = kinds[0].Patterns?[0].TrimStart('*', '.'),
        });

        if (file?.TryGetLocalPath() is not { } path) return;

        // A take is of a patch that is playing, and a paused one has no sound to record.
        playback.Resume();

        // Before the count rather than only as the file is opened: a count-in is
        // three seconds of standing ready, and spending them to be told there is
        // no ffmpeg is three seconds nobody gets back.
        if (Recording.Refusal(path) is { } refused)
        {
            Report(refused);
            return;
        }

        await Recording.CountInAsync(path, TakeRecording.CountInStep);
    }

    #endregion

    #region Recovery

    // What a crash would lose, and putting it back at the next start — ADR-0103.
    // What is handed to WorkKeeper is the document as the unsaved question
    // sees it — the patch, the text where the text is the document, what a bundle carried
    // and the conversation — so that what comes back is what that question would have
    // offered to save.

    /// <summary>Unsaved work kept against a crash, or null where none is kept — which is every test.</summary>
    private readonly WorkKeeper? keeper;

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

        files.Became(
            work.Name,
            work.Beside,
            work.Files is { } held ? new BundleFiles(held, files.SoundFolder, files.PictureFolder) : null);

        playback.Show(loaded.Patch);

        if (work.Source is { } text)
        {
            // Written nowhere, so all of it is unsaved text.
            document.TakeSource(text, saved: false);
        }
        else
        {
            document.DropSource();
        }

        assistant?.Open(work.Conversation);
        editor.History.MarkUnsaved();

        // Kept at once, since the orphan it came from is about to go.
        keeper?.Keep(later: false);

        Report($"Restored {work.Name ?? "the patch"} after a crash. It has not been saved.");

        return true;
    }

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
