using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.App.Audio;
using Flyback.App.Controls;
using Flyback.App.Files;
using Flyback.App.Midi;
using Flyback.App.Statistics;
using Flyback.App.Updates;
using Flyback.Core.Compile;
using Flyback.Core.Render;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App;

public sealed partial class MainWindow : Window
{
    /// <summary>Takes the picture and the sound back to zero seconds.</summary>
    private void RewindToZero() => playback.Rewind();

    /// <summary>The Graphics, Recording and Sound sections, and what they were last saved as.</summary>
    private readonly OutputSections outputSections;

    private readonly CanvasSection canvasSection;

    private readonly UpdatesSection updatesSection;

    private readonly UsageSection usageSection;

    private readonly FilesSection filesSection;

    private readonly NodeEditor editor = new();

    private readonly SourceView source = new();

    /// <summary>Which of the canvas and the text owns the patch.</summary>
    private readonly Document document;

    /// <summary>Which file the patch is, and opening and saving it.</summary>
    private readonly PatchFiles files;

    private readonly PreviewHost preview = new();

    /// <summary>
    /// The pieces of the shell the fullscreen preview puts away and has to bring
    /// back. Nullable only because the layout is built after the fields are, and
    /// never null once <see cref="BuildLayout"/> has run.
    /// </summary>
    private Grid? columns;
    private Border? previewBox;

    /// <summary>
    /// The presets somebody saved, or null for a window that keeps none — every
    /// test that did not ask for a folder, which must not see the ones on the
    /// machine running it.
    /// </summary>
    private readonly PresetLibrary? savedPresets;

    /// <summary>Trying a preset from the gallery by resting the pointer on its tile.</summary>
    private readonly PresetAudition audition;

    /// <summary>The preset slot on the toolbar, and the gallery it opens.</summary>
    private readonly PresetSlot presets;

    /// <summary>The frames the preset gallery's tiles are drawn with, and the ones it has already drawn.</summary>
    private readonly PresetThumbnails thumbnails;

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
    private readonly ReportLine report = new();

    private AssistantPanel? assistant;
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
    private readonly PluginCatalog plugins = Startup.Plugins;

    private readonly AudioEngine audio;

    /// <summary>The patch compiled and played, paused or muted.</summary>
    private readonly Playback playback;

    /// <summary>
    /// What runs the processor's programs as machine code once they are built —
    /// the sound always, and the picture while the processor is drawing it. See
    /// ADR-0076.
    /// </summary>
    private readonly IlCompiler compiler = new();

    /// <summary>
    /// Everything that plays the patch from outside it. The mirror of
    /// <see cref="audio"/>, which takes what the patch makes to a device. Assigned
    /// in the constructor because it is handed the MIDI backend the plugins
    /// offered, which is declared below it.
    /// </summary>
    private readonly MidiHub midi;

    /// <param name="groupFolder">
    /// Where the kept groups live. Null is the usual place; a path is for the
    /// tests, which must not write into the folder a person's own groups are in.
    /// </param>
    /// <param name="presetFolder">
    /// Where the presets somebody saved live. Null keeps none and offers no way to
    /// save one — unlike <paramref name="groupFolder"/>, for the reason
    /// <paramref name="outputSettingsPath"/> gives. The program itself passes
    /// <see cref="PresetLibrary.DefaultFolder"/>.
    /// </param>
    /// <param name="thumbnailFolder">
    /// Where the gallery's thumbnails are kept between runs. Null draws them afresh
    /// each run. The program itself passes <see cref="ThumbnailStore.DefaultFolder"/>.
    /// </param>
    /// <param name="openPath">
    /// A file to open once there is a window for it, or null for the usual
    /// start on the default preset — see <see cref="Startup.OpenPath"/>.
    /// </param>
    /// <param name="outputSettingsPath">
    /// Where the Graphics, Recording and Sound settings are read from and saved to.
    /// Null reads nothing and keeps nothing — unlike <paramref name="groupFolder"/>
    /// — so that the many tests that build a window with no arguments start on the defaults
    /// rather than on whatever the machine running them last saved. The program
    /// itself passes <see cref="OutputSettings.File"/>.
    /// </param>
    /// <param name="interpreted">
    /// Keep the CPU's programs on the interpreter for the whole run — see
    /// <see cref="Startup.Interpreted"/>.
    /// </param>
    /// <param name="updateSettingsPath">
    /// Where the Updates section is read from and saved to, null keeping it nowhere
    /// for the reason <paramref name="outputSettingsPath"/> does.
    /// </param>
    /// <param name="updateNote">
    /// What the last update did, said once on the status bar — see
    /// <see cref="Startup.UpdateNote"/>.
    /// </param>
    /// <param name="whatsNew">
    /// What the release just installed changed, shown once in a dialog when the
    /// window opens in place of <paramref name="updateNote"/> — see
    /// <see cref="Startup.WhatsNew"/>.
    /// </param>
    /// <param name="usageSettingsPath">
    /// Where the Usage section is read from and saved to, null keeping it nowhere
    /// for the reason <paramref name="outputSettingsPath"/> does.
    /// </param>
    /// <param name="canvasSettingsPath">
    /// Where the Canvas section is read from and saved to, null keeping it nowhere
    /// for the reason <paramref name="outputSettingsPath"/> does.
    /// </param>
    /// <param name="fileTypeSettingsPath">
    /// Where the Files section is read from and saved to, null keeping it nowhere
    /// for the reason <paramref name="outputSettingsPath"/> does.
    /// </param>
    /// <param name="fileTypes">
    /// What the Files section tells the operating system. Null tells it nothing,
    /// which is what every test gets.
    /// </param>
    /// <param name="usage">
    /// What this run says about itself (ADR-0094). Null says nothing, which is what
    /// every test gets: none of them has any business reaching a network.
    /// </param>
    /// <param name="recoveryFolder">
    /// Where unsaved work is kept against a crash, and where what a crash left is
    /// looked for (ADR-0103). Null keeps nothing and offers nothing, for the reason
    /// <paramref name="outputSettingsPath"/> reads nothing.
    /// </param>
    /// <param name="pluginFolder">
    /// Where a plugin package opened in the window is installed. Null installs
    /// nothing, for the reason <paramref name="outputSettingsPath"/> reads nothing.
    /// </param>
    /// <param name="relaunch">
    /// Starts Flyback again once this window has closed, which is what loads a plugin
    /// just installed. Null offers no restart, which is what every test gets unless it
    /// is watching for one.
    /// </param>
    /// <param name="presetSite">
    /// The site the gallery lists shared presets from and the plugins window shared
    /// plugins. Null lists none, so no test reaches the network unless it asks to.
    /// </param>
    public MainWindow(
        string? groupFolder = null,
        string? openPath = null,
        string? openShared = null,
        string? outputSettingsPath = null,
        bool interpreted = false,
        string? updateSettingsPath = null,
        string? updateNote = null,
        string? usageSettingsPath = null,
        Usage? usage = null,
        ReleaseNotes? whatsNew = null,
        string? recoveryFolder = null,
        string? presetFolder = null,
        string? thumbnailFolder = null,
        string? canvasSettingsPath = null,
        string? layoutPath = null,
        string? fileTypeSettingsPath = null,
        FileTypes? fileTypes = null,
        string? pluginFolder = null,
        Action<Reopen?>? relaunch = null,
        Uri? presetSite = null)
    {
        this.pluginFolder = pluginFolder;
        this.relaunch = relaunch;
        this.presetSite = presetSite;
        this.openShared = openShared;

        // Before the layout, because the toolbar lists what is saved.
        if (presetFolder is not null) savedPresets = new PresetLibrary(presetFolder);

        thumbnails = new PresetThumbnails(Startup.Plugins.Modules, compiler, thumbnailFolder) { Saved = savedPresets };
        this.outputSettingsPath = outputSettingsPath;
        this.usage = usage ?? Usage.Off;

        document = new Document(editor, source, report, this.usage);

        var shell = new Shell(this, editor, document, plugins, report, this.usage, () => assistant);

        outputSections = new OutputSections(shell, OrderedPresets, PickStartupPatchAsync);

        // Before anything is compiled, so no build is started only to be taken off.
        compiler.Enabled = !interpreted;

        if (outputSettingsPath is not null) outputSettings = OutputSettings.Load(outputSettingsPath);

        this.layoutPath = layoutPath;
        if (layoutPath is not null) layout = WindowLayout.Load(layoutPath);

        updatesSection = new UpdatesSection(updateSettingsPath, (message, detail) => Report(message, detail));
        usageSection = new UsageSection(usageSettingsPath, this.usage, (message, detail) => Report(message, detail));
        canvasSection = new CanvasSection(canvasSettingsPath, editor, (message, detail) => Report(message, detail));
        filesSection = new FilesSection(fileTypeSettingsPath, fileTypes, (message, detail) => Report(message, detail));

        var sound = Sound.Open(plugins, outputSettings);

        // Here rather than at the launch, because what a run started as includes
        // which backend actually opened, and that is only known once one has been
        // asked for.
        this.usage.Started(plugins.Plugins.Select(plugin => plugin.Info.Id), sound.Output?.Id, ScreenHeights());
        audio = new AudioEngine(sound.Device) { Compiler = compiler };

        // Nothing is opened by this. The backend is asked what is plugged in
        // when a picker is drawn, and asked for a device only once a compiled
        // program is actually reading one — see MidiHub.Listen.
        midi = new MidiHub(plugins.PreferredMidiInput);

        knobs = new PanelKnobs(shell, preview, audio, midi);

        palette = new Palette(shell, knobs.View.Instruments, () => outputSettings.Keyboard, groupFolder);

        files = new PatchFiles(shell, Show, OfferMissingPluginsAsync);

        // Where the last document's knobs were left says nothing about this one's.
        files.Arrived += (_, _) => knobs.Hub.Forget();
        files.Saved += (_, _) => ClearPresetSelection();

        inspector = new Inspector(shell, midi, knobs.Instruments, files.SoundFolder, files.PictureFolder, () => palette.Groups, palette.SaveGroup);

        playback = new Playback(
            shell,
            preview,
            audio,
            compiler,
            midi,
            sound,
            () => files.Sounds,
            () => files.Pictures,
            // The take is made next, and needs the playback to make it.
            () => Recording is { Running: true });

        pluginInstalls = new PluginInstalls(
            shell,
            pluginFolder,
            presetSite,
            () => SiteHttp ?? SiteClient.Value,
            () => playback.Sound,
            Assisting,
            relaunch is null ? null : RestartAsync);

        audition = new PresetAudition(
            audio,
            compiler,
            plugins.Modules,
            savedPresets,
            // A take records what the speakers play, and a preset tried on the
            // way past is not part of it.
            () => playback.CanSound && Recording is { Running: false },
            playback.SyncAudioToVolume);

        presets = new PresetSlot(
            shell,
            files,
            thumbnails,
            audition,
            savedPresets,
            PresetSite,
            MayReplaceThePatchAsync,
            Show,
            OfferMissingPluginsAsync);

        toolbar = new Toolbar(presets.View, plugins.Assistants.Count > 0);

        statusBar = new StatusBar(shell, preview, WriteToTheAuthorAsync);

        // Before anything recompiles, because a recompile asks the take what the
        // record button should say and whether the device may be stopped.
        Recording = new TakeRecording(
            toolbar.Record,
            outputSections.Resolution,
            preview,
            audio,
            this.usage,
            () => editor.Patch,
            () => outputSettings,
            (message, progress) => Report(message, progress: progress),
            SyncTransport,
            RewindToZero,
            playback.SyncAudioToVolume);


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
        midi.Heard += () => this.usage.Count(Used.Instrument);

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

        editor.PatchChanged += (_, _) =>
        {
            playback.Recompile(opened: editor.Opening);
            if (editor.Opening) statusBar.Compiling.Watch(() => playback.Starting);

            // Patching an input takes its knob away and unpatching gives it
            // back, and neither is a selection change — so the panel is asked
            // here as well, and answers only when a wire actually moved.
            inspector.Sync();
        };

        editor.SelectionChanged += (_, _) =>
        {
            inspector.Build();
            playback.ProbeSelectionChanged();
        };
        editor.HistoryChanged += (_, _) => RefreshEditState();

        // Asked again rather than simply put away: the wire may have been dropped
        // back onto 'color', and then there is nothing to put back.
        editor.GestureFinished += (_, _) =>
        {
            if (previewHideWaiting) ShowPreview(playback.HasPicture);
        };
        editor.Reported += (_, message) => Report(message);

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
        presets.StartOn(outputSettings.DefaultPreset);

        // No manual switch any more — Volume is the one now, and the Recompile
        // that patch assignment just ran already brought sound up to match its
        // default (ADR-0079). What is left to say only where turning it up would
        // not help: nothing was there to open it with.
        if (playback.Sound.Output is null)
            Report("No sound backend is installed, so Volume will do nothing. "
                + "See About for where plugins are looked for.");

        // Said once, because nothing else on screen shows it, and a run that is
        // slower for a reason should say which.
        if (interpreted)
            Report($"Running interpreted ({Startup.InterpretedFlag}): the CPU's programs are not compiled this run.");

        // Last, so it is what the bar is showing when the window first appears.
        if (whatsNew is null && updateNote is not null) Report(updateNote);

        ApplyPanelLayout();

        if (recoveryFolder is not null) keeper = new WorkKeeper(recoveryFolder, Work);

        // Opened rather than called straight away: there is nothing to put a
        // dialog over before, and the platform window behind this one — and the
        // storage provider that comes with it — is not guaranteed to exist until
        // then, which OpenPathAsync cannot wait for a click to find out. One
        // handler, so the three come one after another: what changed, then what
        // a crash left, then the file this launch was for, which asks about
        // unsaved work like any other and so about work just restored.
        Opened += async (_, _) =>
        {
            if (whatsNew is not null) await this.ShowDialog(WhatsNew.Title(whatsNew), WhatsNew.View(whatsNew));

            keeper?.Restore(Recover);

            // A plugin package replaces nothing, so it asks about nothing unsaved.
            if (openPath is { } path && (PluginPackage.Named(path) || await MayReplaceThePatchAsync()))
                await OpenPathAsync(path);

            if (openShared is { Length: > 0 } id) await presets.OpenSharedAgainAsync(id);
        };
    }

    private Control BuildLayout()
    {
        var root = new DockPanel();

        // Before the bars, because both of them ask it what it is called.
        assistant = new AssistantPanel(
            plugins,
            () => editor.Patch,
            // An edit rather than a new document, so it undoes like every other
            // edit and there is nothing to ask about first: what it replaced is
            // one press of Ctrl+Z away rather than gone.
            patch =>
            {
                document.TakeFromAssistant(patch);
                preview.Rewind();
            },
            // Wrapped rather than handed over as it stands, because the third
            // thing Report takes is about how a line ages in the log and the
            // panel has no business knowing there is one.
            (message, detail) => Report(message, detail),
            samples: files.Sounds,
            pictures: files.Pictures,
            asked: usage.Assistant,
            presets: () => OrderedPresets())
        {
            IsVisible = false,
        };

        // A conversation is saved with the patch, so one with a turn nobody has
        // saved is something the title and the close have to know about.
        assistant.ConversationChanged += (_, _) => RefreshEditState();

        // Which modules the assistant is not told about is a question about the
        // catalog and the settings, so it moves only when settings are saved.
        editor.Undescribed = assistant.Undescribed;
        assistant.UndescribedChanged += (_, _) =>
        {
            editor.Undescribed = assistant.Undescribed;
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
        // parent, which would tear its GPU context down (see MainWindow.FullScreen).
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
        previewHideWaiting = !shown && toolbar.Swap.IsChecked == true && editor.Gesturing;
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

    /// <summary>Every preset the toolbar and the "Startup patch" list offer, in <see cref="PresetLibrary.Ordered"/>'s order.</summary>
    private List<PatchPreset> OrderedPresets() => PresetLibrary.Ordered(plugins.Presets, savedPresets);

    /// <summary>What each button on the toolbar does. Called once, as the window is built.</summary>
    private void WireToolbar()
    {
        toolbar.Open.Click += async (_, _) => await OpenAnotherPatchAsync();
        toolbar.Save.Click += async (_, _) => await SavePatchAsync();

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
        outputSections.Show(outputSettings);
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

}
