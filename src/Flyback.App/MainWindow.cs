using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.App.Audio;
using Flyback.App.Controls;
using Flyback.App.Midi;
using Flyback.Core.Compile;
using Flyback.Core.Render;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App;

public sealed partial class MainWindow : Window
{
    /// <summary>
    /// How long an export runs. The only thing about an export that cannot be
    /// defaulted sensibly — a patch is an endless function of time, so where to
    /// stop is a decision rather than a setting — which is why it is a control
    /// on the toolbar rather than a constant in here, and why both exports read
    /// the same one.
    /// </summary>
    private readonly NumericUpDown length = new()
    {
        Value = 10m,
        Minimum = 1m,
        Maximum = 600m,
        Increment = 5m,
        FormatString = "0",
        Width = 112,
    };

    /// <summary>
    /// One button for both kinds of file, because which kind you get is a
    /// property of the name you give it rather than of which control you
    /// pressed — and because a patch that makes no sound has nothing to put
    /// behind a second one. Fixed width: its label becomes the one that stops an
    /// export, and a button that resizes mid-render drags the panel about.
    /// </summary>
    private readonly Button exportButton = new() { Content = "Export…", Width = 118 };

    /// <summary>
    /// Beside the export and deliberately not folded into it. They answer
    /// different questions — one writes what the patch would do, the other what
    /// it did — and a take has no length to set, so there is nothing for them to
    /// share but the file dialog. Same fixed width, for the same reason: its
    /// label becomes the one that stops a take.
    /// </summary>
    private readonly Button recordButton = new() { Content = "Record…", Width = 118 };

    private readonly ComboBox resolution = new Picker
    {
        ItemsSource = Resolutions.Select(r => r.Label).ToList(),
        SelectedIndex = DefaultResolution,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };
    /// <summary>
    /// The Output's own panel, assembled once and shown whenever that block is
    /// selected. Its contents outlive the inspector being rebuilt.
    /// </summary>
    private readonly StackPanel outputSettings = new()
    {
        Spacing = 8,
        Margin = new Thickness(0, 16, 0, 0),
    };

    /// <summary>Live while an export is running, and the only thing that says one is.</summary>
    private CancellationTokenSource? export;

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
    /// The files a bundle carries, while one is open, and null while the
    /// document is a loose patch backed by a folder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Held rather than unpacked, which is the whole of what makes a bundle a
    /// document here instead of an archive somebody has to spill onto their disk
    /// before working on it: nothing is written anywhere until they save.
    /// </para>
    /// <para>
    /// It costs one copy of the compressed bytes for as long as the bundle is
    /// open, and costs the undo history nothing at all — the history is snapshots
    /// of the patch, the patch is paths, and payloads sit beside the document
    /// exactly as the two caches above already do. That distinction is the whole
    /// of what ADR-0052 was about, and it is untouched.
    /// </para>
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
    private Grid? rightPanel;
    private Border? previewBox;
    private Control? toolbar;
    private Control? statusBar;

    /// <summary>
    /// Behind the inspector, and brighter when there is nothing selected for it
    /// to sit behind. Never hit-testable, so it cannot swallow a click meant for
    /// a slider underneath.
    /// </summary>
    private readonly LogoMark watermark = new()
    {
        // Fills the panel and centres itself, so it grows with the splitter
        // instead of being sized for one particular panel width.
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        Margin = new Thickness(20),

        // The no-selection value, so it is never briefly full strength if
        // something ever builds the panel before the first selection lands.
        Opacity = 0.14,
        IsHitTestVisible = false,
    };

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
        Margin = new Thickness(12),
        Spacing = 8,
    };
    private readonly TextBlock status = new()
    {
        VerticalAlignment = VerticalAlignment.Center,

        // Every line on this bar shares one row of a narrow window, so this one
        // gives way the same way the report beside it does rather than being
        // sheared off at whatever character the edge fell on.
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>
    /// The one line anything is said on, and the log of what has been said. See
    /// <see cref="Report"/>, which is the only thing that writes to it.
    /// </summary>
    private readonly ReportLine report = new();

    private readonly TextBlock backend = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 12,
        Opacity = 0.55,
    };

    private readonly TextBlock helper = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 12,
        Opacity = 0.55,
    };

    private readonly ToggleButton audioButton = new() { Content = "Audio off", Width = 92 };
    private readonly ToggleButton gpuButton = new() { Content = "GPU", Width = 60 };
    private readonly ToggleButton assistantButton =
        Toggle("assistant", "✦", "Describe a patch and have one built.");

    private readonly Button undoButton = Glyph("undo", "↶", "Take back the last edit  (Ctrl+Z)");
    private readonly Button redoButton = Glyph("redo", "↷", "Put it back  (Ctrl+Shift+Z)");

    /// <summary>
    /// Held so that laying out can be switched off where it would not last: a
    /// patch built from text is re-laid on every evaluation.
    /// </summary>
    private Button? tidyButton;

    private AssistantPanel? assistant;
    private RowDefinition? assistantRow;
    private GridSplitter? assistantSplitter;

    /// <summary>How much of the window the assistant had when it was last open.</summary>
    private GridLength assistantShare = new(1, GridUnitType.Star);

    /// <summary>
    /// Read before this window existed, and already installed. Nothing here
    /// knows which backends or modules there are, or what they are called.
    /// </summary>
    private readonly PluginCatalog plugins = Startup.Plugins;

    private readonly AudioSetup sound;
    private readonly AudioEngine audio;

    /// <summary>
    /// Everything that plays the patch from outside it. The mirror of
    /// <see cref="audio"/>, which takes what the patch makes to a device.
    /// </summary>
    /// <remarks>
    /// Assigned in the constructor rather than beside the declaration, because it
    /// is handed the MIDI backend the plugins offered and a field initializer
    /// would be depending on <see cref="plugins"/> being declared above it.
    /// </remarks>
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
    public MainWindow(string? groupFolder = null)
    {
        this.groupFolder = groupFolder;

        sound = OpenAudio(plugins);
        audio = new AudioEngine(sound.Device);

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

        // Everything let go when this stops being the window you are typing
        // into. A key released over another program is a key this never hears
        // about, and the note would hang until something else happened to move
        // it — alt-tabbing away mid-chord should not leave a drone behind.
        Deactivated += (_, _) => midi.AllOff();

        Title = BaseTitle;
        Width = 1280;
        Height = 800;
        MinWidth = 860;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Colors.Window);

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
        editor.Reported += (_, message) => Report(message);

        // The other copy of everything said. A status bar is written over by the
        // next compile and the log behind it is five deep, so a run watched from
        // a terminal — which is the run anybody debugging is having — would
        // otherwise keep no account of itself at all. Trace rather than the
        // console directly: Program.Main is where it is decided whether there is
        // a terminal worth writing to, Avalonia's own diagnostics already go
        // there, and a second destination is a second listener rather than a
        // second call here.
        report.Said += (_, message) =>
            Trace.WriteLine($"{DateTime.Now:HH:mm:ss}  {message}");

        // Before the layout, because these are live from the moment the window
        // is: the preview needs its resolution and its backend whether or not
        // anybody has selected the Output to look at them.
        WireOutputControls();

        Content = BuildLayout();

        // The preset the box opens on, which is the patch about to be built —
        // said here so the title agrees with the toolbar from the first frame.
        patchName = plugins.Presets.Count > 0 ? plugins.Presets[0].Name : null;

        editor.Patch = Presets.Default();

        // Sound on, if there is anything to make it with. This is the half of
        // the instrument that a silent launch hides completely: the picture
        // announces itself, and a patch whose audio side is doing nothing looks
        // exactly like one whose audio side is working. Turned on here rather
        // than in the toolbar because the toolbar is built before the preset is
        // loaded, and the engine would be started on an empty patch.
        //
        // A device that will not open is not fatal — SetAudioEnabled reports it
        // and puts the button back — so this cannot stop the window appearing.
        audioButton.IsChecked = audioButton.IsEnabled;

        var ticker = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        ticker.Tick += (_, _) => UpdateStatus();
        ticker.Start();
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
                editor.ApplyEdit(patch);
                preview.Rewind();
            },
            // Wrapped rather than handed over as it stands, because the third
            // thing Report takes is about how a line ages in the log and the
            // panel has no business knowing there is one.
            (message, detail) => Report(message, detail),
            samples: Sounds,
            pictures: Pictures)
        {
            IsVisible = false,
        };

        toolbar = BuildToolbar();
        statusBar = BuildStatusBar();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(statusBar, Dock.Bottom);

        // The two flexible columns are star-sized: GridSplitter redistributes
        // star weights, and a fixed-pixel column next to one just gets squeezed.
        columns = new Grid
        {
            // Named because the fullscreen preview's test has to find exactly
            // this grid, and counting its columns stopped telling it apart from
            // the toolbar's the moment the palette left the layout.
            Name = "columns",
            ColumnDefinitions =
            [
                new ColumnDefinition(new GridLength(3, GridUnitType.Star)) { MinWidth = 280 },
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(1.6, GridUnitType.Star)) { MinWidth = 300 },
            ],
        };

        // Rows rather than a dock, so the edge between the patch and the
        // assistant can be dragged. Both flexible rows are star-sized for the
        // reason the columns are: a GridSplitter redistributes star weights, and
        // a fixed-pixel track beside a star one just gets squeezed.
        //
        // In the canvas column rather than across the window, because what the
        // assistant is talking about is the patch — the palette and the
        // inspector are no part of the conversation, and pushing them up out of
        // reach to make room for it costs more than the width it buys.
        var canvas = new Grid
        {
            RowDefinitions =
            [
                new RowDefinition(new GridLength(2.2, GridUnitType.Star)) { MinHeight = 160 },
                new RowDefinition(GridLength.Auto),
                new RowDefinition(assistantShare),
            ],
        };

        assistantRow = canvas.RowDefinitions[2];
        assistantSplitter = new GridSplitter { Background = Brushes.Transparent, Height = 5 };

        // The text sits in the canvas's own row rather than under it: they are
        // two views of one patch and only ever one of them shows, so putting
        // them side by side would halve the room for each and invite the
        // question ADR-0068 exists to answer — which one is being edited.
        Grid.SetRow(editor, 0);
        Grid.SetRow(source, 0);
        Grid.SetRow(assistantSplitter, 1);
        Grid.SetRow(assistant, 2);

        canvas.Children.Add(editor);
        canvas.Children.Add(source);
        canvas.Children.Add(assistantSplitter);
        canvas.Children.Add(assistant);

        BuildPalette();
        Grid.SetColumn(canvas, 0);

        var rightSplitter = new GridSplitter { Width = 5, Background = Brushes.Transparent };
        Grid.SetColumn(rightSplitter, 1);

        var right = rightPanel = BuildRightPanel();
        Grid.SetColumn(right, 2);

        columns.Children.Add(canvas);
        columns.Children.Add(rightSplitter);
        columns.Children.Add(right);

        // Hidden costs nothing, which is why this needs no dialog — and this
        // application has none.
        ShowAssistant(false);

        root.Children.Add(toolbar);
        root.Children.Add(statusBar);
        root.Children.Add(columns);

        return root;
    }

    /// <summary>
    /// Opens or closes the assistant, and gives its share of the window back
    /// when it closes.
    /// </summary>
    /// <remarks>
    /// A star row keeps its weight whether or not anything in it is visible, so
    /// hiding the panel alone would leave a third of the window empty. The share
    /// is kept rather than recomputed, so a panel dragged to a size somebody
    /// liked comes back that size — and the row's minimum has to go with it,
    /// since a minimum outranks a height of zero and would hold the gap open.
    /// </remarks>
    private void ShowAssistant(bool shown)
    {
        if (assistant is null || assistantRow is null || assistantSplitter is null) return;

        if (!shown && assistant.IsVisible) assistantShare = assistantRow.Height;

        assistant.IsVisible = shown;
        assistantSplitter.IsVisible = shown;

        assistantRow.MinHeight = shown ? 140d : 0d;
        assistantRow.Height = shown ? assistantShare : new GridLength(0);
    }

    private Control BuildToolbar()
    {
        // Grouped by kind and only then by where they came from, so the list
        // reads as three sections: the patches that teach one thing, the ones
        // about the two sinks meeting, and the big ones. A stable sort, so within
        // a kind the engine's own still come before any plugin's — the list the
        // app opens on is the same wherever it is installed.
        var available = plugins.Presets.OrderBy(p => p.Kind).ToList();

        // A Picker rather than a plain list, and this is the one where it matters
        // most: every change here throws the patch on the canvas away, so a
        // keystroke that moved the selection would be a keystroke that discarded
        // somebody's work — twenty times over if it were an arrow held down.
        var presets = new Picker
        {
            ItemsSource = available,
            SelectedIndex = 0,
            Width = 160,

            // Two lines per row in the dropdown: what it is called, and the one
            // sentence saying what it is for. The descriptions are the part of
            // each preset's documentation a person choosing between twenty names
            // actually needs.
            ItemTemplate = new FuncDataTemplate<PatchPreset>((preset, _) =>
                preset is null
                    ? null
                    : new StackPanel
                    {
                        Spacing = 1,
                        Children =
                        {
                            new TextBlock { Text = preset.Name, FontSize = 12 },
                            new TextBlock
                            {
                                Text = preset.Description,
                                FontSize = 10,
                                Foreground = new SolidColorBrush(Colors.Muted),
                                TextWrapping = TextWrapping.Wrap,
                                MaxWidth = 260,
                                IsVisible = preset.Description.Length > 0,
                            },
                        },
                    }),

            // And the name alone in the box itself, which is a separate template
            // rather than the same one: a closed picker that grew to two lines
            // would push the whole toolbar down to say something the dropdown
            // already says, and the toolbar is a row of one-line controls.
            SelectionBoxItemTemplate = new FuncDataTemplate<PatchPreset>((preset, _) =>
                preset is null ? null : new TextBlock { Text = preset.Name, FontSize = 12 }),
        };

        // Which preset is on the canvas, so a refused change can put the box
        // back where it was. Setting the index raises this same handler, hence
        // the flag around it.
        var showing = 0;
        var restoring = false;

        presets.SelectionChanged += async (_, _) =>
        {
            if (restoring) return;
            if (presets.SelectedIndex < 0 || presets.SelectedIndex >= available.Count) return;
            if (presets.SelectedIndex == showing) return;

            var wanted = presets.SelectedIndex;

            if (!await MayReplaceThePatchAsync())
            {
                PutTheBoxBack();
                return;
            }

            var preset = available[wanted];

            try
            {
                // A preset from a plugin is built here, not when it was
                // registered, so this is where a plugin that offered a patch
                // using modules it failed to add finally shows up.
                var built = preset.Build(plugins.Modules);

                // Named before it is shown, because showing it is what redraws
                // the title — and named at all because a preset is one of the
                // three ways a patch arrives and the only one with no file to
                // be named after.
                patchName = preset.Name;

                editor.Patch = built;
                preview.Rewind();

                // A preset arrives as a graph and no text describes it, so the
                // canvas owns it until somebody applies one — ADR-0068.
                DropSource();

                showing = wanted;
            }
            catch (Exception ex)
            {
                Report($"Could not build the '{preset.Name}' preset: {ex.Message}");
                PutTheBoxBack();
            }
        };

        var open = Drawn("open", Glyphs.Open(), "Open a patch…");
        open.Click += async (_, _) =>
        {
            if (await MayReplaceThePatchAsync()) await OpenPatchAsync();
        };

        var save = Drawn("save", Glyphs.Save(), "Save this patch…");
        save.Click += async (_, _) => await SavePatchAsync();

        undoButton.Click += (_, _) => editor.Undo();
        redoButton.Click += (_, _) => editor.Redo();

        var tidy = tidyButton =
            Drawn("tidy", Glyphs.Tidy(), "Lay the modules out so the patch reads left to right  (Ctrl+L)");

        tidy.Click += (_, _) => editor.Tidy();

        WireSource();
        RefreshEditState();

        // What is done to the patch, in the order it is done: pick one, open or
        // save one, take an edit back. Tidy sits with undo and redo rather than
        // with the files, because it is an edit and is taken back like one.
        var patchwork = Row();

        patchwork.Children.Add(Label("Patch"));
        patchwork.Children.Add(presets);
        patchwork.Children.Add(open);
        patchwork.Children.Add(save);
        patchwork.Children.Add(Separator());
        patchwork.Children.Add(undoButton);
        patchwork.Children.Add(redoButton);
        patchwork.Children.Add(tidy);
        patchwork.Children.Add(Separator());
        patchwork.Children.Add(codeButton);

        assistantButton.IsEnabled = plugins.Assistants.Count > 0;
        ToolTip.SetTip(assistantButton, plugins.Assistants.Count > 0
            ? "Describe a patch and have one built. Nothing is sent until you ask, and what "
              + "comes back is an edit Ctrl+Z takes off again."
            : "No assistant plugin is installed. See the status bar for where plugins are looked for.");
        assistantButton.IsCheckedChanged += (_, _) => ShowAssistant(assistantButton.IsChecked == true);

        var settings = Glyph("settings", "⚙", "Which assistant to use, and the key it needs.");
        settings.Click += async (_, _) => await ShowSettingsAsync();

        var about = Glyph("about", "ⓘ", "What this is, who wrote it, and what it may be done with.");
        about.Click += async (_, _) => await ShowAboutAsync();

        // The other end of the bar, because none of these is about the patch:
        // they are the program itself, and a thing reached for once a session
        // does not belong in the path of the things reached for constantly.
        var program = Row();

        program.Children.Add(assistantButton);
        program.Children.Add(settings);
        program.Children.Add(about);

        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

        Grid.SetColumn(patchwork, 0);
        Grid.SetColumn(program, 2);

        bar.Children.Add(patchwork);
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
            presets.SelectedIndex = showing;
            restoring = false;
        }
    }

    /// <summary>
    /// The settings window. One button on the toolbar rather than one per thing
    /// that has settings, so what it holds can grow without the bar doing the
    /// same — today that is the assistant's provider and key, which is all there
    /// is in this program that a person has to tell it rather than show it.
    /// </summary>
    private async Task ShowSettingsAsync()
    {
        if (assistant is not { } panel) return;

        // The panel's own controls, lent to the window rather than built for it,
        // so that what a key or a provider was last set to is still on them the
        // next time this is opened.
        await this.ShowDialog("Settings", panel.SettingsSection());
    }

    /// <summary>
    /// The About window. Its contents are built fresh each time rather than kept
    /// like the settings section: nothing in it is a control anybody has typed
    /// into, so there is nothing to carry from one opening to the next.
    /// </summary>
    private async Task ShowAboutAsync() =>
        await this.ShowDialog("About", About.View());

    /// <summary>
    /// The bar along the bottom: what the patch costs, what it is being played
    /// through, and whatever there is to say about it.
    /// </summary>
    /// <remarks>
    /// A grid rather than a row of controls, because a row hands every child all
    /// the width it asks for and lets the last of them fall off the end — and
    /// the report is the one thing here that a person actually has to read. The
    /// two prose columns share what the two fixed ones leave, and each says so
    /// with an ellipsis when its share is not enough.
    /// <para>
    /// Not evenly. The counts on the left are a fixed sentence of a known
    /// length, and the split is the one that fits all of it in a window of the
    /// size this one opens at — a share rather than that length, so a longer
    /// word in it never pushes the report off the end again.
    /// </para>
    /// </remarks>
    private Control BuildStatusBar()
    {
        var bar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.4*,Auto,Auto,*"),
            Margin = new Thickness(12, 5),
        };

        // The popup behind the report hangs off the window rather than off the
        // line, so what it is to look like has to be said here — the same way
        // the palette's is, and for the same reason.
        Styles.Add(ReportLine.Trim());

        backend.Text = sound.Output is { } output ? $"sound: {output.Name}" : "sound: none";
        ToolTip.SetTip(backend, PluginSummary());

        // Always visible, so the capability's existence is never a surprise to
        // somebody who did not go looking for it.
        helper.Text = assistant?.Summary ?? "assistant: none";
        ToolTip.SetTip(helper, PluginSummary());

        // The gap a StackPanel gives for free, added by hand here since this is
        // a grid. On the children rather than the grid, so the first column
        // starts at the margin and the last one keeps every pixel it is given.
        backend.Margin = new Thickness(16, 0, 0, 0);
        helper.Margin = new Thickness(16, 0, 0, 0);
        report.Margin = new Thickness(16, 0, 0, 0);

        Grid.SetColumn(status, 0);
        Grid.SetColumn(backend, 1);
        Grid.SetColumn(helper, 2);
        Grid.SetColumn(report, 3);

        bar.Children.Add(status);
        bar.Children.Add(backend);
        bar.Children.Add(helper);
        bar.Children.Add(report);

        return new Border
        {
            Background = new SolidColorBrush(Colors.Panel),
            BorderBrush = new SolidColorBrush(Colors.Edge),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = bar,
        };
    }

    // --- shared bits of chrome -----------------------------------------------

    /// <summary>One group of toolbar controls, laid out along it.</summary>
    private static StackPanel Row() => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Margin = new Thickness(12, 8),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// A toolbar button that is a symbol rather than a word.
    /// </summary>
    /// <remarks>
    /// The tip is not decoration here: with the labels gone it is the only place
    /// the button says what it does, so every one of these has one and it is a
    /// sentence rather than a repeat of the icon's name.
    /// <para>
    /// Named as well, so a test can find the button without reading it — a
    /// glyph is a poor thing to write an assertion against.
    /// </para>
    /// </remarks>
    private static Button Glyph(string name, string glyph, string tip) =>
        Marked(new Button(), name, glyph, tip);

    /// <summary>The same, for the two icons that are drawn rather than typed.</summary>
    private static Button Drawn(string name, Control icon, string tip) =>
        Marked(new Button(), name, icon, tip);

    /// <summary>The same, for a button that stays down.</summary>
    private static ToggleButton Toggle(string name, string glyph, string tip) =>
        Marked(new ToggleButton(), name, glyph, tip);

    private static T Marked<T>(T button, string name, object content, string tip)
        where T : ContentControl
    {
        button.Name = name;
        button.Content = content;
        button.Width = 34;
        button.Height = 30;
        button.Padding = new Thickness(0);
        button.FontSize = 15;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;

        ToolTip.SetTip(button, tip);

        return button;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Opacity = 0.6,
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Control Separator() => new Border
    {
        Width = 1,
        Margin = new Thickness(4, 4),
        Background = new SolidColorBrush(Colors.Separator),
    };
}
