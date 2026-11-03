using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Flyback.Editor.Canvas;
using Flyback.Editor.Composition;
using Flyback.Editor.Controls;
using Flyback.Editor.Hubs;
using Flyback.Editor.Notices;
using Flyback.Editor.Settings;
using Flyback.Editor.Shell;
using Flyback.Engine.Graph;
using Shouldly;
using Xunit;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Tests.Settings;

/// <summary>A setting that takes effect only at a start offers to start Flyback again.</summary>
public sealed class RestartOfferTests : EditorTest
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "flyback-restart-" + Guid.NewGuid().ToString("N"));

    private readonly List<Reopen?> relaunched = [];

    public override void Dispose()
    {
        base.Dispose();

        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private MainWindow Open(bool canRestart = true)
    {
        var window = NewMainWindow(new EditorSetup
        {
            Folders = new() { SettingsPath = Path.Combine(folder, "settings.json") },
            Host = new() { Relaunch = canRestart ? relaunched.Add : null },
        });

        window.Show();
        Settle(window);

        return window;
    }

    /// <summary>Picks a color scheme other than the one drawing the editor.</summary>
    private static string PickOtherColors(ModalOverlay dialog)
    {
        var colors = All<ComboBox>(dialog).Single(c => c.Name == "colorScheme");
        var other = colors.Items.OfType<string>().First(name => !string.Equals(name, Colors.Current.Name, StringComparison.OrdinalIgnoreCase));

        colors.SelectedItem = other;

        return other;
    }

    private static CheckBox Restart(ModalOverlay dialog) => All<CheckBox>(dialog).Single(c => c.Name == "restart");

    [AvaloniaFact]
    public void Settings_as_they_were_offer_no_restart()
    {
        var dialog = OpenSettings(Open(), "Canvas");

        Restart(dialog).IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void New_colors_offer_a_ticked_restart_named_for_them()
    {
        var window = Open();
        var dialog = OpenSettings(window, "Canvas");

        PickOtherColors(dialog);

        Restart(dialog).IsVisible.ShouldBeTrue();
        Restart(dialog).IsChecked.ShouldBe(true, "the colors change nothing until Flyback starts again");
        Restart(dialog).Content.ShouldBe("Restart Flyback to use the new colors");
        Shoot(window, "restart-offer-desktop.png");
    }

    [AvaloniaFact]
    public void On_a_phone_the_offer_sits_above_Save()
    {
        var window = Open();
        window.Width = 390;
        window.Height = 844;
        Settle(window);

        var dialog = OpenSettings(window, "Canvas");
        PickOtherColors(dialog);
        Settle(window);

        var offer = All<RestartOffer>(dialog).Single();

        offer.IsVisible.ShouldBeTrue();
        DockPanel.GetDock(offer).ShouldBe(Dock.Top);
        Shoot(window, "restart-offer-phone.png");
    }

    [AvaloniaFact]
    public void Picking_the_colors_drawing_now_takes_the_offer_down()
    {
        var dialog = OpenSettings(Open(), "Canvas");

        PickOtherColors(dialog);
        All<ComboBox>(dialog).Single(c => c.Name == "colorScheme").SelectedItem = Colors.Current.Name;

        Restart(dialog).IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Saving_with_restart_ticked_starts_Flyback_again_and_closes_this_window()
    {
        var window = Open();
        var dialog = OpenSettings(window, "Canvas");
        var picked = PickOtherColors(dialog);

        CloseSettings(window, dialog);
        Pump(() => relaunched.Count > 0, window);

        relaunched.Count.ShouldBe(1);
        window.IsVisible.ShouldBeFalse();
        CanvasSettings.Load(Path.Combine(folder, "settings.json")).ColorScheme.ShouldBe(picked, "saved before the restart reads it");
    }

    [AvaloniaFact]
    public void A_restart_opens_the_patch_file_on_screen_again()
    {
        var window = Open();
        var path = Path.Combine(folder, $"on screen.{PatchIO.FileExtension}");

        Directory.CreateDirectory(folder);
        var saving = Service<PatchFiles>(window).SavePatchFileAsync(StorageFiles.OnDisk(path));
        Pump(() => saving.IsCompleted, window);
        saving.Result.ShouldBeTrue();

        var dialog = OpenSettings(window, "Canvas");
        PickOtherColors(dialog);
        CloseSettings(window, dialog);
        Pump(() => relaunched.Count > 0, window);

        relaunched.Single().ShouldBe(new Reopen(Path: path));
    }

    [AvaloniaFact]
    public void Saving_with_restart_unticked_keeps_the_window_and_the_colors()
    {
        var window = Open();
        var dialog = OpenSettings(window, "Canvas");
        var picked = PickOtherColors(dialog);

        Restart(dialog).IsChecked = false;
        CloseSettings(window, dialog);

        relaunched.ShouldBeEmpty();
        window.IsVisible.ShouldBeTrue();
        Service<CanvasSection>(window).ColorScheme.ShouldBe(picked);
    }

    /// <summary>Colors saved without a restart are not offered again with every later save.</summary>
    [AvaloniaFact]
    public void Colors_saved_without_a_restart_are_not_offered_again()
    {
        var window = Open();
        var dialog = OpenSettings(window, "Canvas");

        PickOtherColors(dialog);
        Restart(dialog).IsChecked = false;
        CloseSettings(window, dialog);

        Restart(OpenSettings(window, "Canvas")).IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_window_that_cannot_restart_offers_no_restart()
    {
        var dialog = OpenSettings(Open(canRestart: false), "Canvas");

        PickOtherColors(dialog);

        Restart(dialog).IsVisible.ShouldBeFalse();
        All<TextBlock>(dialog).Single(t => t.Name == "restartNote").IsVisible.ShouldBe(OperatingSystem.IsIOS());
    }

    [AvaloniaFact]
    public void Two_changes_are_named_in_one_offer()
    {
        RestartOffer.Joined(["use the new colors", null, "draw with Direct3D"]).ShouldBe("use the new colors and draw with Direct3D");
        RestartOffer.Joined([null, null]).ShouldBeNull();
    }

    [AvaloniaFact]
    public void A_device_at_another_rate_asks_to_restart_now()
    {
        var window = Open();

        Service<Reactions>(window).Raise(new DeviceRateDiffers());
        Pump(() => All<Control>(window).Any(c => c.Name == "restartQuestion"), window);
        Shoot(window, "restart-question.png");

        Press(Named<Button>(window, "restartNow"));
        Pump(() => relaunched.Count > 0, window);

        relaunched.Count.ShouldBe(1);
    }

    [AvaloniaFact]
    public void A_device_at_another_rate_restarts_nothing_until_asked()
    {
        var window = Open();

        Service<Reactions>(window).Raise(new DeviceRateDiffers());
        Pump(() => All<Control>(window).Any(c => c.Name == "restartQuestion"), window);

        Press(Named<Button>(window, "later"));
        Pump(() => !All<Control>(window).Any(c => c.Name == "restartQuestion"), window);

        relaunched.ShouldBeEmpty();
        window.IsVisible.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void A_window_that_cannot_restart_asks_nothing_of_a_device_at_another_rate()
    {
        var window = Open(canRestart: false);

        Service<Reactions>(window).Raise(new DeviceRateDiffers());
        Settle(window);

        All<Control>(window).ShouldNotContain(c => c.Name == "restartQuestion");
    }
}
