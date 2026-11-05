using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Editor.Controls;
using Flyback.Editor.Settings;
using Flyback.Editor.Shell;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Canvas;

/// <summary>
/// The Canvas section of the settings window: how modules are laid out, how much of
/// a plugin's own module background the canvas draws (ADR-0118), whether the
/// window has a menu bar, whether the toolbar's buttons are labeled, and the colors
/// the editor is drawn in.
/// </summary>
internal sealed class CanvasSection : ISettingsSection
{
    private readonly CheckBox toolbarLabels = new()
    {
        Name = "toolbarLabels",
        Content = "Label the toolbar buttons",
        FontSize = Text.Body,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly CheckBox compactModules = new()
    {
        Name = "compactModules",
        Content = "Compact modules",
        FontSize = Text.Body,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly CheckBox dragToPan = new()
    {
        Name = "dragToPan",
        Content = "Drag empty canvas to pan",
        FontSize = Text.Body,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly CheckBox pluginSkins = new()
    {
        Name = "pluginSkins",
        Content = "Let a plugin paint its own modules",
        FontSize = Text.Body,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly CheckBox animateSkins = new()
    {
        Name = "animateSkins",
        Content = "Play animated module backgrounds",
        FontSize = Text.Body,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly CheckBox menuBar = new()
    {
        Name = "showMenuBar",
        Content = "Show a menu bar",
        FontSize = Text.Body,
        VerticalAlignment = VerticalAlignment.Center,
        IsVisible = false,
    };

    private readonly ComboBox measureWindow = new Picker
    {
        Name = "measureWindow",
        ItemsSource = CanvasSettings.MeasureWindows.Select(seconds => $"{seconds} s").ToList(),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private readonly ComboBox colorScheme = new Picker
    {
        Name = "colorScheme",
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>Where the user's own color schemes are read from, or null for the shipped ones alone.</summary>
    private readonly string? schemeFolder;

    /// <summary>The names the Colors list offers, read afresh each time the window opens.</summary>
    private List<string> schemes = [];

    private readonly ComboBox measurePicture = new Picker
    {
        Name = "measurePicture",
        ItemsSource = Enum.GetNames<MeasurePictureSize>().ToList(),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>What the section was last saved as, and so what closing without Save puts it back to.</summary>
    private CanvasSettings saved;

    /// <summary>Where <see cref="saved"/> is kept, or null to keep it nowhere.</summary>
    private readonly string? path;

    private readonly NodeEditor canvas;

    /// <summary>Where <see cref="saved"/> is kept in a page, which has no settings file.</summary>
    private readonly IBrowserStore browser;

    private readonly Action<string, string?> report;

    /// <param name="canvas">The canvas, redrawn whenever what it draws changes.</param>
    public CanvasSection(EditorFolders folders, NodeEditor canvas, ReportLine report, IBrowserStore browser, EditorHost host)
    {
        path = folders.SettingsPath;
        schemeFolder = folders.SchemeFolder;
        this.canvas = canvas;
        this.browser = browser;
        this.report = (message, detail) => report.Say(message, detail);

        saved = path is not null ? CanvasSettings.Load(path) : CanvasSettings.Parse(browser.Read(CanvasSettings.Section));

        canvas.Gestures.DragToPan = saved.DragToPan;
        canvas.Gestures.ScrollPans = host.ScrollPans;

        ToolTip.SetTip(compactModules,
            "Put each input beside an output on one row, and show a knob's value when "
            + "its socket is hovered rather than on the row. Tidy spaces modules for the size they are drawn at.");

        ToolTip.SetTip(dragToPan,
            "Drag empty canvas with the left button to move the view. The right button then "
            + "selects: drag it across modules, or click it to add one. For a mouse or trackpad without a middle "
            + "button; cleared, the middle button pans and the left one selects.");

        ToolTip.SetTip(pluginSkins,
            "A plugin may give its modules a color, a texture or a picture of their own. "
            + "Clear this to draw every module as its category, the way Flyback's own are drawn. Only the "
            + "background changes: the shape, header, sockets and description stay Flyback's, and a plugin may "
            + "ask for its text to be worked out from a pale background.");

        ToolTip.SetTip(animateSkins,
            "A module whose background is an animated GIF plays it. Clear this to hold every "
            + "one at its first frame — the canvas then redraws only when the patch changes. A module's author "
            + "may have asked for a still picture already, and clearing this cannot put that back.");

        ToolTip.SetTip(menuBar,
            "File, Edit, View, Play and Help along the top of the window, the menus a Mac shows in its menu bar.");

        ToolTip.SetTip(toolbarLabels,
            "Print each toolbar button's name under its symbol. Cleared, the bar is lower "
            + "and a button says what it does when the pointer rests on it.");

        ToolTip.SetTip(colorScheme,
            "Phosphor is the green of the Beam module's trace on blue-gray. Amber is amber on "
            + "teal-black, Graphite orange on charcoal, Midnight orange on violet-black, Paper "
            + "ballpoint blue on cream. A scheme "
            + "of your own is a .json file in the schemes folder beside the settings.");

        Schemes(report: false);

        View.Children.Add(SettingRows.Heading("Look"));
        View.Children.Add(InspectorRows.Field("Colors", colorScheme));
        View.Children.Add(Note("The editor is drawn in the new colors the next time it starts."));
        View.Children.Add(menuBar);
        View.Children.Add(toolbarLabels);
        View.Children.Add(compactModules);
        View.Children.Add(Note("A patch tidied compact may overlap once this is cleared."));
        // On a device every press is a finger's, which never drags to pan.
        if (!host.OnDevice)
        {
            View.Children.Add(SettingRows.Heading("Mouse"));
            View.Children.Add(dragToPan);
        }
        View.Children.Add(SettingRows.Heading("Plugin modules"));
        View.Children.Add(pluginSkins);
        View.Children.Add(animateSkins);
        View.Children.Add(SettingRows.Heading("Measure"));

        ToolTip.SetTip(measureWindow,
            "How long Measure runs the patch for, from the playhead. Longer catches a slow LFO's turns "
            + "and a delay filling; it only takes longer.");

        View.Children.Add(InspectorRows.Field("Measure for", measureWindow));

        ToolTip.SetTip(measurePicture,
            "How finely Measure looks at the picture: Small is a 32 by 18 grid, Medium 64 by 36, "
            + "Large 128 by 72. Larger shows more detail; it only takes longer.");

        View.Children.Add(InspectorRows.Field("Grid", measurePicture));

        Show();
    }

    public string Name => "Canvas";

    public string Group => SettingsGroups.Making;

    internal StackPanel View { get; } = new() { Spacing = 10, Width = SettingsSession.SectionWidth };

    Control ISettingsSection.View => View;

    /// <summary>Whether the menu bar is offered, which it is only where a window can show one.</summary>
    internal bool MenuBarOffered
    {
        get => menuBar.IsVisible;
        set => menuBar.IsVisible = value;
    }

    /// <summary>Whether the window has a menu bar, as last saved.</summary>
    internal bool MenuBar => saved.MenuBar;

    /// <summary>Raised with <see cref="MenuBar"/> whenever what was saved is put in force.</summary>
    internal event Action<bool>? MenuBarShown;

    /// <summary>Whether the toolbar's buttons are labeled, as last saved.</summary>
    internal bool ToolbarLabels => saved.ToolbarLabels;

    /// <summary>Raised with <see cref="ToolbarLabels"/> whenever what was saved is put in force.</summary>
    internal event Action<bool>? ToolbarLabelsShown;

    /// <summary>
    /// Puts what was last saved on the controls and on the canvas both, since what
    /// the canvas draws is read from <see cref="ModuleSkins"/> and its <see cref="NodeGeometry"/> rather than from here.
    /// </summary>
    public void Show()
    {
        menuBar.IsChecked = saved.MenuBar;
        toolbarLabels.IsChecked = saved.ToolbarLabels;
        compactModules.IsChecked = saved.CompactModules;
        dragToPan.IsChecked = saved.DragToPan;
        pluginSkins.IsChecked = saved.PluginSkins;
        animateSkins.IsChecked = saved.AnimateSkins;
        measureWindow.SelectedIndex = Math.Max(0, CanvasSettings.MeasureWindows.ToList().IndexOf(saved.MeasureSeconds));

        measurePicture.SelectedIndex = (int)saved.MeasurePicture;
        colorScheme.SelectedIndex = Math.Max(0, schemes.FindIndex(name => string.Equals(name, saved.ColorScheme, StringComparison.OrdinalIgnoreCase)));

        canvas.Geometry.Compact = saved.CompactModules;
        MenuBarShown?.Invoke(saved.MenuBar);
        ToolbarLabelsShown?.Invoke(saved.ToolbarLabels);

        // The empty inspector names the gestures, so it is told when they change.
        if (canvas.Gestures.DragToPan != saved.DragToPan)
        {
            canvas.Gestures.DragToPan = saved.DragToPan;
            canvas.Selection.Announce();
        }

        ModuleSkins.Honored = saved.PluginSkins;
        ModuleSkins.Animated = saved.AnimateSkins;

        canvas.InvalidateVisual();
    }

    /// <summary>The text editor's font size, kept beside the switches but changed by the editor itself.</summary>
    /// <summary>The text view's font size, as last saved; the view reads it when built.</summary>
    internal double EditorFontSize => saved.EditorFontSize;

    /// <summary>Saves the editor's font size on its own, leaving the switches as last saved.</summary>
    internal void SaveEditorFontSize(double size)
    {
        saved.EditorFontSize = size;
        Write();
    }

    /// <summary>Whether the left button pans empty canvas, as last saved.</summary>
    internal bool DragToPan => saved.DragToPan;

    /// <summary>Whether modules are drawn compact, as last saved.</summary>
    internal bool CompactModules => saved.CompactModules;

    /// <summary>Changes a setting and saves it at once, for a page's panel that has no Save.</summary>
    internal void Change(Action<CanvasSettings> change)
    {
        change(saved);
        Show();
        Write();
    }

    /// <summary>A scheme picked and not yet saved, other than the one drawing the editor now, waits for a restart.</summary>
    public string? Restarts =>
        colorScheme.SelectedItem is string picked
        && !string.Equals(picked, saved.ColorScheme, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(picked, Flyback.Ui.Controls.Colors.Current.Name, StringComparison.OrdinalIgnoreCase)
            ? "use the new colors"
            : null;

    /// <summary>The name of the color scheme the editor starts in next time, as last saved.</summary>
    internal string ColorScheme => saved.ColorScheme;

    /// <summary>Looks again for schemes, so one put in the folder since is offered.</summary>
    public void Opening()
    {
        Schemes(report: true);
        Show();
    }

    /// <summary>Lists the shipped schemes and the folder's, and says what was wrong with any file there.</summary>
    private void Schemes(bool report)
    {
        var library = SchemeLibrary.Load(schemeFolder);

        schemes = [.. library.Schemes.Select(scheme => scheme.Name)];
        colorScheme.ItemsSource = schemes;

        if (report && library.Problems.Count > 0)
            this.report($"A color scheme in the schemes folder has a problem: {library.Problems[0]}", string.Join('\n', library.Problems));
    }

    /// <summary>How long Measure runs the patch for, as last saved.</summary>
    internal double MeasureSeconds => saved.MeasureSeconds;

    /// <summary>The grid Measure draws the picture on, as last saved.</summary>
    internal (int Columns, int Rows) MeasureGrid => CanvasSettings.MeasureGrid(saved.MeasurePicture);

    /// <summary>Whether the seek bar loops, kept beside the switches but set on the bar.</summary>
    internal bool SeekLoop => saved.SeekLoop;

    /// <summary>Saves whether the seek bar loops, leaving the switches as last saved.</summary>
    internal void SaveSeekLoop(bool loop)
    {
        saved.SeekLoop = loop;
        Write();
    }

    /// <summary>Whether a patch with no length plays on, as last saved.</summary>
    internal bool Endless => saved.Endless;

    /// <summary>Saves whether a patch with no length plays on, leaving the switches as last saved.</summary>
    internal void SaveEndless(bool endless)
    {
        saved.Endless = endless;
        Write();
    }

    public void Save()
    {
        saved = new CanvasSettings
        {
            MenuBar = menuBar.IsChecked == true,
            ToolbarLabels = toolbarLabels.IsChecked == true,
            CompactModules = compactModules.IsChecked == true,
            DragToPan = dragToPan.IsChecked == true,
            PluginSkins = pluginSkins.IsChecked == true,
            AnimateSkins = animateSkins.IsChecked == true,
            EditorFontSize = saved.EditorFontSize,
            SeekLoop = saved.SeekLoop,
            Endless = saved.Endless,
            MeasureSeconds = CanvasSettings.MeasureWindows[Math.Max(measureWindow.SelectedIndex, 0)],
            MeasurePicture = (MeasurePictureSize)Math.Max(measurePicture.SelectedIndex, 0),
            ColorScheme = colorScheme.SelectedItem as string ?? CanvasSettings.DefaultColorScheme,
        };

        Show();
        Write();
    }

    private void Write()
    {
        if (path is null)
        {
            browser.Write(CanvasSettings.Section, saved.Json());
            return;
        }

        try
        {
            saved.Save(path);
        }
        catch (Exception ex)
        {
            report($"Could not save the canvas settings: {ex.Message}", path);
        }
    }

    /// <summary>A quiet line under a row, for a consequence the row's tip alone would hide.</summary>
    private static TextBlock Note(string text) => new()
    {
        Text = text,
        FontSize = Text.Small,
        Foreground = Text.Muted,
        TextWrapping = TextWrapping.Wrap,
    };
}
