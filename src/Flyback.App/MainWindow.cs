using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Flyback.App.Audio;
using Flyback.App.Controls;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;

namespace Flyback.App;

public sealed class MainWindow : Window
{
    private static readonly (string Label, PixelSize Size)[] Resolutions =
    [
        ("320 x 180", new PixelSize(320, 180)),
        ("480 x 270", new PixelSize(480, 270)),
        ("640 x 360", new PixelSize(640, 360)),
        ("960 x 540", new PixelSize(960, 540)),
        ("1280 x 720", new PixelSize(1280, 720)),
        ("1920 x 1080", new PixelSize(1920, 1080)),
    ];

    private static readonly PixelSize ExportSize = new(1920, 1080);

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

    private readonly Button exportFrame = new() { Content = "Save frame…" };

    private readonly ComboBox resolution = new()
    {
        ItemsSource = Resolutions.Select(r => r.Label).ToList(),
        SelectedIndex = DefaultResolution,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>960 x 540: enough to judge a patch by, cheap enough to keep up.</summary>
    private const int DefaultResolution = 3;

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
    private readonly PreviewHost preview = new();

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

    private readonly StackPanel modules = new() { Margin = new Thickness(10, 0, 10, 8), Spacing = 2 };
    private readonly TextBox filter = new()
    {
        PlaceholderText = "Filter modules",
        FontSize = 12,
    };

    /// <summary>
    /// Opens the list of plugins to show modules from. The engine's own are one
    /// entry among the rest, because that is exactly what the catalogue thinks
    /// they are.
    /// </summary>
    private readonly Button sources = new()
    {
        FontSize = 12,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Left,
        Padding = new Thickness(8, 4),
    };

    /// <summary>
    /// Providers whose modules are hidden. Stored as what is *off* rather than
    /// what is on, so a plugin installed later shows up without having to be
    /// found and ticked.
    /// </summary>
    private readonly HashSet<string> hidden = [];

    private readonly StackPanel inspector = new() { Margin = new Thickness(12), Spacing = 8 };
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock issues = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = new SolidColorBrush(Colours.Attention),
    };

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

    private readonly ToggleButton playButton = new() { Content = "Pause", Width = 78, IsChecked = true };
    private readonly ToggleButton audioButton = new() { Content = "Audio off", Width = 92 };
    private readonly ToggleButton gpuButton = new() { Content = "GPU", Width = 60 };

    private const string GpuTip =
        "Render the picture with a shader instead of the processor. Turn it off to " +
        "compare the two, or if a long session starts to look stepped.";
    private readonly ToggleButton assistantButton = new() { Content = "Assistant", Width = 92 };

    private readonly Button undoButton = new() { Content = "Undo", Width = 66 };
    private readonly Button redoButton = new() { Content = "Redo", Width = 66 };

    /// <summary>The window title, before anything is said about the patch in it.</summary>
    private const string BaseTitle = "Flyback";

    /// <summary>
    /// Set once the question about unsaved work has been asked and answered, so
    /// the second Close does not ask it again. A close has to be cancelled to
    /// put a dialog up at all — nothing may block inside OnClosing — so the way
    /// back out is to close again once there is an answer.
    /// </summary>
    private bool leaving;



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

    public MainWindow()
    {
        sound = OpenAudio(plugins);
        audio = new AudioEngine(sound.Device);

        Title = BaseTitle;
        Width = 1280;
        Height = 800;
        MinWidth = 860;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Colours.Window);

        editor.PatchChanged += (_, _) => Recompile();
        editor.SelectionChanged += (_, _) => BuildInspector();
        editor.HistoryChanged += (_, _) => RefreshEditState();


        // Before the layout, because these are live from the moment the window
        // is: the preview needs its resolution and its backend whether or not
        // anybody has selected the Output to look at them.
        WireOutputControls();

        Content = BuildLayout();

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

    // --- layout ---------------------------------------------------------------

    private Control BuildLayout()
    {
        var root = new DockPanel();

        // Before the bars, because both of them ask it what it is called.
        assistant = new AssistantPanel(
            plugins,
            () => editor.Patch,
            async patch =>
            {
                if (!await MayReplaceThePatchAsync()) return false;

                editor.Patch = patch;
                preview.Rewind();
                return true;
            },
            Report)
        {
            IsVisible = false,
        };

        var toolbar = BuildToolbar();
        var statusBar = BuildStatusBar();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(statusBar, Dock.Bottom);

        // The two flexible columns are star-sized: GridSplitter redistributes
        // star weights, and a fixed-pixel column next to one just gets squeezed.
        var columns = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(220, GridUnitType.Pixel) { MinWidth = 150 },
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(3, GridUnitType.Star)) { MinWidth = 280 },
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(1.6, GridUnitType.Star)) { MinWidth = 300 },
            ],
        };

        var palette = BuildPalette();
        Grid.SetColumn(palette, 0);
        Grid.SetColumn(editor, 2);

        var leftSplitter = new GridSplitter { Width = 5, Background = Brushes.Transparent };
        Grid.SetColumn(leftSplitter, 1);

        var rightSplitter = new GridSplitter { Width = 5, Background = Brushes.Transparent };
        Grid.SetColumn(rightSplitter, 3);

        var right = BuildRightPanel();
        Grid.SetColumn(right, 4);

        columns.Children.Add(palette);
        columns.Children.Add(leftSplitter);
        columns.Children.Add(editor);
        columns.Children.Add(rightSplitter);
        columns.Children.Add(right);

        // Rows rather than a dock, so the edge between the patch and the
        // assistant can be dragged. Both flexible rows are star-sized for the
        // reason the columns above are: a GridSplitter redistributes star
        // weights, and a fixed-pixel track beside a star one just gets squeezed.
        // Full width rather than a column, because streamed prose wants width.
        var body = new Grid
        {
            RowDefinitions =
            [
                new RowDefinition(new GridLength(2.2, GridUnitType.Star)) { MinHeight = 160 },
                new RowDefinition(GridLength.Auto),
                new RowDefinition(assistantShare),
            ],
        };

        assistantRow = body.RowDefinitions[2];
        assistantSplitter = new GridSplitter { Background = Brushes.Transparent, Height = 5 };

        Grid.SetRow(columns, 0);
        Grid.SetRow(assistantSplitter, 1);
        Grid.SetRow(assistant, 2);

        body.Children.Add(columns);
        body.Children.Add(assistantSplitter);
        body.Children.Add(assistant);

        // Hidden costs nothing, which is why this needs no dialog — and this
        // application has none.
        ShowAssistant(false);

        root.Children.Add(toolbar);
        root.Children.Add(statusBar);
        root.Children.Add(body);

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

    /// <summary>
    /// Sets up the controls that live on the Output's panel. Called once, from
    /// the constructor, rather than while building that panel: these are the
    /// state of the instrument and not of a selection, so they have to work
    /// before anything has been selected and keep their values after the panel
    /// showing them has been torn down and rebuilt.
    /// </summary>
    private void WireOutputControls()
    {
        resolution.SelectionChanged += (_, _) =>
        {
            if (resolution.SelectedIndex >= 0)
                preview.Resolution = Resolutions[resolution.SelectedIndex].Size;
        };
        preview.Resolution = Resolutions[DefaultResolution].Size;

        // On by default, because it is the one that keeps up with a large patch.
        // Turning it off is how two backends get compared, and the answer to a
        // long session drifting — see ADR-0035 on float32 and the phase
        // accumulator. It disables itself if the GPU turns out to be unusable.
        gpuButton.IsChecked = true;
        gpuButton.IsCheckedChanged += (_, _) =>
            preview.Use(gpuButton.IsChecked == true ? PreviewBackend.Gpu : PreviewBackend.Cpu);

        preview.BackendChanged += message =>
        {
            gpuButton.IsChecked = preview.Backend == PreviewBackend.Gpu;
            gpuButton.IsEnabled = preview.GpuAvailable;
            ToolTip.SetTip(gpuButton, preview.GpuAvailable ? GpuTip : message);
            Report(message);
        };

        ToolTip.SetTip(exportFrame, $"Render the current moment at {ExportSize.Width} x {ExportSize.Height} and write a PNG.");
        exportFrame.Click += async (_, _) => await SaveFrameAsync();

        // It cannot be switched on at all where no plugin offered a device. The
        // constructor turns it on once there is a patch to play — see there for
        // why it starts on rather than off.
        audioButton.IsEnabled = sound.Output is not null;
        ToolTip.SetTip(audioButton, sound.Output is { } output
            ? $"Play the patch through {output.Name}. Needs something wired into 'left'."
            : "No sound backend is installed. See the status bar for where plugins are looked for.");
        audioButton.IsCheckedChanged += (_, _) => SetAudioEnabled(audioButton.IsChecked == true);

        ToolTip.SetTip(length, "How many seconds an export writes.");

        // Shown while it is greyed out too, because a disabled control that will
        // not say why is the most annoying thing a panel can contain.
        ToolTip.SetShowOnDisabled(exportButton, true);

        exportButton.Click += async (_, _) =>
        {
            // The same button stops it. An export is the one thing here that
            // runs long enough to be worth abandoning, and it is already the
            // control your eye is on.
            if (export is not null)
            {
                export.Cancel();
                return;
            }

            await ExportAsync();
        };

        BuildOutputSettings();
    }

    private Control BuildToolbar()
    {
        // Plugin presets sit after the engine's own, so the list the app opens
        // on is the same wherever it is installed.
        var available = plugins.Presets;

        var presets = new ComboBox
        {
            ItemsSource = available.Select(p => p.Name).ToList(),
            SelectedIndex = 0,
            Width = 160,
        };

        // Which preset is on the canvas, so a refused change can put the box
        // back where it was. Setting the index raises this same handler, hence
        // the flag around it.
        var showing = 0;
        var restoring = false;

        void PutTheBoxBack()
        {
            restoring = true;
            presets.SelectedIndex = showing;
            restoring = false;
        }

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
                editor.Patch = preset.Build(plugins.Modules);
                preview.Rewind();
                showing = wanted;
            }
            catch (Exception ex)
            {
                Report($"Could not build the '{preset.Name}' preset: {ex.Message}");
                PutTheBoxBack();
            }
        };


        var open = new Button { Content = "Open…" };
        open.Click += async (_, _) =>
        {
            if (await MayReplaceThePatchAsync()) await OpenPatchAsync();
        };

        var save = new Button { Content = "Save…" };
        save.Click += async (_, _) => await SavePatchAsync();

        undoButton.Click += (_, _) => editor.Undo();
        redoButton.Click += (_, _) => editor.Redo();

        ToolTip.SetTip(undoButton, "Take back the last edit  (Ctrl+Z)");
        ToolTip.SetTip(redoButton, "Put it back  (Ctrl+Shift+Z)");

        RefreshEditState();


        playButton.IsCheckedChanged += (_, _) =>
        {
            preview.IsPlaying = playButton.IsChecked == true;
            playButton.Content = preview.IsPlaying ? "Pause" : "Play";
        };

        var rewind = new Button { Content = "Rewind" };
        rewind.Click += (_, _) =>
        {
            audio.Rewind();
            preview.Rewind();
        };

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(12, 8),
            VerticalAlignment = VerticalAlignment.Center,
        };

        bar.Children.Add(Label("Patch"));
        bar.Children.Add(presets);
        bar.Children.Add(open);
        bar.Children.Add(save);
        bar.Children.Add(Separator());
        bar.Children.Add(undoButton);
        bar.Children.Add(redoButton);
        bar.Children.Add(Separator());

        bar.Children.Add(playButton);
        bar.Children.Add(rewind);
        bar.Children.Add(Separator());
        assistantButton.IsEnabled = plugins.Assistants.Count > 0;
        ToolTip.SetTip(assistantButton, plugins.Assistants.Count > 0
            ? "Describe a patch and have one built. Nothing is sent until you ask, and nothing applied until you accept."
            : "No assistant plugin is installed. See the status bar for where plugins are looked for.");
        assistantButton.IsCheckedChanged += (_, _) => ShowAssistant(assistantButton.IsChecked == true);

        bar.Children.Add(assistantButton);

        return new Border
        {
            Background = new SolidColorBrush(Colours.Toolbar),
            BorderBrush = new SolidColorBrush(Colours.Edge),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = bar,
        };
    }

    private Control BuildStatusBar()
    {
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Margin = new Thickness(12, 5),
        };

        backend.Text = sound.Output is { } output ? $"sound: {output.Name}" : "sound: none";
        ToolTip.SetTip(backend, PluginSummary());

        // Always visible, so the capability's existence is never a surprise to
        // somebody who did not go looking for it.
        helper.Text = assistant?.Summary ?? "assistant: none";
        ToolTip.SetTip(helper, PluginSummary());

        bar.Children.Add(status);
        bar.Children.Add(backend);
        bar.Children.Add(helper);
        bar.Children.Add(issues);

        return new Border
        {
            Background = new SolidColorBrush(Colours.Panel),
            BorderBrush = new SolidColorBrush(Colours.Edge),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = bar,
        };
    }

    private Control BuildPalette()
    {
        filter.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) FillPalette();
        };

        // Escape empties it, which is the quickest way back to the whole list
        // once you have found the module you were after.
        filter.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;

            filter.Text = string.Empty;
            e.Handled = true;
        };

        var ticks = new StackPanel { Spacing = 2, Margin = new Thickness(4) };

        foreach (var provider in Providers)
        {
            var id = provider.Id;
            var tick = new CheckBox { Content = provider.Name, IsChecked = true, FontSize = 12 };

            tick.IsCheckedChanged += (_, _) =>
            {
                if (tick.IsChecked == true) hidden.Remove(id); else hidden.Add(id);

                DescribeSources();
                FillPalette();
            };

            ticks.Children.Add(tick);
        }

        sources.Flyout = new Flyout { Content = ticks, Placement = PlacementMode.BottomEdgeAlignedLeft };

        DescribeSources();
        FillPalette();

        var header = new StackPanel { Spacing = 6, Margin = new Thickness(10, 8, 10, 0) };
        header.Children.Add(filter);

        // With nothing installed there is only the engine's own entry, and a
        // dropdown that can only say "all" or "the only one" is noise.
        if (Providers.Count > 1) header.Children.Add(sources);

        // The header stays put while the list beneath it scrolls; a filter that
        // scrolled away with the results would be the wrong way round.
        var panel = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);

        panel.Children.Add(header);
        panel.Children.Add(new ScrollViewer { Content = modules });

        return new Border
        {
            Background = new SolidColorBrush(Colours.Panel),
            Child = panel,
        };
    }

    /// <summary>
    /// Rebuilds the module list for whatever is in the filter box. Rebuilding
    /// rather than hiding buttons keeps the category headings honest: a heading
    /// with nothing under it is worse than no heading.
    /// </summary>
    private void FillPalette()
    {
        var text = filter.Text?.Trim() ?? string.Empty;
        var matches = NodeCatalog.All.Where(d => Matches(d, text)).ToList();

        modules.Children.Clear();

        if (matches.Count == 0)
        {
            modules.Children.Add(Hint(
                hidden.Count == Providers.Count ? "No plugins are ticked."
                : text.Length == 0 ? "Nothing to show."
                : $"Nothing matches “{text}”."));
            return;
        }

        if (text.Length == 0 && hidden.Count == 0)
            modules.Children.Add(Hint("Click to drop a module into the middle of the patch."));

        foreach (var category in matches.Select(d => d.Category).Distinct())
        {
            modules.Children.Add(new TextBlock
            {
                Text = category.ToUpperInvariant(),
                FontSize = 10.5,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Colours.Accent(category)),
                Margin = new Thickness(2, 12, 2, 4),
            });

            foreach (var def in matches.Where(d => d.Category == category))
            {
                var button = new Button
                {
                    Content = def.Name,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(8, 4),
                    FontSize = 12,
                };

                // Naming the plugin only where it is not the engine keeps the
                // built-in modules reading as they always did, and tells you
                // which patches will need something installed to open.
                var from = plugins.Modules.ProviderOf(def.TypeId);
                var origin = from is null || from.Id == NodeCatalog.BuiltInProvider.Id
                    ? string.Empty
                    : $"{Environment.NewLine}{Environment.NewLine}From {from.Name} ({from.Id})";

                var tip = def.Description + origin;
                if (tip.Length > 0) ToolTip.SetTip(button, tip);

                var typeId = def.TypeId;
                button.Click += (_, _) => editor.AddNode(typeId);
                modules.Children.Add(button);
            }
        }
    }

    /// <summary>Every provider the installed catalogue knows, the engine's own first.</summary>
    private IReadOnlyList<ModuleProvider> Providers => plugins.Modules.Providers;

    /// <summary>Says what the ticks add up to, so the state is readable without opening them.</summary>
    private void DescribeSources()
    {
        var showing = Providers.Count - hidden.Count;

        sources.Content = showing switch
        {
            _ when hidden.Count == 0 => "All modules  ▾",
            0 => "No plugins  ▾",
            1 => $"{Providers.First(p => !hidden.Contains(p.Id)).Name}  ▾",
            _ => $"{showing} of {Providers.Count} plugins  ▾",
        };
    }

    /// <summary>
    /// Text matches name, category and type id, but deliberately not the
    /// description: every module has a sentence of prose, and matching it turns a
    /// search for a common word into most of the catalogue. The ticks narrow
    /// separately, so the two combine rather than compete.
    /// </summary>
    private bool Matches(NodeDef def, string text)
    {
        // The Output is never listed. Every patch has one already and cannot
        // have a second, so a button for it could only ever select the one that
        // is there — and a palette entry that never adds anything is a puzzle.
        if (NodeCatalog.IsSink(def.TypeId)) return false;

        if (plugins.Modules.ProviderOf(def.TypeId) is { } from && hidden.Contains(from.Id)) return false;

        return text.Length == 0
            || def.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
            || def.Category.Contains(text, StringComparison.OrdinalIgnoreCase)
            || def.TypeId.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 11,
        Opacity = 0.55,
        Margin = new Thickness(2, 8, 2, 0),
    };

    private Control BuildRightPanel()
    {
        var grid = new Grid
        {
            RowDefinitions =
            [
                new RowDefinition(new GridLength(1, GridUnitType.Star)) { MinHeight = 140 },
                new RowDefinition(GridLength.Auto),
                new RowDefinition(new GridLength(1.1, GridUnitType.Star)) { MinHeight = 120 },
            ],
        };

        var previewBox = new Border
        {
            Background = Brushes.Black,
            Child = preview,
        };
        Grid.SetRow(previewBox, 0);

        var splitter = new GridSplitter { Background = Brushes.Transparent, Height = 5 };
        Grid.SetRow(splitter, 1);

        // The mark sits behind the inspector rather than beside it, and never
        // takes a click — an empty panel is a better place for it than a corner
        // of the toolbar, and it is out of the way once there is something to read.
        var inspectorBorder = new Border
        {
            Background = new SolidColorBrush(Colours.Panel),
            Child = new Panel
            {
                Children =
                {
                    watermark,

                    // Explicitly transparent: a theme that gave the scroll
                    // viewer a background would paint straight over the mark.
                    new ScrollViewer { Content = inspector, Background = Brushes.Transparent },
                },
            },
        };
        Grid.SetRow(inspectorBorder, 2);

        grid.Children.Add(previewBox);
        grid.Children.Add(splitter);
        grid.Children.Add(inspectorBorder);

        return grid;
    }

    // --- inspector -------------------------------------------------------------

    /// <summary>
    /// Rebuilt whenever selection changes. The canvas handles patching; exact
    /// numbers are easier to set with real controls than by dragging on a knob.
    /// </summary>
    private void BuildInspector()
    {
        inspector.Children.Clear();

        var selected = editor.SelectedNode is { } chosen && NodeCatalog.Get(chosen.TypeId) is not null;

        // Louder with nothing in front of it, faint once there are values to
        // read. A watermark that competed with a column of sliders would be a
        // decoration in the way of the thing it decorates.
        watermark.Opacity = selected ? 0.06 : 0.14;

        if (editor.SelectedNode is not { } node || NodeCatalog.Get(node.TypeId) is not { } def)
        {
            inspector.Children.Add(new TextBlock
            {
                // The Output is named first because everything that used to be
                // along the top of the window is now behind it, and a setting
                // nobody can find is worse than one in the wrong place.
                Text = "Select a module to edit its values.\n\n"
                     + "Select the Output for the preview size, the renderer, "
                     + "sound, and saving a frame or a clip.\n\n"
                     + "Drag from a socket to patch it into another.\n"
                     + "Drag a connected input to unplug it.\n"
                     + "Right-drag or drag the background to pan, wheel to zoom.\n"
                     + "Delete removes the selected module, F frames the patch.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.5,
                FontSize = 12,
            });
            return;
        }

        inspector.Children.Add(new TextBlock
        {
            Text = def.Name,
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
        });

        inspector.Children.Add(new TextBlock
        {
            Text = def.Category,
            FontSize = 11,
            Foreground = new SolidColorBrush(Colours.Accent(def.Category)),
        });

        if (!string.IsNullOrEmpty(def.Description))
            inspector.Children.Add(new TextBlock
            {
                Text = def.Description,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.6,
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 6),
            });

        for (var i = 0; i < def.Inputs.Count; i++)
            inspector.Children.Add(BuildInputRow(node, def.Inputs[i], i));

        // A sequencer's tune is a list rather than a row of knobs (ADR-0038),
        // so it is edited as one — added to, taken from and reordered.
        if (def.DefaultSteps is not null)
            inspector.Children.Add(new StepList(node, def, because => editor.NotifyPatchChanged(because)).View);

        if (def.Inputs.Count == 0 && def.DefaultSteps is null)
            inspector.Children.Add(new TextBlock
            {
                Text = "This module has nothing to set — it only produces.",
                Opacity = 0.5,
                FontSize = 12,
            });

        // Everything about seeing and hearing the patch hangs off the one module
        // that does both, instead of being spread along a toolbar. Nothing here
        // is saved with the patch — a preview size is a property of the machine
        // you are working on, not of the instrument — but this is where you go
        // looking for it, because this is the block it acts on.
        if (NodeCatalog.IsSink(node.TypeId))
        {
            inspector.Children.Add(outputSettings);
            return;
        }

        var delete = new Button
        {
            Content = "Delete module",
            Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        delete.Click += (_, _) => editor.DeleteSelected();
        inspector.Children.Add(delete);
    }

    /// <summary>
    /// The screen-and-speakers half of the Output's panel: what the picture is
    /// rendered at and by, and the four ways of getting either of them out.
    /// </summary>
    /// <remarks>
    /// Built once and kept, not rebuilt per selection like the knob rows above
    /// it. These controls hold live state, and a control may have one parent at
    /// a time — putting <see cref="resolution"/> into a freshly made row on
    /// every selection would leave it owned by the row before.
    /// </remarks>
    private void BuildOutputSettings()
    {
        outputSettings.Children.Add(Heading("Picture"));
        outputSettings.Children.Add(Field("Size", resolution));
        outputSettings.Children.Add(Field("Render", gpuButton));
        outputSettings.Children.Add(exportFrame);

        outputSettings.Children.Add(Heading("Sound"));
        outputSettings.Children.Add(audioButton);

        outputSettings.Children.Add(Heading("Export"));

        // The seconds box with its unit beside it, so the number is not a bare
        // one on a panel where every other number is in patch units.
        outputSettings.Children.Add(Field("Length", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { length, Label("seconds") },
        }));

        outputSettings.Children.Add(exportButton);
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = 10.5,
        FontWeight = FontWeight.SemiBold,
        Opacity = 0.6,
        Margin = new Thickness(0, 10, 0, 2),
    };

    /// <summary>A labelled row on the same 78-pixel gutter the knob rows use.</summary>
    private static Control Field(string name, Control control)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("78,*") };

        var label = new TextBlock
        {
            Text = name,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetColumn(label, 0);
        Grid.SetColumn(control, 1);

        row.Children.Add(label);
        row.Children.Add(control);

        return row;
    }

    private Control BuildInputRow(NodeInstance node, PortSpec spec, int index)
    {
        var connected = editor.Patch.IncomingTo(node.Id, index) is not null;

        var label = new TextBlock
        {
            Text = spec.Name,
            Width = 78,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        // A note knob gets a column for the name the number stands for, since
        // "57" is not what anyone means by the note they are picking. A count
        // needs no such column — the number is already what it stands for — but
        // it lands on whole numbers for the same reason a note does.
        var named = spec.Display == PortDisplay.Note;
        var whole = spec.Stepped;

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions(named ? "78,*,84,40" : "78,*,84") };
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        if (connected)
        {
            var wired = new TextBlock
            {
                Text = "◀ patched",
                FontSize = 12,
                Opacity = 0.55,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(wired, 1);
            Grid.SetColumnSpan(wired, named ? 3 : 2);
            row.Children.Add(wired);
            return row;
        }

        var value = index < node.InputValues.Length ? node.InputValues[index] : spec.Default;

        var slider = new Slider
        {
            // Widen the range if a saved value sits outside the module's usual span.
            Minimum = Math.Min(spec.Min, value),
            Maximum = Math.Max(spec.Max, value),
            Value = value,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0),

            // Dragging a note or a count lands on whole numbers. The module
            // quantises whatever it is given anyway, so a slider that stopped
            // between two would only be showing a distinction the patch does
            // not have.
            IsSnapToTickEnabled = whole,
            TickFrequency = 1,
        };

        var numeric = new NumericUpDown
        {
            Value = (decimal)value,
            Increment = whole ? 1m : 0.05m,
            FormatString = whole ? "0.##" : "0.###",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            ShowButtonSpinner = false,
        };

        var name = new TextBlock
        {
            Text = spec.Format(value),
            FontSize = 12,
            Opacity = 0.75,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var updating = false;

        void Apply(float next)
        {
            if (updating) return;

            updating = true;
            if (index < node.InputValues.Length) node.InputValues[index] = next;
            slider.Value = next;
            numeric.Value = (decimal)next;
            name.Text = spec.Format(next);
            updating = false;

            // Named after the socket, so a slider dragged across its range is one
            // step to undo rather than one per frame of the drag.
            editor.NotifyPatchChanged($"{node.Id} input {index}");
        }

        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && e.NewValue is double d)
                Apply((float)d);
        };

        numeric.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } d) Apply((float)d);
        };

        Grid.SetColumn(slider, 1);
        Grid.SetColumn(numeric, 2);
        row.Children.Add(slider);
        row.Children.Add(numeric);

        if (named)
        {
            Grid.SetColumn(name, 3);
            row.Children.Add(name);
        }

        return row;
    }

    // --- plugins -----------------------------------------------------------------

    /// <summary>The device that was opened, and what it came from — null when nothing could play.</summary>
    private sealed record AudioSetup(IAudioDevice Device, IAudioOutput? Output, string? Failure);

    /// <summary>
    /// Opens the best backend the plugins offered. A machine with no sound
    /// plugin, or one whose device refuses to open, gets silence and a disabled
    /// button — never a program that will not start.
    /// </summary>
    private static AudioSetup OpenAudio(PluginCatalog plugins)
    {
        if (plugins.PreferredAudioOutput is not { } output)
            return new AudioSetup(new SilentAudioDevice(), null, null);

        try
        {
            return new AudioSetup(output.Create(AudioFormat.Default), output, null);
        }
        catch (Exception ex)
        {
            return new AudioSetup(new SilentAudioDevice(), null, $"{output.Name} — {ex.Message}");
        }
    }

    /// <summary>
    /// What loaded and what did not. This is the only place a plugin failure is
    /// reported, so it says where the folder is even when it is empty.
    /// </summary>
    private string PluginSummary()
    {
        var lines = new List<string> { plugins.Plugins.Count == 0 ? "No plugins loaded." : "Loaded:" };

        lines.AddRange(plugins.Plugins.Select(p => $"    {p.Info.Name}  ({p.Info.Id})"));

        // Module providers are worth naming separately: they are what a saved
        // patch records, and what another machine would have to install.
        var providers = plugins.Modules.Providers.Where(p => p.Id != NodeCatalog.BuiltInProvider.Id).ToList();

        if (providers.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Modules from:");
            lines.AddRange(providers.Select(p => $"    {p.Name}  ({p.Id})"));
        }

        if (sound.Failure is { } failure)
            lines.Add($"Could not open sound: {failure}");

        // Where a key would be kept, but never what it is. An assistant with no
        // store behind it still works; it just forgets between runs, and saying
        // so here is the difference between that and appearing to have saved one.
        if (plugins.Assistants.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add(plugins.PreferredSecretStore is { } store
                ? $"Keys are kept by: {store.Name}"
                : "No secret store is installed, so a key lasts only as long as the window.");
        }

        lines.AddRange(plugins.Problems.Select(p => $"Problem: {p}"));

        lines.Add(string.Empty);
        lines.Add(PluginHost.DefaultDirectory);

        return string.Join(Environment.NewLine, lines);
    }

    // --- compile and status -----------------------------------------------------

    /// <summary>
    /// One patch, one program per sink. The audio program is compiled even when
    /// sound is off, so switching it on is instant and the status line can show
    /// what the ear would cost.
    /// </summary>
    private void Recompile()
    {
        var result = editor.Patch.CompileForVideo();
        preview.Program = result.Program;
        audio.Update(editor.Patch);

        // What the ear reaches is said too. Compiling backwards from one sink
        // means the video pass never visits a module only the speakers reach —
        // and stops at the first line when there is no screen at all — so a
        // patch built for sound had nothing said about it, however wrong it was.
        var said = result.Issues
            .Concat(editor.Patch.CompileForAudio().Issues)
            .Select(i => i.Message)
            .Distinct()
            .ToArray();

        Report(said.Length > 0 ? string.Join("  •  ", said) : string.Empty);

        MarkExportable();
    }

    /// <summary>
    /// Greys the export out while there is nothing to write, which is the same
    /// question the dialog would have asked a moment later — better answered on
    /// the button than by a file picker with nothing in its list.
    /// </summary>
    /// <remarks>
    /// An export already running keeps it enabled whatever the patch says: the
    /// button is <c>Stop</c> by then, and editing the patch mid-render must not
    /// take away the only way to abandon it.
    /// </remarks>
    private void MarkExportable()
    {
        var kinds = ExportKinds(editor.Patch);

        exportButton.IsEnabled = export is not null || kinds.Count > 0;

        ToolTip.SetTip(exportButton, kinds.Count > 0 ? ExportTip : NothingToExport);
    }

    private static readonly string ExportTip =
        "Write the patch to a file, for as long as Length says. Pick AVI for the picture "
        + $"— Motion JPEG at {MovieRenderer.DefaultFrameRate:0} frames a second, at whatever "
        + "Size says, with the sound alongside it — or WAV for the sound on its own.";

    private const string NothingToExport =
        "Nothing is wired into the Output, so there is nothing to write. "
        + "Patch something into its 'colour' or its 'left'.";

    /// <summary>
    /// The one place anything is said to the user. <paramref name="detail"/> is
    /// for what will not fit on a status bar — a list of missing plugins, say —
    /// and is cleared along with the line, so nothing stale hangs off it.
    /// </summary>
    private void Report(string message, string? detail = null)
    {
        issues.Text = message;
        ToolTip.SetTip(issues, string.IsNullOrEmpty(detail) ? null : detail);
    }

    private void SetAudioEnabled(bool enabled)
    {
        audioButton.Content = enabled ? "Audio on" : "Audio off";

        if (enabled)
        {
            audio.Update(editor.Patch);

            try
            {
                audio.Start();
            }
            catch (Exception ex)
            {
                // A device is only really opened here, so this is where a card
                // that is busy, unplugged or missing its library says so. The
                // button goes back off and stays off: whatever it is will not
                // have fixed itself by the next click, and ADR-0025 promises
                // that nothing a plugin does takes the shell down.
                Report($"Sound could not start — {ex.Message}", PluginSummary());
                audioButton.IsEnabled = false;
                audioButton.IsChecked = false;
                return;
            }

            // Sound cannot stretch, so it leads and the picture follows.
            preview.Clock = () => audio.Time;
        }
        else
        {
            preview.Clock = null;
            audio.Stop();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        audio.Dispose();
        base.OnClosed(e);
    }

    private void UpdateStatus()
    {
        var nodes = editor.Patch.Nodes.Count;
        var wires = editor.Patch.Connections.Count;
        var ops = preview.Program.Ops.Length;
        var ms = preview.FrameMilliseconds;
        var size = preview.Resolution;

        // The loop is capped at ~60 Hz, so report the cost of a frame rather
        // than a frame rate the timer would never let you observe. Which renderer
        // produced the number is part of what it means, so it is said alongside.
        status.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{nodes} modules · {wires} wires · {ops} ops   |   t = {preview.Time:0.00}s   |   {ms:0.0} ms to render {size.Width} × {size.Height} on the {preview.BackendName}");
    }

    // --- files -------------------------------------------------------------------

    private async Task OpenPatchAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open patch",
            AllowMultiple = false,
            FileTypeFilter = [PatchFileType],
        });

        if (files.Count == 0) return;

        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            var loaded = PatchIo.Read(await reader.ReadToEndAsync());

            // A patch short of a module would open with holes in it and compile
            // to something that is not what was saved. Better to refuse it and
            // say what is missing, leaving what is open where it was.
            if (!loaded.IsComplete)
            {
                Report($"Not opened. {loaded.Summary}", loaded.Detail);
                return;
            }

            editor.Patch = loaded.Patch;
            preview.Rewind();
        }
        catch (Exception ex)
        {
            Report($"Could not open patch: {ex.Message}");
        }
    }

    /// <returns>Whether a file was written. A cancelled picker is not one.</returns>
    private async Task<bool> SavePatchAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save patch",
            SuggestedFileName = "patch",
            DefaultExtension = PatchIo.FileExtension,
            FileTypeChoices = [PatchFileType],
        });

        if (file is null) return false;

        try
        {
            await using (var stream = await file.OpenWriteAsync())
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(PatchIo.ToJson(editor.Patch));
            }

            // Only once it is actually on disk. A patch that failed to write is
            // still a patch with everything to lose.
            editor.MarkSaved();
            return true;
        }
        catch (Exception ex)
        {
            Report($"Could not save patch: {ex.Message}");
            return false;
        }
    }

    private async Task SaveFrameAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save frame",
            SuggestedFileName = "frame",
            DefaultExtension = "png",
            FileTypeChoices = [FilePickerFileTypes.ImagePng],
        });

        if (file is null) return;

        try
        {
            var path = file.TryGetLocalPath();
            if (path is null)
            {
                Report("That location can't be written to directly.");
                return;
            }

            await PreviewSurface.SaveFrameAsync(preview.Program, preview.Time, path, ExportSize);
        }
        catch (Exception ex)
        {
            Report($"Could not save frame: {ex.Message}");
        }
    }


    /// <summary>What a patch has to offer, and therefore what it can be written to.</summary>
    /// <remarks>
    /// The Output is always there, so its presence says nothing — what matters
    /// is whether anything reaches each half of it. A picture nothing draws is a
    /// black rectangle and a track nothing feeds is silence, and neither is
    /// worth a file.
    /// </remarks>
    private static (bool Picture, bool Sound) Reaches(Patch patch)
    {
        var output = patch.Output.Id;

        return (
            patch.IncomingTo(output, NodeCatalog.OutputColourPort) is not null,
            patch.IncomingTo(output, NodeCatalog.OutputLeftPort) is not null
            || patch.IncomingTo(output, NodeCatalog.OutputRightPort) is not null);
    }

    private static FilePickerFileType Avi => new("AVI video") { Patterns = ["*.avi"] };

    private static FilePickerFileType Wav => new("WAV audio") { Patterns = ["*.wav"] };

    /// <summary>
    /// The kinds of file this patch could be written to, in the order the dialog
    /// should offer them.
    /// </summary>
    /// <remarks>
    /// Video first when there is one, because an AVI carries the sound too and
    /// is therefore the whole of what the patch does. A patch that draws nothing
    /// is offered no AVI and one that makes no sound is offered no WAV, so the
    /// dialog can never produce a file that is only a black rectangle or only
    /// silence. Empty means there is nothing to write at all.
    /// </remarks>
    internal static IReadOnlyList<FilePickerFileType> ExportKinds(Patch patch)
    {
        var (picture, sound) = Reaches(patch);

        return (picture, sound) switch
        {
            (true, true) => [Avi, Wav],
            (true, false) => [Avi],
            (false, true) => [Wav],
            _ => [],
        };
    }

    /// <summary>
    /// Writes the patch to a file. One button and one dialog for both kinds,
    /// because which kind you want is the same decision as what to call it —
    /// and the dialog offers only the kinds this patch actually has, so a silent
    /// patch is never offered a WAV of silence.
    /// </summary>
    /// <remarks>
    /// Unlike every other export here a video takes long enough to watch, so it
    /// reports as it goes and can be stopped: rendering a minute of an expensive
    /// patch is minutes of work, and a program that merely appears to have hung
    /// during it is not acceptable.
    /// </remarks>
    private async Task ExportAsync()
    {
        var patch = editor.Patch;
        var kinds = ExportKinds(patch);

        if (kinds.Count == 0)
        {
            Report("Nothing is wired into the Output, so there is nothing to write.");
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export",
            SuggestedFileName = "flyback",

            // The first kind offered is the one the patch is most fully
            // described by, so it is also the extension a name gets by default.
            DefaultExtension = kinds[0] == Wav ? "wav" : "avi",
            FileTypeChoices = [.. kinds],
        });

        if (file is null) return;

        var path = file.TryGetLocalPath();
        if (path is null)
        {
            Report("That location can't be written to directly.");
            return;
        }

        // The name decides, because the name is what the person actually chose
        // — a dialog's selected filter is not carried back on every platform,
        // and the extension is.
        if (Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase))
            await ExportSoundAsync(patch, path);
        else
            await ExportPictureAsync(patch, path);
    }

    private async Task ExportSoundAsync(Patch patch, string path)
    {
        var seconds = ExportSeconds;

        try
        {
            await Task.Run(() => RenderAudioFile(patch, path, seconds));
            Report($"Wrote {seconds:0.0}s to {Path.GetFileName(path)}.");
        }
        catch (Exception ex)
        {
            Report($"Could not render audio: {ex.Message}");
        }
    }

    private async Task ExportPictureAsync(Patch patch, string path)
    {
        // Everything the background pass needs, taken while the patch is still
        // sitting still. An export is a picture of the patch as it is now, so
        // editing during one changes the next export and not this one.
        var size = preview.Resolution;
        var seconds = ExportSeconds;
        var settings = new MovieSettings(size.Width, size.Height, seconds);

        var video = patch.CompileForVideo().Program;
        var sound = Reaches(patch).Sound ? patch.CompileForAudio().Program : null;
        var scan = AudioScanFor(patch);

        using var stopping = new CancellationTokenSource();
        export = stopping;
        exportButton.Content = "Stop";
        length.IsEnabled = false;

        var progress = new Progress<double>(done => Report(
            $"Exporting {seconds:0}s at {size.Width} × {size.Height} — {done:P0}"));

        try
        {
            var written = await Task.Run(
                () => MovieRenderer.Render(path, video, sound, scan, settings, progress, stopping.Token),
                stopping.Token);

            var duration = written / settings.FramesPerSecond;

            Report(written < settings.FrameCount
                ? $"Stopped — kept the {duration:0.0}s already rendered in {Path.GetFileName(path)}."
                : $"Wrote {duration:0.0}s to {Path.GetFileName(path)}.");
        }
        catch (Exception ex)
        {
            Report($"Could not export video: {ex.Message}");
        }
        finally
        {
            export = null;
            exportButton.Content = "Export…";
            length.IsEnabled = true;

            // The patch may have been edited while this ran, so what there is
            // to write is asked again rather than assumed to be what it was.
            MarkExportable();
        }
    }

    /// <summary>The length control, as the number an export actually wants.</summary>
    private double ExportSeconds => (double)(length.Value ?? 10m);

    /// <summary>
    /// Renders offline through a fresh renderer, so exporting never disturbs the
    /// cursor or filter state of whatever is currently playing.
    /// </summary>
    private static void RenderAudioFile(Patch patch, string path, double seconds)
    {
        var program = patch.CompileForAudio().Program;
        var renderer = new AudioRenderer();
        var frames = (int)Math.Round(renderer.SampleRate * seconds);
        var buffer = new float[frames * NodeCatalog.AudioChannels];
        renderer.Render(program, buffer, AudioScanFor(patch));

        WavWriter.Write(path, buffer, renderer.SampleRate, NodeCatalog.AudioChannels);
    }

    private static AudioScan AudioScanFor(Patch patch)
    {
        var sink = patch.FirstOf(NodeCatalog.OutputTypeId);
        var def = NodeCatalog.Get(NodeCatalog.OutputTypeId);
        if (sink is null || def is null) return AudioScan.TimeDriven;

        return new AudioScan(Knob("scan") >= 0.5f, MathF.Max(Knob("scan rate"), 1f), 16f / 9f);

        float Knob(string name)
        {
            for (var i = 0; i < def.Inputs.Count; i++)
                if (def.Inputs[i].Name == name)
                    return i < sink.InputValues.Length ? sink.InputValues[i] : def.Inputs[i].Default;
            return 0f;
        }
    }

    private static FilePickerFileType PatchFileType => new("Flyback patch")
    {
        Patterns = [$"*.{PatchIo.FileExtension}"],
    };

    // --- small helpers -------------------------------------------------------------

    // --- unsaved work ---------------------------------------------------------

    /// <summary>What to do about a patch that has been edited and not written out.</summary>
    private enum Unsaved
    {
        /// <summary>Refused, and whatever asked should not go ahead.</summary>
        Cancel,

        Save,

        Discard,
    }

    /// <summary>
    /// Whether the thing about to replace or close the patch may go ahead. Asks
    /// only when there is something to lose, so every caller can front its own
    /// action with this and none of them has to know whether anything was
    /// edited.
    /// </summary>
    private async Task<bool> MayReplaceThePatchAsync()
    {
        if (!editor.IsModified) return true;

        return await AskAboutUnsavedAsync() switch
        {
            // A cancelled save picker is a cancelled close: somebody who asked
            // to save and then thought better of where has not agreed to lose
            // the patch, and the safe reading of that is to stay put.
            Unsaved.Save => await SavePatchAsync(),
            Unsaved.Discard => true,
            _ => false,
        };
    }

    /// <summary>
    /// The three answers, as a window rather than as a system message box —
    /// there is no such thing here, and one built by hand is the same three
    /// buttons in the same palette as the rest of the shell.
    /// </summary>
    /// <remarks>
    /// Closing it by its own frame is Cancel, which is the answer that loses
    /// nothing. That is why Cancel is the enum's default as well: an answer
    /// nobody gave should never be the destructive one.
    /// </remarks>
    private async Task<Unsaved> AskAboutUnsavedAsync()
    {
        var answer = Unsaved.Cancel;

        var dialog = new Window
        {
            Title = "Unsaved changes",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Colours.Panel),
        };

        Button Answering(string text, Unsaved with, bool wide = false)
        {
            var button = new Button { Content = text, MinWidth = wide ? 120 : 96 };

            button.Click += (_, _) =>
            {
                answer = with;
                dialog.Close();
            };

            return button;
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        buttons.Children.Add(Answering("Save…", Unsaved.Save));
        buttons.Children.Add(Answering("Discard changes", Unsaved.Discard, wide: true));
        buttons.Children.Add(Answering("Cancel", Unsaved.Cancel));

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            MaxWidth = 420,
            Children =
            {
                new TextBlock
                {
                    Text = "This patch has changes that have not been saved. "
                        + "Closing it now would lose them.",
                    TextWrapping = TextWrapping.Wrap,
                },
                buttons,
            },
        };

        await dialog.ShowDialog(this);

        return answer;
    }

    /// <summary>
    /// Nothing may block inside a closing handler, so a window with unsaved work
    /// in it cancels the close, asks, and closes itself again on the way back.
    /// </summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (e.Cancel || leaving || !editor.IsModified) return;

        e.Cancel = true;

        if (!await MayReplaceThePatchAsync()) return;

        leaving = true;
        Close();
    }

    /// <summary>

    /// Undo and redo, from wherever the focus happens to be. Handled on the
    /// window rather than on the canvas because an edit is as likely to have
    /// been made in the inspector as on it, and a shortcut that worked only
    /// while the canvas had the focus would be one somebody learns not to
    /// trust. Anything that already dealt with the key keeps it — a text box
    /// undoing its own typing is doing the same job at its own scale.
    /// </summary>
    /// <remarks>
    /// Command as well as Control, so the shortcut is the one the machine uses:
    /// Ctrl+Z on Windows and Linux, Cmd+Z on a Mac. Both are accepted
    /// everywhere rather than asked which platform this is, since neither is a
    /// gesture anything else here claims.
    /// </remarks>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled) return;
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0) return;

        var again = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        switch (e.Key)
        {
            case Key.Z:
                if (again) editor.Redo();
                else editor.Undo();
                e.Handled = true;
                break;

            // The other half of the convention Windows carries: Ctrl+Y is redo
            // where Ctrl+Shift+Z is, and somebody who reaches for one is not
            // going to enjoy discovering which this program wanted.
            case Key.Y:
                editor.Redo();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Greys the two out when there is nothing behind or ahead — the same
    /// question a button would answer by doing nothing, asked where it can be
    /// seen instead — and says in the title whether there is unsaved work.
    /// </summary>
    private void RefreshEditState()
    {
        undoButton.IsEnabled = editor.CanUndo;
        redoButton.IsEnabled = editor.CanRedo;

        // A dot rather than the word, because the title bar is read at a glance
        // and the question it answers is only whether there is anything to lose.
        Title = editor.IsModified ? BaseTitle + " •" : BaseTitle;
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
        Background = new SolidColorBrush(Colours.Separator),
    };
}
