using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Editor.Controls;
using Flyback.Editor.Inspect;
using Flyback.Editor.Settings;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Canvas;

/// <summary>
/// The Canvas section of the settings window: how modules are laid out, and how
/// much of a plugin's own module background the canvas draws (ADR-0118).
/// </summary>
internal sealed class CanvasSection : ISettingsSection
{
    private readonly CheckBox compactModules = new()
    {
        Name = "compactModules",
        Content = "Compact modules",
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

    private readonly ComboBox measureWindow = new Picker
    {
        Name = "measureWindow",
        ItemsSource = CanvasSettings.MeasureWindows.Select(seconds => $"{seconds} s").ToList(),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private readonly ComboBox measurePicture = new Picker
    {
        Name = "measurePicture",
        ItemsSource = Enum.GetNames<MeasurePictureSize>().ToList(),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>What the section was last saved as, and so what closing without Save puts it back to.</summary>
    private CanvasSettings saved = new();

    /// <summary>Where <see cref="saved"/> is kept, or null to keep it nowhere.</summary>
    private readonly string? path;

    private readonly NodeEditor canvas;

    private readonly Action<string, string?> report;

    /// <param name="canvas">The canvas, redrawn whenever what it draws changes.</param>
    /// <param name="source">The text view, whose font size is this section's setting.</param>
    public CanvasSection(EditorFolders folders, NodeEditor canvas, ReportLine report, SourceView source)
    {
        path = folders.SettingsPath;
        this.canvas = canvas;
        this.report = (message, detail) => report.Say(message, detail);

        if (path is not null) saved = CanvasSettings.Load(path);

        source.EditorFontSize = saved.EditorFontSize;
        source.EditorFontSizeChanged += (_, size) => SaveEditorFontSize(size);

        ToolTip.SetTip(compactModules,
            "Put each input beside an output on one row, and show a knob's value when "
            + "its socket is hovered rather than on the row.");

        ToolTip.SetTip(pluginSkins,
            "A plugin may give its modules a color, a texture or a picture of their own. "
            + "Clear this to draw every module as its category, the way Flyback's own are drawn.");

        ToolTip.SetTip(animateSkins,
            "A module whose background is an animated GIF plays it. Clear this to hold every "
            + "one at its first frame — the canvas then redraws only when the patch changes.");

        View.Children.Add(compactModules);

        View.Children.Add(new TextBlock
        {
            Text = "Tidy spaces modules for the size they are drawn at, so a patch tidied "
                + "compact may overlap once this is cleared.",
            FontSize = Text.Small,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
        });

        View.Children.Add(pluginSkins);

        View.Children.Add(new TextBlock
        {
            Text = "A module's shape, its header, its sockets and its description are the same "
                + "whatever a plugin says; only the background behind it changes. A plugin may "
                + "also ask for the text on its module to be worked out from that background "
                + "rather than drawn white, which is what keeps a title readable on a pale one.",
            FontSize = Text.Small,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
        });

        View.Children.Add(animateSkins);

        View.Children.Add(new TextBlock
        {
            Text = "A module's author may have asked for a still picture already, and clearing "
                + "this cannot put that back.",
            FontSize = Text.Small,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
        });

        ToolTip.SetTip(measureWindow,
            "How long Measure runs the patch for, from the playhead. Longer catches a slow LFO's turns "
            + "and a delay filling; it only takes longer.");

        View.Children.Add(InspectorRows.Field("Measure for", measureWindow));

        ToolTip.SetTip(measurePicture,
            "How finely Measure looks at the picture: Small is a 32 by 18 grid, Medium 64 by 36, "
            + "Large 128 by 72. Larger shows more detail; it only takes longer.");

        View.Children.Add(InspectorRows.Field("Measure grid", measurePicture));

        Show();
    }

    public string Name => "Canvas";

    internal StackPanel View { get; } = new() { Spacing = 10, Width = SettingsSession.SectionWidth };

    Control ISettingsSection.View => View;

    /// <summary>
    /// Puts what was last saved on the controls and on the canvas both, since what
    /// the canvas draws is read from <see cref="ModuleSkins"/> and its <see cref="NodeGeometry"/> rather than from here.
    /// </summary>
    public void Show()
    {
        compactModules.IsChecked = saved.CompactModules;
        pluginSkins.IsChecked = saved.PluginSkins;
        animateSkins.IsChecked = saved.AnimateSkins;
        measureWindow.SelectedIndex = Math.Max(0, CanvasSettings.MeasureWindows.ToList().IndexOf(saved.MeasureSeconds));

        measurePicture.SelectedIndex = (int)saved.MeasurePicture;

        canvas.Geometry.Compact = saved.CompactModules;
        ModuleSkins.Honored = saved.PluginSkins;
        ModuleSkins.Animated = saved.AnimateSkins;

        canvas.InvalidateVisual();
    }

    /// <summary>The text editor's font size, kept beside the switches but changed by the editor itself.</summary>
    internal double EditorFontSize => saved.EditorFontSize;

    /// <summary>Saves the editor's font size on its own, leaving the switches as last saved.</summary>
    internal void SaveEditorFontSize(double size)
    {
        saved.EditorFontSize = size;
        Write();
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

    public void Save()
    {
        saved = new CanvasSettings
        {
            CompactModules = compactModules.IsChecked == true,
            PluginSkins = pluginSkins.IsChecked == true,
            AnimateSkins = animateSkins.IsChecked == true,
            EditorFontSize = saved.EditorFontSize,
            SeekLoop = saved.SeekLoop,
            MeasureSeconds = CanvasSettings.MeasureWindows[Math.Max(measureWindow.SelectedIndex, 0)],
            MeasurePicture = (MeasurePictureSize)Math.Max(measurePicture.SelectedIndex, 0),
        };

        Show();
        Write();
    }

    private void Write()
    {
        if (path is null) return;

        try
        {
            saved.Save(path);
        }
        catch (Exception ex)
        {
            report($"Could not save the canvas settings: {ex.Message}", path);
        }
    }
}
