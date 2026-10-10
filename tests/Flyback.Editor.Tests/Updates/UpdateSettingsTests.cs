using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Flyback.Editor.Controls;
using Flyback.Editor.Updates;
using Flyback.Editor.Windows;
using Shouldly;

namespace Flyback.Editor.Tests.Updates;

/// <summary>
/// The update switch on the settings window's Privacy tab, and the line the window opens with after
/// an update was installed (ADR-0088).
/// </summary>
public sealed class UpdateSettingsTests : EditorTest
{
    private readonly string settingsPath = Path.Combine(
        Path.GetTempPath(),
        "flyback-update-settings-" + Guid.NewGuid().ToString("N"),
        "settings.json");

    private const string PrivacyTab = "Privacy";

    public override void Dispose()
    {
        base.Dispose();

        var folder = Path.GetDirectoryName(settingsPath);

        if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private MainWindow Open(string? settingsPath = null, string? note = null, ReleaseNotes? whatsNew = null)
    {
        var window = NewMainWindow(new EditorSetup { Folders = new() { SettingsPath = settingsPath }, Launch = new() { OpeningNote = note, WhatsNew = whatsNew } });

        window.Show();
        Settle(window);
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    private static CheckBox Switch(ModalOverlay dialog) =>
        All<CheckBox>(dialog).Single(c => c.Name == "checkForUpdates");

    [AvaloniaFact]
    public void Updates_are_on_until_switched_off()
    {
        var window = Open(settingsPath);

        Switch(OpenSettings(window, PrivacyTab)).IsChecked.ShouldBe(true);
    }

    [AvaloniaFact]
    public void Switching_off_and_saving_keeps_it_off()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window, PrivacyTab);

        Switch(dialog).IsChecked = false;
        CloseSettings(window, dialog);

        UpdateSettings.Load(settingsPath).CheckForUpdates.ShouldBeFalse();
        Switch(OpenSettings(Open(settingsPath), PrivacyTab)).IsChecked.ShouldBe(false, "the next launch reads it back");
    }

    [AvaloniaFact]
    public void Cancel_puts_the_switch_back()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window, PrivacyTab);

        Switch(dialog).IsChecked = false;
        CloseSettings(window, dialog, "cancel");

        File.Exists(settingsPath).ShouldBeFalse();
        Switch(OpenSettings(window, PrivacyTab)).IsChecked.ShouldBe(true);
    }

    [AvaloniaFact]
    public void What_the_last_update_did_is_on_the_status_bar()
    {
        var window = Open(note: "Updated to Flyback 0.4.0.");

        All<ReportLine>(window).Single().History.ShouldContain("Updated to Flyback 0.4.0.");
    }

    [AvaloniaFact]
    public void What_the_release_changed_is_shown_instead()
    {
        var notes = new ReleaseNotes(new Version(0, 4, 0), null, [new("0.4.0 — 2026-09-30", "### Modules\n- Added `Echo`, a delay.")]);
        var window = Open(note: "Updated to Flyback 0.4.0.", whatsNew: notes);
        var dialog = WhatsNewDialog(window);

        All<TextBlock>(dialog).ShouldContain(t => t.Text == "What's new in Flyback 0.4.0");
        All<TextBlock>(dialog).ShouldContain(t => t.Inlines!.Text == "Added Echo, a delay.");
        All<TextBlock>(dialog).ShouldNotContain(t => t.Inlines!.Text == "0.4.0 — 2026-09-30", "the title names the one release");
        All<ReportLine>(window).Single().History.ShouldNotContain("Updated to Flyback 0.4.0.");
    }

    [AvaloniaFact]
    public void Every_release_since_the_one_replaced_is_shown_under_its_own_heading()
    {
        var notes = new ReleaseNotes(new Version(0, 5, 0), new Version(0, 3, 0),
            [new("0.5.0 — 2026-10-14", "- Fifth."), new("0.4.0 — 2026-09-30", "- Fourth.")]);
        var dialog = WhatsNewDialog(Open(note: "Updated to Flyback 0.5.0.", whatsNew: notes));

        All<TextBlock>(dialog).ShouldContain(t => t.Text == "What's new since Flyback 0.3.0");
        var page = All<StackPanel>(dialog).Single(p => p.Name == "whatsNew");

        All<TextBlock>(page)
            .Select(t => t.Inlines?.Text)
            .Where(text => !string.IsNullOrEmpty(text))
            .ShouldBe(["0.5.0 — 2026-10-14", "Fifth.", "0.4.0 — 2026-09-30", "Fourth."]);
    }

    private static ModalOverlay WhatsNewDialog(MainWindow window)
    {
        for (var attempt = 0; attempt < 20 && !All<ModalOverlay>(window).Any(); attempt++)
            Dispatcher.UIThread.RunJobs();

        return All<ModalOverlay>(window).Single();
    }
}
