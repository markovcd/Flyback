using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.App.Audio;
using Flyback.App.Controls;
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

    private readonly ComboBox resolution = new()
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
    private readonly SampleLibrary samples = new();
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

    private readonly StackPanel inspector = new() { Margin = new Thickness(12), Spacing = 8 };
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock issues = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = new SolidColorBrush(Colors.Attention),
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

    private readonly ToggleButton audioButton = new() { Content = "Audio off", Width = 92 };
    private readonly ToggleButton gpuButton = new() { Content = "GPU", Width = 60 };
    private readonly ToggleButton assistantButton =
        Toggle("assistant", "✦", "Describe a patch and have one built.");

    private readonly Button undoButton = Glyph("undo", "↶", "Take back the last edit  (Ctrl+Z)");
    private readonly Button redoButton = Glyph("redo", "↷", "Put it back  (Ctrl+Shift+Z)");

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
            Report,
            samples: samples)
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

        Grid.SetRow(editor, 0);
        Grid.SetRow(assistantSplitter, 1);
        Grid.SetRow(assistant, 2);

        canvas.Children.Add(editor);
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

        var open = Drawn("open", Glyphs.Open(), "Open a patch…");
        open.Click += async (_, _) =>
        {
            if (await MayReplaceThePatchAsync()) await OpenPatchAsync();
        };

        var save = Drawn("save", Glyphs.Save(), "Save this patch…");
        save.Click += async (_, _) => await SavePatchAsync();

        undoButton.Click += (_, _) => editor.Undo();
        redoButton.Click += (_, _) => editor.Redo();

        var tidy = Drawn("tidy", Glyphs.Tidy(), "Lay the modules out so the patch reads left to right  (Ctrl+L)");
        tidy.Click += (_, _) => editor.Tidy();

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
    /// Named as well, because a test can no longer find a button by reading it —
    /// and a glyph is a poor thing to write an assertion against.
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
