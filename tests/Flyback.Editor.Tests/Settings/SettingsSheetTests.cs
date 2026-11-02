using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Flyback.Editor.Composition;
using Flyback.Editor.Settings;
using Shouldly;

namespace Flyback.Editor.Tests.Settings;

/// <summary>
/// The settings on a window as narrow as a phone: a list of the tabs to tap into, each
/// tab then alone and full width, rather than a sheet wider than the screen.
/// </summary>
public class SettingsSheetTests : OutputSettingsTest
{
    [AvaloniaFact]
    public void A_wide_window_shows_the_list_beside_the_tab()
    {
        var window = Open();
        var dialog = OpenSettings(window);

        All<SettingsSheet>(dialog).Single().Narrow.ShouldBeFalse();
        Named<ScrollViewer>(dialog, "settingsList").IsEffectivelyVisible.ShouldBeFalse();
        Tabs(dialog).Items.Cast<TabItem>().ShouldAllBe(item => item.IsVisible);
    }

    [AvaloniaFact]
    public void A_phone_lists_the_tabs_and_opens_one_full_width()
    {
        var window = Phone();
        var dialog = OpenSettings(window);

        All<SettingsSheet>(dialog).Single().Narrow.ShouldBeTrue();
        Tabs(dialog).IsVisible.ShouldBeFalse("the list of tabs comes first");
        Rows(dialog).Select(Said).ShouldBe(["Picture", "Full screen", "Sound", "Recording", "Playing", "Canvas", "Files", "Assistant", "Decisions", "Privacy"]);

        Shoot(window, "settings-phone-list.png");

        var frame = All<Border>(dialog).Single(b => b.Name == "dialog");
        frame.Bounds.Width.ShouldBeLessThanOrEqualTo(window.Bounds.Width, "the sheet fits the screen");

        Press(Rows(dialog).Single(row => Said(row) == "Playing"));
        Settle(window);

        Tabs(dialog).IsVisible.ShouldBeTrue();
        ((Tabs(dialog).SelectedItem as TabItem)?.Header as TextBlock)?.Text.ShouldBe("Playing");
        Named<Button>(dialog, "settingsBack").IsEffectivelyVisible.ShouldBeTrue();
        Shoot(window, "settings-phone-page.png");
        Named<CheckBox>(dialog, "endless").Bounds.Right.ShouldBeLessThanOrEqualTo(frame.Bounds.Width, "a tab runs to the sheet's width, not past it");

        Press(Named<Button>(dialog, "settingsBack"));
        Settle(window);

        Tabs(dialog).IsVisible.ShouldBeFalse("back is the list again");
        Named<ScrollViewer>(dialog, "settingsList").IsEffectivelyVisible.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void A_phone_saves_every_tab_as_the_desktop_does()
    {
        var window = Phone(settingsPath);
        var dialog = OpenSettings(window);

        Press(Rows(dialog).Single(row => Said(row) == "Picture"));
        Settle(window);
        Size(dialog).SelectedIndex = 1;
        CloseSettings(window, dialog);

        var next = Open(settingsPath);

        Size(OpenSettings(next)).SelectedIndex.ShouldBe(1);
    }

    private MainWindow Phone(string? path = null)
    {
        var window = Open(path);

        window.Width = 390;
        window.Height = 800;
        Settle(window);

        return window;
    }

    private static List<Button> Rows(Visual dialog) => [.. All<Button>(Named<ScrollViewer>(dialog, "settingsList")).Where(b => b.Name == "settingsRow")];

    private static string Said(Button row) => All<TextBlock>(row).Last().Text!;
}
