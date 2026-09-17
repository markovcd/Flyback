using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.App.Controls;
using Flyback.App.Updates;

namespace Flyback.App;

public sealed partial class MainWindow
{
    /// <summary>The Updates section of the settings window: whether Flyback keeps itself up to date (ADR-0088).</summary>
    private readonly StackPanel updatesSection = new() { Spacing = 10, Width = 280 };

    private readonly CheckBox checkForUpdates = new()
    {
        Name = "checkForUpdates",
        Content = "Keep Flyback up to date",
        FontSize = Text.Body,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>What the Updates section was last saved as, and so what closing without Save puts it back to.</summary>
    private UpdateSettings updateSettings = new();

    /// <summary>Where <see cref="updateSettings"/> is kept, or null to keep it nowhere.</summary>
    private readonly string? updateSettingsPath;

    /// <remarks>
    /// Saving takes effect at the next start, which is the only time either half of
    /// an update happens — so switching it off never interrupts a download already
    /// under way, but does stop what it downloaded from being installed.
    /// </remarks>
    private void BuildUpdatesSection()
    {
        ToolTip.SetTip(checkForUpdates,
            "Look for a new release when Flyback starts, download it in the background, "
            + "and install it the next time Flyback starts.");

        updatesSection.Children.Add(checkForUpdates);

        updatesSection.Children.Add(new TextBlock
        {
            Text = "Flyback asks GitHub for the latest release each time it starts. A newer one is "
                + "downloaded while you work and installed the next time Flyback starts, and only "
                + "if it is signed with Flyback's release key. Nothing about you or your patches "
                + "is sent.",
            FontSize = Text.Small,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
        });

        updatesSection.Children.Add(new TextBlock
        {
            Text = $"This is version {About.Version}.",
            FontSize = Text.Small,
            Foreground = Text.Muted,
        });
    }

    private void ShowUpdateSettings(UpdateSettings settings) =>
        checkForUpdates.IsChecked = settings.CheckForUpdates;

    private void SaveUpdateSettings()
    {
        updateSettings = new UpdateSettings { CheckForUpdates = checkForUpdates.IsChecked == true };

        if (updateSettingsPath is null) return;

        try
        {
            updateSettings.Save(updateSettingsPath);
        }
        catch (Exception ex)
        {
            Report($"Could not save the update settings: {ex.Message}", updateSettingsPath);
        }
    }
}
