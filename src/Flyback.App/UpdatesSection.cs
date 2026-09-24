using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.App.Controls;
using Flyback.App.Updates;

namespace Flyback.App;

/// <summary>The Updates section of the settings window: whether Flyback keeps itself up to date (ADR-0088).</summary>
/// <remarks>
/// Saving takes effect at the next start, which is the only time either half of
/// an update happens — so switching it off never interrupts a download already
/// under way, but does stop what it downloaded from being installed.
/// </remarks>
internal sealed class UpdatesSection
{
    private readonly CheckBox checkForUpdates = new()
    {
        Name = "checkForUpdates",
        Content = "Keep Flyback up to date",
        FontSize = Text.Body,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>What the section was last saved as, and so what closing without Save puts it back to.</summary>
    private UpdateSettings saved = new();

    /// <summary>Where <see cref="saved"/> is kept, or null to keep it nowhere.</summary>
    private readonly string? path;

    private readonly Action<string, string?> report;

    public UpdatesSection(EditorSetup setup, ReportLine report)
    {
        path = setup.UpdateSettingsPath;
        this.report = (message, detail) => report.Say(message, detail);

        if (path is not null) saved = UpdateSettings.Load(path);

        ToolTip.SetTip(checkForUpdates,
            "Look for a new release when Flyback starts, download it in the background, "
            + "and install it the next time Flyback starts.");

        View.Children.Add(checkForUpdates);

        View.Children.Add(new TextBlock
        {
            Text = "Flyback asks GitHub for the latest release each time it starts. A newer one is "
                + "downloaded while you work and installed the next time Flyback starts, and only "
                + "if it is signed with Flyback's release key. Nothing about you or your patches "
                + "is sent.",
            FontSize = Text.Small,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
        });

        View.Children.Add(new TextBlock
        {
            Text = $"This is version {About.Version}.",
            FontSize = Text.Small,
            Foreground = Text.Muted,
        });

        Show();
    }

    internal StackPanel View { get; } = new() { Spacing = 10, Width = 280 };

    /// <summary>Puts what was last saved back on the controls.</summary>
    internal void Show() => checkForUpdates.IsChecked = saved.CheckForUpdates;

    internal void Save()
    {
        saved = new UpdateSettings { CheckForUpdates = checkForUpdates.IsChecked == true };

        if (path is null) return;

        try
        {
            saved.Save(path);
        }
        catch (Exception ex)
        {
            report($"Could not save the update settings: {ex.Message}", path);
        }
    }
}
