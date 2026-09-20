using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.App.Controls;

namespace Flyback.App;

public sealed partial class MainWindow
{
    /// <summary>
    /// The Canvas section of the settings window: how much of a plugin's own
    /// module background the canvas draws (ADR-0118).
    /// </summary>
    private readonly StackPanel canvasSection = new() { Spacing = 10, Width = 280 };

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

    /// <summary>What the Canvas section was last saved as, and so what closing without Save puts it back to.</summary>
    private CanvasSettings canvasSettings = new();

    /// <summary>Where <see cref="canvasSettings"/> is kept, or null to keep it nowhere.</summary>
    private readonly string? canvasSettingsPath;

    private void BuildCanvasSection()
    {
        ToolTip.SetTip(pluginSkins,
            "A plugin may give its modules a color, a texture or a picture of their own. "
            + "Clear this to draw every module as its category, the way Flyback's own are drawn.");

        ToolTip.SetTip(animateSkins,
            "A module whose background is an animated GIF plays it. Clear this to hold every "
            + "one at its first frame — the canvas then redraws only when the patch changes.");

        canvasSection.Children.Add(pluginSkins);

        canvasSection.Children.Add(new TextBlock
        {
            Text = "A module's shape, its header, its sockets and its description are the same "
                + "whatever a plugin says; only the background behind it changes. A plugin may "
                + "also ask for the text on its module to be worked out from that background "
                + "rather than drawn white, which is what keeps a title readable on a pale one.",
            FontSize = Text.Small,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
        });

        canvasSection.Children.Add(animateSkins);

        canvasSection.Children.Add(new TextBlock
        {
            Text = "A module's author may have asked for a still picture already, and clearing "
                + "this cannot put that back.",
            FontSize = Text.Small,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
        });
    }

    /// <summary>
    /// Puts the section on the controls and on the canvas both, since what the
    /// canvas draws is read from <see cref="ModuleSkins"/> rather than from here.
    /// </summary>
    private void ShowCanvasSettings(CanvasSettings settings)
    {
        pluginSkins.IsChecked = settings.PluginSkins;
        animateSkins.IsChecked = settings.AnimateSkins;

        ModuleSkins.Honored = settings.PluginSkins;
        ModuleSkins.Animated = settings.AnimateSkins;

        editor.InvalidateVisual();
    }

    private void SaveCanvasSettings()
    {
        canvasSettings = new CanvasSettings
        {
            PluginSkins = pluginSkins.IsChecked == true,
            AnimateSkins = animateSkins.IsChecked == true,
        };

        ShowCanvasSettings(canvasSettings);

        if (canvasSettingsPath is null) return;

        try
        {
            canvasSettings.Save(canvasSettingsPath);
        }
        catch (Exception ex)
        {
            Report($"Could not save the canvas settings: {ex.Message}", canvasSettingsPath);
        }
    }
}
