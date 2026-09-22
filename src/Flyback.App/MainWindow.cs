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
    /// <summary>
    /// Writes a performance, knobs and all, to a file. On the toolbar rather
    /// than the Output's panel — ADR-0080 — so the one control every session
    /// reaches for is never a click away behind a selection. The same button
    /// stops a take; its glyph swaps between the dot and the square rather
    /// than its label, since a toolbar button here carries no text at all. A
    /// deterministic render of a frozen patch is `flyback-cli render`'s job
    /// now — ADR-0078.
    /// </summary>
    private readonly Button recordButton = new();

    /// <summary>
    /// Takes the patch back to zero seconds, in the picture and in the sound.
    /// Beside recordButton rather than on the Output's panel — ADR-0081, the
    /// same move ADR-0080 made for Record.
    /// </summary>
    private readonly Button rewindButton = new();

    /// <summary>Takes the picture and the sound back to zero seconds.</summary>
    private void RewindToZero()
    {
        audio.Rewind();
        preview.Rewind();

        // A paused clock reads the time it froze at, so the next tick would undo the rewind.
        if (!paused) return;

        frozenAt = 0;
        preview.Time = 0;
    }

    /// <summary>What the rewind button does — the same sentence its Output-panel tip used to carry.</summary>
    private const string RewindTip =
        "Take the patch back to zero seconds, in the picture and in the sound.";

    private readonly ComboBox resolution = new Picker
    {
        ItemsSource = Resolutions.All.Select(r => r.Label).ToList(),
        SelectedIndex = Resolutions.Default,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>
    /// The Graphics section of the settings window: size, preview rate and renderer.
    /// Assembled once and lent to the window each time it opens, because these
    /// controls are the live state of the instrument (ADR-0082).
    /// </summary>
    private readonly StackPanel graphicsSection = new() { Spacing = 8, Width = 280 };

    /// <summary>The Recording section: how a take's frames are timed and compressed.</summary>
    private readonly StackPanel recordingSection = new() { Spacing = 8, Width = 280 };

    /// <summary>
    /// The Sound section: whatever the sound backend declares, then how far behind
    /// the patch the speakers may run.
    /// </summary>
    private readonly StackPanel soundSection = new() { Spacing = 8, Width = 280 };

    private readonly CanvasSection canvasSection;

    private readonly UpdatesSection updatesSection;

    private readonly UsageSection usageSection;

    private readonly FilesSection filesSection;

    private readonly ComboBox frameRate = new Picker
    {
        Name = "frameRate",
        ItemsSource = FrameRates.Select(r => $"{r:0.##} fps").ToList(),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private readonly ComboBox previewFrameRate = new Picker
    {
        Name = "previewFrameRate",
        ItemsSource = PreviewFrameRates.Select(r => r <= 0 ? "Unlimited" : $"{r:0.##} fps").ToList(),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>
    /// Which preset the window opens on at the next launch — the Graphics section
    /// (ADR-0093). It reads <see cref="startupPatch"/>, and a click picks another
    /// from the gallery.
    /// </summary>
    private readonly Button defaultPreset = new()
    {
        Name = "defaultPreset",
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>The name on <see cref="defaultPreset"/>.</summary>
    private readonly TextBlock defaultPresetName = new()
    {
        Name = "defaultPresetName",
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>The name <see cref="defaultPreset"/> shows, and what Save writes.</summary>
    private string startupPatch = "";

    private readonly NumericUpDown jpegQuality = new()
    {
        Name = "jpegQuality",
        Minimum = OutputSettings.LowestQuality,
        Maximum = OutputSettings.HighestQuality,
        Increment = 5,
        FormatString = "0",
        FontSize = Text.Body,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>
    /// How long a take is counted in for — the length ADR-0090 fixed at three
    /// seconds and ADR-0091 made a choice.
    /// </summary>
    private readonly ComboBox countIn = new Picker
    {
        Name = "countIn",
        ItemsSource = CountIns.Select(s => s <= 0 ? "None" : $"{s} s").ToList(),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>
    /// Whether a take starts at nought seconds. Its own row rather than another
    /// entry on <see cref="countIn"/>, because standing ready and starting from
    /// the beginning are two different things: a take may want either alone.
    /// </summary>
    private readonly CheckBox rewindBeforeTake = new()
    {
        Name = "rewindBeforeTake",
        Content = "Rewind to zero first",
        FontSize = Text.Body,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly ComboBox latency = new Picker
    {
        Name = "latency",
        ItemsSource = Latencies.Select(ms => $"{ms} ms").ToList(),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>
    /// The sound backend's own settings — which device plays, for one — drawn from
    /// what it declares (ADR-0085). Empty where no backend is installed or it has
    /// nothing to ask.
    /// </summary>
    private readonly SettingsForm soundForm = new() { Name = "soundForm", Beside = true };

    /// <summary>Which backend plays, and which plugin it came from, above the rows it asks for.</summary>
    private readonly TextBlock soundNote = new()
    {
        Name = "soundNote",
        FontSize = Text.Small,
        Foreground = Text.Muted,
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>
    /// What the Graphics, Recording and Sound sections were last saved as, and so
    /// what closing the settings window without Save puts them back to.
    /// </summary>
    private OutputSettings outputSettings = new();

    /// <summary>Where <see cref="outputSettings"/> is kept, or null to keep it nowhere.</summary>
    private readonly string? outputSettingsPath;

    private readonly NodeEditor editor = new();

    /// <summary>
    /// The sound files the patch names, read once each and kept. Owned by the
    /// window because it is the window that knows where the patch was opened
    /// from, and handed to every compile from here.
    /// </summary>
    private readonly SampleLibrary soundFolder = new();

    /// <summary>The pictures a patch shows, cached the way its sounds are.</summary>
    private readonly ImageLibrary pictureFolder = new();

    /// <summary>
    /// The files a bundle carries, while one is open, and null while the document
    /// is a loose patch backed by a folder.
    /// </summary>
    /// <remarks>
    /// Held rather than unpacked, which is what makes a bundle a document here
    /// rather than an archive to spill onto a disk first: nothing is written
    /// anywhere until they save. It costs one copy of the compressed bytes and
    /// costs the undo history nothing — the history is snapshots of the patch, and
    /// the patch is paths (ADR-0052).
    /// </remarks>
    private BundleFiles? carried;

    /// <summary>
    /// Where a sound is looked for: the bundle first while one is open, and the
    /// folder behind it — because a module pointed at a file on this machine
    /// while a bundle is open means the file on this machine.
    /// </summary>
    private ISampleLibrary Sounds => carried ?? (ISampleLibrary)soundFolder;

    /// <inheritdoc cref="Sounds"/>
    private IImageLibrary Pictures => carried ?? (IImageLibrary)pictureFolder;
    private readonly PreviewHost preview = new();

    /// <summary>
    /// The pieces of the shell the fullscreen preview puts away and has to bring
    /// back. Nullable only because the layout is built after the fields are, and
    /// never null once <see cref="BuildLayout"/> has run.
    /// </summary>
    private Grid? columns;
    private Border? previewBox;
    private Control? toolbar;
    private Control? statusBar;

    /// <summary>
    /// The toolbar's preset list, kept so a file opened from elsewhere — see
    /// <see cref="ClearPresetSelection"/> — can take the selection off it. Null
    /// only before <see cref="BuildLayout"/> has run.
    /// </summary>
    private Picker? presetsPicker;

    /// <summary>The frames the preset gallery's tiles are drawn with, and the ones it has already drawn.</summary>
    private readonly PresetThumbnails thumbnails;

    /// <summary>
    /// Which row of <see cref="presetsPicker"/> is on the canvas, or -1 for a
    /// document that did not come from that list. What a refused change puts the
    /// box back to, and what a later pick is compared against so re-choosing the
    /// same preset is a no-op rather than a rebuild.
    /// </summary>
    private int presetShowing;

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

    /// <summary>
    /// Puts the picture in the wide column and the canvas where the picture
    /// was. Enabled only while there is a picture to put there — see
    /// <see cref="ShowPreview"/>.
    /// </summary>
    private readonly ToggleButton swapButton = ToolbarButtons.Toggle("swap", "⇄", NoPictureToSwapTip);

    /// <summary>What the swap button says while it can be pressed.</summary>
    private const string SwapTip =
        "Swap the preview and the canvas, for a bigger picture while you patch.";

    /// <summary>What it says while it cannot.</summary>
    private const string NoPictureToSwapTip =
        "Nothing is wired into the Output's 'color', so there is no picture to swap in.";

    /// <summary>
    /// The module list, shown at the pointer when the canvas is right-clicked
    /// rather than standing open down one side — ADR-0046. Built once and kept,
    /// because it holds which plugins are ticked and that is a setting rather
    /// than something to be re-answered on every opening.
    /// </summary>
    private ModulePalette? palette;

    private readonly Flyout paletteFlyout = new()
    {
        Placement = PlacementMode.Pointer,
        ShowMode = FlyoutShowMode.Standard,
    };

    /// <summary>
    /// Named so a test can find it. It is the one panel here that is switched
    /// off whole — see <see cref="RefreshOwnership"/> — and there is nothing
    /// else about it to tell it apart by.
    /// </summary>
    private readonly StackPanel inspector = new()
    {
        Name = "inspector",
        Margin = new Thickness(PanelInset),
        Spacing = 8,
    };

    /// <summary>
    /// How far the panel's rows keep off its edges. Named because the plate at the
    /// head of it takes the inset back off again to reach them — see BuildInspector.
    /// </summary>
    internal const double PanelInset = 12;

    /// <summary>
    /// The selected block's background, behind everything on the panel and fading
    /// out down it, with the block's mark set large in it.
    /// </summary>
    private readonly ModuleWash wash = new();

    /// <summary>
    /// Where the plate stands: above the scroller rather than in it, so the name and
    /// the buttons are there at every scroll position.
    /// </summary>
    private readonly ContentControl plateHost = new() { Name = "plate-host" };
    private readonly TextBlock status = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = Text.Body,

        // Every line on this bar shares one row of a narrow window, so this one
        // gives way the same way the report beside it does rather than being
        // sheared off at whatever character the edge fell on.
        TextTrimming = TextTrimming.CharacterEllipsis,

        // On the right of the bar, against the edge the report is not on.
        TextAlignment = TextAlignment.Right,
    };

    /// <summary>
    /// The one line anything is said on, and the log of what has been said. See
    /// <see cref="Report"/>, which is the only thing that writes to it.
    /// </summary>
    private readonly ReportLine report = new();

    private readonly ComboBox gpuButton = new Picker
    {
        Name = "render",
        ItemsSource = new[] { "GPU", "CPU" },
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };
    private readonly ToggleButton assistantButton =
        ToolbarButtons.Toggle("assistant", "✦", "Describe a patch and have one built.");

    private readonly Button undoButton = ToolbarButtons.Glyph("undo", "↶", "Take back the last edit  (Ctrl+Z)");
    private readonly Button redoButton = ToolbarButtons.Glyph("redo", "↷", "Put it back  (Ctrl+Shift+Z)");

    /// <summary>
    /// Held because what laying out means, and whether it is worth doing at
    /// all, depends on which view is showing — see <see cref="RefreshOwnership"/>.
    /// </summary>
    private Button? tidyButton;

    /// <summary>What the layout button does to the canvas, which is what it says by default.</summary>
    private const string TidyTip =
        "Lay the modules out so the patch reads left to right  (Ctrl+L). "
        + "Ctrl+click lays out only what is selected, leaving the rest where it is  (Ctrl+Shift+L)";

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

    private AudioSetup sound;
    private readonly AudioEngine audio;

    /// <summary>
    /// Set once a device has refused to start, so a Volume left above nought does
    /// not retry it on every edit. Only a different device clears it — saved in the
    /// Sound settings, or found at the next launch — see ADR-0079 and ADR-0085.
    /// </summary>
    private bool audioBlocked;

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

    /// <summary>
    /// Where the kept groups are read from and written to, or null for the usual
    /// place. Held because <see cref="BuildPalette"/> runs later than the
    /// constructor's argument list does.
    /// </summary>
    private readonly string? groupFolder;

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
        Action? relaunch = null,
        Uri? presetSite = null)
    {
        this.groupFolder = groupFolder;
        this.pluginFolder = pluginFolder;
        this.relaunch = relaunch;
        this.presetSite = presetSite;

        // Before the layout, because the toolbar lists what is saved.
        if (presetFolder is not null) savedPresets = new PresetLibrary(presetFolder);

        thumbnails = new PresetThumbnails(Startup.Plugins.Modules, compiler, thumbnailFolder) { Saved = savedPresets };
        this.outputSettingsPath = outputSettingsPath;
        this.usage = usage ?? Usage.Off;

        // Before anything is compiled, so no build is started only to be taken off.
        compiler.Enabled = !interpreted;

        if (outputSettingsPath is not null) outputSettings = OutputSettings.Load(outputSettingsPath);

        this.layoutPath = layoutPath;
        if (layoutPath is not null) layout = WindowLayout.Load(layoutPath);

        updatesSection = new UpdatesSection(updateSettingsPath, (message, detail) => Report(message, detail));
        usageSection = new UsageSection(usageSettingsPath, this.usage, (message, detail) => Report(message, detail));
        canvasSection = new CanvasSection(canvasSettingsPath, editor, (message, detail) => Report(message, detail));
        filesSection = new FilesSection(fileTypeSettingsPath, fileTypes, (message, detail) => Report(message, detail));

        sound = Sound.Open(plugins, outputSettings);

        // Here rather than at the launch, because what a run started as includes
        // which backend actually opened, and that is only known once one has been
        // asked for.
        this.usage.Started(plugins.Plugins.Select(plugin => plugin.Info.Id), sound.Output?.Id, ScreenHeights());
        audio = new AudioEngine(sound.Device) { Compiler = compiler };

        // Nothing is opened by this. The backend is asked what is plugged in
        // when a picker is drawn, and asked for a device only once a compiled
        // program is actually reading one — see MidiHub.Listen.
        midi = new MidiHub(plugins.PreferredMidiInput);

        // Before anything is compiled and before a panel is drawn, because a
        // MIDI In asks this what there is to listen to as soon as either
        // happens. Installed here rather than in Startup because the list is the
        // window's — the computer's keyboard is only an instrument while there
        // is a window for it to be typed into.
        MidiSources.Install(() => midi.Sources);

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
            Recompile();

            // Patching an input takes its knob away and unpatching gives it
            // back, and neither is a selection change — so the panel is asked
            // here as well, and answers only when a wire actually moved.
            SyncInspector();
        };

        editor.SelectionChanged += (_, _) =>
        {
            BuildInspector();
            ProbeSelectionChanged();
        };
        editor.HistoryChanged += (_, _) => RefreshEditState();

        // Asked again rather than simply put away: the wire may have been dropped
        // back onto 'color', and then there is nothing to put back.
        editor.GestureFinished += (_, _) =>
        {
            if (previewHideWaiting) ShowPreview(HasPicture);
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
        var offered = OrderedPresets();
        var openIndex = PresetLibrary.Opening(offered, outputSettings.DefaultPreset);

        Patch opened;

        try
        {
            opened = Arrive(offered[openIndex]);
        }
        catch (Exception ex)
        {
            // Only a saved preset can fail here — one whose file has gone bad, or
            // that needs a plugin taken away since. The window still opens, on
            // the preset it would have opened on had none been chosen.
            Report($"Could not open the '{offered[openIndex].Name}' preset: {ex.Message}");

            openIndex = PresetLibrary.Opening(offered, "");
            opened = Arrive(offered[openIndex]);
        }

        // Set before the patch, whose first play says which preset it is, and
        // before the picker's own index, so its handler — which rebuilds the
        // patch on a change — sees the row it is already showing and does
        // nothing: the patch below is already built.
        presetShowing = openIndex;

        editor.Patch = opened;
        if (presetsPicker is not null) presetsPicker.SelectedIndex = openIndex;

        // No manual switch any more — Volume is the one now, and the Recompile
        // that patch assignment just ran already brought sound up to match its
        // default (ADR-0079). What is left to say only where turning it up would
        // not help: nothing was there to open it with.
        if (sound.Output is null)
            Report("No sound backend is installed, so Volume will do nothing. "
                + "See About for where plugins are looked for.");

        // Said once, because nothing else on screen shows it, and a run that is
        // slower for a reason should say which.
        if (interpreted)
            Report($"Running interpreted ({Startup.InterpretedFlag}): the CPU's programs are not compiled this run.");

        // Last, so it is what the bar is showing when the window first appears.
        if (whatsNew is null && updateNote is not null) Report(updateNote);

        ApplyPanelLayout();

        var ticker = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        ticker.Tick += (_, _) => UpdateStatus();
        ticker.Start();

        if (recoveryFolder is not null) KeepRecovery(recoveryFolder);

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

            RestoreLeftover();

            // A plugin package replaces nothing, so it asks about nothing unsaved.
            if (openPath is { } path && (PluginPackage.Named(path) || await MayReplaceThePatchAsync()))
                await OpenPathAsync(path);
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
                TakeFromAssistant(patch);
                preview.Rewind();
            },
            // Wrapped rather than handed over as it stands, because the third
            // thing Report takes is about how a line ages in the log and the
            // panel has no business knowing there is one.
            (message, detail) => Report(message, detail),
            samples: Sounds,
            pictures: Pictures,
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
            BuildInspector();
        };

        toolbar = BuildToolbar();
        statusBar = BuildStatusBar();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(statusBar, Dock.Bottom);

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
        Grid.SetRow(controlsPanel, 2);

        patch.Children.Add(editor);
        patch.Children.Add(source);
        patch.Children.Add(controlsSplitter);
        patch.Children.Add(controlsPanel);

        BuildPalette();

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

        root.Children.Add(toolbar);
        root.Children.Add(statusBar);
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
        previewHideWaiting = !shown && swapButton.IsChecked == true && editor.Gesturing;
        if (previewHideWaiting) return;

        // Ahead of the full screen guard, so the button is right by the time the
        // toolbar comes back.
        swapButton.IsEnabled = shown;
        ToolTip.SetTip(swapButton, shown ? SwapTip : NoPictureToSwapTip);

        if (previewIsFullScreen) return;
        if (previewBox is null || previewRow is null || previewSplitter is null) return;

        // A picture that has gone gives the canvas its column back before the row
        // it would stand in is put away. Unticking is what moves it — see the
        // button's handler in BuildToolbar.
        if (!shown) swapButton.IsChecked = false;

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
        Hang(swapped ? controlsPanel : inspectorBox, columns, pictureColumn, 2);
        Hang(swapped ? previewSplitter : controlsSplitter, patchPane, 0, 1);
        Hang(swapped ? inspectorBox : controlsPanel, patchPane, 0, 2);

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

    private Control BuildToolbar()
    {
        offeredPresets = OrderedPresets();

        // Not shown, and never opened: what this holds is which preset is on the
        // canvas, and its selection changing is how a pick from the gallery
        // reaches the code below. A Picker rather than a plain list because it
        // was the dropdown before the gallery, and its refusal to move on a
        // keystroke is still what keeps an arrow at it from discarding the patch.
        var presets = new Picker
        {
            Name = "presets",
            ItemsSource = offeredPresets,
            SelectedIndex = offeredPresets.FindIndex(preset => preset.Kind != PresetKind.Blank),
            Width = 34,
            Height = 30,
            Opacity = 0,
            IsHitTestVisible = false,
            IsTabStop = false,
        };

        presetsPicker = presets;

        // The toolbar button: the same square, glyph-only shape as open, save and
        // tidy. It opens the gallery, and a tile picked there is a row of the
        // picker above chosen, so there is one road to changing the preset and it
        // is the one that asks about unsaved work.
        var presetsButton = ToolbarButtons.Drawn("presets-glyph", Glyphs.Presets(), "Start from a preset patch, or save this one as a preset…");
        presetsButton.Click += async (_, _) =>
        {
            var showing = presets.SelectedItem as PatchPreset;
            var gallery = PresetGallery.Build([.. plugins.Presets.OrderBy(p => p.Kind)], showing, thumbnails, PointedAt, Yours(), PresetSite());
            var chosen = await this.ShowDialog<object?>("Start from a preset", gallery.Tiles, gallery.Filter, fill: true);

            PointedAt(null);

            switch (chosen)
            {
                // Looked up in the list as it is now, which a save in the gallery may have changed.
                case PatchPreset preset:
                    presets.SelectedIndex = offeredPresets.IndexOf(preset);
                    break;

                case SitePreset shared:
                    await OpenSharedPresetAsync(shared);
                    break;
            }
        };

        // Stacked in one cell so the toolbar keeps the one slot it had.
        var presetsSlot = new Grid();
        presetsSlot.Children.Add(presets);
        presetsSlot.Children.Add(presetsButton);

        // Which preset is on the canvas, so a refused change can put the box
        // back where it was. Setting the index raises this same handler, hence
        // the flag around it.
        var restoring = false;

        presets.SelectionChanged += async (_, _) =>
        {
            if (restoring) return;

            // Nothing on no selection, and nothing on a heading either — which
            // no pointer can land on, and so can only have been set from here.
            if (presets.SelectedItem is not PatchPreset preset) return;
            if (presets.SelectedIndex == presetShowing) return;

            var wanted = presets.SelectedIndex;

            if (!await MayReplaceThePatchAsync())
            {
                PutTheBoxBack();
                return;
            }

            try
            {
                // A preset from a plugin is built here, not when it was
                // registered, so this is where a plugin that offered a patch
                // using modules it failed to add finally shows up.
                //
                // Named before it is shown, because showing it is what redraws the
                // title — and named at all because a preset is one of the three ways a
                // patch arrives and the only one with no file to be named after. It has
                // no folder either, and disowns whatever the last document was carrying:
                // a preset naming a sound means the one beside the program, or the one
                // in its own bundle, not the one inside a bundle somebody happened to
                // open first.
                editor.Patch = Arrive(preset);
                RewindToZero();

                // A preset has no file to have saved a conversation with, so it
                // arrives with none — ADR-0072.
                assistant?.Open(null);

                // A preset arrives as a graph and no text describes it, so the
                // canvas owns it — ADR-0068.
                DropSource();

                // Unless it was picked from the text view, where it is read into
                // text there and then: which view somebody picks a preset from
                // says which of the two they mean to work in.
                if (showingCode) ReadIntoText();

                presetShowing = wanted;

                usage.Count(Used.Preset);

                // The question above may have been answered with a save, and a
                // save takes the selection off this list: what was saved is a
                // file, and no preset. The row that was picked is picked again,
                // or the title would name a preset the list does not show.
                PutTheBoxBack();
            }
            catch (Exception ex)
            {
                Report($"Could not build the '{preset.Name}' preset: {ex.Message}");
                PutTheBoxBack();
            }
        };

        var open = ToolbarButtons.Drawn("open", Glyphs.Open(), "Open a patch (CTRL+O)…");
        open.Click += async (_, _) => await OpenAnotherPatchAsync();

        var save = ToolbarButtons.Drawn("save", Glyphs.Save(), "Save this patch (CTRL+S)…");
        save.Click += async (_, _) => await SavePatchAsync();

        // All three go to whichever view is showing — see MainWindow.Source.
        undoButton.Click += (_, _) => Undo();
        redoButton.Click += (_, _) => Redo();

        var tidy = tidyButton = ToolbarButtons.Drawn("tidy", Glyphs.Tidy(), TidyTip);

        // A locked canvas says why in its tip, and that is wasted unless a
        // disabled button is still allowed to show it — see the same call on
        // recordButton.
        ToolTip.SetShowOnDisabled(tidy, true);

        // A Click says which button was pressed and nothing about what was held
        // down while it was, so that is read on the way in. The key offers both
        // layouts and so does the button, because a modifier is how a toolbar
        // offers the narrower of two things without a second glyph for it.
        var modifiers = KeyModifiers.None;

        tidy.AddHandler(
            PointerPressedEvent,
            (object? _, PointerPressedEventArgs e) => modifiers = e.KeyModifiers,
            RoutingStrategies.Tunnel);

        tidy.Click += (_, _) =>
        {
            Tidy((modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0);
            modifiers = KeyModifiers.None;
        };

        // The same, for a patch with no picture to swap in.
        ToolTip.SetShowOnDisabled(swapButton, true);
        swapButton.IsCheckedChanged += (_, _) => SwapPreview(swapButton.IsChecked == true);

        // The glyph and the tip are set here, alongside every other toolbar
        // button; what the tip actually says is decided per patch by
        // MarkRecordable, which runs before this is ever shown.
        ToolbarButtons.Marked(recordButton, "record", Glyphs.Record(), RecordTip);

        ToolbarButtons.Marked(pauseButton, "pause", Glyphs.Pause(), PauseTip);
        pauseButton.Click += (_, _) => TogglePause();

        ToolbarButtons.Marked(rewindButton, "rewind", Glyphs.Rewind(), RewindTip);
        rewindButton.Click += (_, _) => RewindToZero();

        WireSource();
        RefreshEditState();

        // What is done to the patch, in the order it is done: pick one, open or
        // save one, take an edit back. Tidy sits with undo and redo rather than
        // with the files, because it is an edit and is taken back like one.
        var patchwork = ToolbarButtons.Group();

        patchwork.Children.Add(presetsSlot);
        patchwork.Children.Add(open);
        patchwork.Children.Add(save);
        patchwork.Children.Add(ToolbarButtons.Separator());
        patchwork.Children.Add(undoButton);
        patchwork.Children.Add(redoButton);
        patchwork.Children.Add(tidy);
        patchwork.Children.Add(ToolbarButtons.Separator());
        patchwork.Children.Add(codeButton);
        patchwork.Children.Add(controlsButton);
        patchwork.Children.Add(swapButton);

        // On its own, between what is done to the patch and what is done to
        // the program: pausing, rewinding and recording are neither — all are
        // facts about the performance, not an edit Ctrl+Z takes back.
        var transport = ToolbarButtons.Group();
        transport.Children.Add(pauseButton);
        transport.Children.Add(rewindButton);
        transport.Children.Add(recordButton);

        assistantButton.IsEnabled = plugins.Assistants.Count > 0;
        ToolTip.SetTip(assistantButton, plugins.Assistants.Count > 0
            ? "Describe a patch and have one built. Nothing is sent until you ask, and what "
              + "comes back is an edit Ctrl+Z takes off again."
            : "No assistant plugin is installed. See About for where plugins are looked for.");
        assistantButton.IsCheckedChanged += (_, _) => ShowAssistant(assistantButton.IsChecked == true);

        var settings = ToolbarButtons.Glyph("settings", "⚙", "Open the settings.");
        settings.Click += async (_, _) => await ShowSettingsAsync();

        var pluginsButton = ToolbarButtons.Drawn("plugins", Glyphs.Plug(), "Find, install and update plugins.");
        pluginsButton.Click += async (_, _) => await ShowPluginsAsync();

        var about = ToolbarButtons.Glyph("about", "ⓘ", "What this is, who wrote it, and what it may be done with.");
        about.Click += async (_, _) => await ShowAboutAsync();

        // The other end of the bar, because none of these is about the patch:
        // they are the program itself, and a thing reached for once a session
        // does not belong in the path of the things reached for constantly.
        var program = ToolbarButtons.Group();

        program.Children.Add(assistantButton);
        program.Children.Add(settings);
        program.Children.Add(pluginsButton);
        program.Children.Add(about);

        // One row, left to right, rather than the program group docked to the
        // far edge — everything reached from the toolbar sits together at the
        // near side instead of one end chasing the window's width. Unmargined
        // itself: patchwork and program each carry their own margin already,
        // from ToolbarButtons.Group(), and stacking a second one here would double the gaps.
        var bar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        bar.Children.Add(patchwork);
        bar.Children.Add(ToolbarButtons.Separator());
        bar.Children.Add(transport);
        bar.Children.Add(ToolbarButtons.Separator());
        bar.Children.Add(program);

        return new Border
        {
            Background = new SolidColorBrush(Colors.Toolbar),
            BorderBrush = new SolidColorBrush(Colors.Edge),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = bar,
        };

        void PutTheBoxBack()
        {
            restoring = true;
            presets.SelectedIndex = presetShowing;
            restoring = false;
        }
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
    internal void ClearPresetSelection()
    {
        presetShowing = -1;

        if (presetsPicker is not null) presetsPicker.SelectedIndex = -1;
    }

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

        // Every section is a set of controls lent to the window rather than
        // built for it, so what they were last set to is still on them the next
        // time this is opened. The window around them is built fresh, so each
        // section the window owns has to be taken back from the last one first.
        foreach (var section in new[]
                 { graphicsSection, canvasSection.View, recordingSection, soundSection, midiSection, filesSection.View, updatesSection.View, usageSection.View })
            if (section.Parent is ContentControl lender) lender.Content = null;

        var save = new Button { Content = "Save", Width = 84 };

        // Asked as the window opens rather than kept from the last time: ffmpeg
        // may have been installed, moved or taken away since, and the Recording
        // tab's note is only worth anything if it is about now. Not awaited —
        // the window opens while the search runs and the note fills itself in.
        _ = ShowFfmpegAsync();

        // Tabs rather than one long column, so moving between sections is a
        // click rather than a scroll, listed down the left so a section added
        // later is one more row rather than a strip running out of width. A
        // fixed size, so the window does not jump as the sections are flicked
        // through; a section taller than that scrolls inside its own tab. Save
        // sits under them all, because it saves them all — not only the tab
        // showing.
        var tabs = new TabControl
        {
            Name = "settingsTabs",
            TabStripPlacement = Dock.Left,
            Width = SettingsWidth,
            Height = SettingsHeight,
            Padding = new Thickness(4, 6, 0, 0),
        };

        tabs.Items.Add(SectionTab("Graphics", graphicsSection));
        tabs.Items.Add(SectionTab("Canvas", canvasSection.View));
        tabs.Items.Add(SectionTab("Recording", recordingSection));
        tabs.Items.Add(SectionTab("Sound", soundSection));
        tabs.Items.Add(SectionTab("MIDI", midiSection));
        tabs.Items.Add(SectionTab("Assistant", panel.SettingsSection()));
        tabs.Items.Add(SectionTab("Files", filesSection.View));
        tabs.Items.Add(SectionTab("Updates", updatesSection.View));
        tabs.Items.Add(SectionTab("Usage", usageSection.View));

        var content = new StackPanel { Spacing = 12, Margin = new Thickness(18, 4, 18, 18) };

        // Cancel answers exactly what the cross and Escape answer, so all three
        // take the one way out below rather than each undoing things itself.
        var cancel = new Button { Content = "Cancel", Width = 84 };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { save, cancel },
        };

        // A line rather than a box around the tabs, so it reads as one sheet
        // that ends before the buttons rather than a bordered pane sitting on
        // another.
        var divider = new Border { Height = 1, Background = new SolidColorBrush(Colors.Separator) };

        content.Children.Add(tabs);
        content.Children.Add(divider);
        content.Children.Add(buttons);

        save.Click += (_, _) =>
        {
            panel.SaveSettings();
            SaveOutputSettings();
            updatesSection.Save();
            usageSection.Save();
            canvasSection.Save();
            filesSection.Save();

            // Saving is the end of the errand, so the window goes with it.
            Dialog.Close(save, true);
        };

        cancel.Click += (_, _) => Dialog.Close(cancel, false);

        bool saved;

        settingsAreUp = true;

        usage.Count(Used.Settings);

        try
        {
            saved = await this.ShowDialog<bool>("Settings", content);
        }
        finally
        {
            settingsAreUp = false;
        }

        // Cancel, the cross and Escape all answer false — see Dialog.ShowDialog —
        // which is every way out of this window that is not Save. Whatever was typed
        // or picked since it opened belongs to this window, and only Save is
        // allowed to keep it.
        if (saved) return;

        panel.DiscardSettings();
        ShowOutputSettings(outputSettings);
        updatesSection.Show();
        usageSection.Show();
        canvasSection.Show();
        filesSection.Show();
    }

    /// <summary>
    /// One section of the settings window as a tab. The header is a text block
    /// sized like the rest of the window, because the theme's own tab header is
    /// set at page-title size.
    /// </summary>
    /// <remarks>
    /// The section scrolls in its own viewer, since the tabs are a fixed height
    /// and an assistant's form is as long as its provider declares it to be.
    /// </remarks>
    private static TabItem SectionTab(string name, Control section)
    {

        section.HorizontalAlignment = HorizontalAlignment.Left;

        return new TabItem
        {
            Header = new TextBlock { Text = name, FontSize = Text.Emphasis, FontWeight = FontWeight.SemiBold },
            Content = new Border
            {
                Padding = new Thickness(16),
                Child = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = section,
                },
            },
            Padding = new Thickness(4, 6, 12, 6),
            Height = 46,
            Width = 120,
        };
    }

    /// <summary>
    /// The settings tabs' size, list and section together: wide enough for the
    /// list beside a 280-pixel section with room for its scroll bar, and tall
    /// enough for every tab in one column and an assistant's usual form without a scroll bar.
    /// </summary>
    private const double SettingsWidth = 480;

    /// <inheritdoc cref="SettingsWidth"/>
    private const double SettingsHeight = 460;

    /// <summary>
    /// The About window. Its contents are built fresh each time rather than kept
    /// like the settings section: nothing in it is a control anybody has typed
    /// into, so there is nothing to carry from one opening to the next.
    /// </summary>
    private async Task ShowAboutAsync() =>
        await this.ShowDialog("About", About.View(PluginSummary.Text(plugins, sound.Failure, assistant?.Summary)));

    /// <summary>
    /// The bar along the bottom: what the patch costs, and whatever there is to
    /// say about it.
    /// </summary>
    /// <remarks>
    /// A grid rather than a row of controls, because a row hands every child the
    /// width it asks for and lets the last fall off the end — and the report is
    /// the one thing here that is worth trimming last. The count on the right is
    /// sized to its own text rather than a share of the bar, so the report only
    /// gives up width the count is actually using. Which sound backend is open
    /// and which assistant is chosen are said in the About window, not here.
    /// <para>
    /// The letter is last, at the far edge: it is the one thing on the bar that is
    /// not about the patch, and it is reached for rarely enough that being out of
    /// the way is the point (ADR-0136).
    /// </para>
    /// </remarks>
    private Control BuildStatusBar()
    {
        var bar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            Margin = new Thickness(12, 5),
        };

        // The popup behind the report hangs off the window rather than off the
        // line, so what it is to look like has to be said here — the same way
        // the palette's is, and for the same reason.
        Styles.Add(ReportLine.Trim());
        Styles.Add(ModulePlate.Naming());

        // The gap a StackPanel gives for free, added by hand here since this is
        // a grid. On the children rather than the grid, so the first column
        // starts at the margin and the last one keeps every pixel it is given.
        status.Margin = new Thickness(8, 0, 0, 0);

        var letter = StatusGlyph(
            "letter", Glyphs.Letter(), "Write to Flyback's author. Anything you like, good or bad.");

        letter.Click += async (_, _) => await WriteToTheAuthorAsync();

        Grid.SetColumn(report, 0);
        Grid.SetColumn(status, 1);
        Grid.SetColumn(letter, 2);

        bar.Children.Add(report);
        bar.Children.Add(status);
        bar.Children.Add(letter);

        return new Border
        {
            Background = new SolidColorBrush(Colors.Panel),
            BorderBrush = new SolidColorBrush(Colors.Edge),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = bar,
        };
    }

    /// <summary>
    /// A button for the status bar: the same quiet flat square the dialog frame
    /// closes with, sized so the bar stays as tall as its one line of prose.
    /// </summary>
    /// <remarks>
    /// Not <see cref="ToolbarButtons.Glyph"/>, which is a toolbar's 34 by 30 and
    /// would make the bar half again as tall. The theme's own minimum has to be
    /// undone for that, which is what the two zeroes are. Drawn rather than
    /// typed, since the shipped font has no envelope and what stands in for one
    /// is the platform's emoji face.
    /// </remarks>
    private static Button StatusGlyph(string name, Control glyph, string tip)
    {
        var button = new Button
        {
            Name = name,
            Content = glyph,
            Width = 22,
            Height = 18,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(10, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Text.Muted,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToolTip.SetTip(button, tip);

        return button;
    }
}
