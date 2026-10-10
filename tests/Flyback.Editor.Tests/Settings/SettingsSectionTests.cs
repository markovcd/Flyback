using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Flyback.Editor.Controls;
using Flyback.Editor.Windows;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Settings;

/// <summary>
/// Every settings tab, one rule for all of them: a row turned and saved is read back the
/// same on the next launch. A row added to a tab without being saved, or saved without being
/// read, fails here without a test written for it.
/// </summary>
public sealed class SettingsSectionTests : EditorTest
{
    private readonly string settingsPath = Path.Combine(
        Path.GetTempPath(),
        "flyback-sections-" + Guid.NewGuid().ToString("N"),
        "settings.json");

    /// <summary>The tabs the settings window has, in its order; a new section is added here.</summary>
    public static TheoryData<string> Tabs =>
        ["Picture", "Sound", "MIDI", "Recording", "Canvas", "Files", "Assistant", "Decisions", "Privacy"];

    public override void Dispose()
    {
        base.Dispose();

        var folder = Path.GetDirectoryName(settingsPath);

        if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    [AvaloniaFact]
    public void Every_section_is_a_tab_this_test_knows()
    {
        var window = Open();
        var dialog = OpenSettings(window);

        Headers(dialog).ShouldBe(Tabs.Select(row => row.Data));
    }

    [AvaloniaTheory]
    [MemberData(nameof(Tabs))]
    public void Every_row_turned_and_saved_is_read_back_on_the_next_launch(string tab)
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window, tab);
        var turned = new Dictionary<string, object?>();

        foreach (var box in All<CheckBox>(Shown(dialog)).Where(Drivable).ToList())
        {
            box.IsChecked = box.IsChecked != true;
            turned[box.Name!] = box.IsChecked;
        }

        foreach (var pick in All<ComboBox>(Shown(dialog)).Where(Drivable).Where(p => p.ItemCount > 1).ToList())
        {
            pick.SelectedIndex = (pick.SelectedIndex + 1) % pick.ItemCount;
            turned[pick.Name!] = pick.SelectedIndex;
        }

        Settle(window);
        CloseSettings(window, dialog);

        var next = Open(settingsPath);
        var again = OpenSettings(next, tab);
        var reopened = Shown(again);

        foreach (var (name, value) in turned)
        {
            object? now = Named<Control>(reopened, name) switch
            {
                CheckBox box => box.IsChecked,
                ComboBox pick => pick.SelectedIndex,
                var other => other,
            };

            now.ShouldBe(value, $"{tab}: {name}");
        }

        CloseSettings(next, again, "cancel");
    }

    private MainWindow Open(string? path = null)
    {
        var window = NewMainWindow(new EditorSetup { Folders = new() { SettingsPath = path } });

        window.Show();
        Settle(window);

        return window;
    }

    /// <summary>Pickers that choose what a tab shows rather than a setting: the Decisions tab's For, which picks the use its rows are about.</summary>
    private static readonly HashSet<string> Selectors = ["decisionUse"];

    /// <summary>A row a person can turn, and a test with it: named, shown, not greyed out and a setting.</summary>
    private static bool Drivable(Control control) =>
        control.Name is not null && !Selectors.Contains(control.Name) && control.IsEffectivelyEnabled && control.IsEffectivelyVisible;

    private static TabControl TabsOf(ModalOverlay dialog) => Named<TabControl>(dialog, "settingsTabs");

    private static IEnumerable<string> Headers(ModalOverlay dialog) =>
        TabsOf(dialog).Items.OfType<TabItem>().Select(item => ((TextBlock)item.Header!).Text!);

    /// <summary>The section's controls, as the tab shows them.</summary>
    private static Control Shown(ModalOverlay dialog) => (Control)((TabItem)TabsOf(dialog).SelectedItem!).Content!;
}
