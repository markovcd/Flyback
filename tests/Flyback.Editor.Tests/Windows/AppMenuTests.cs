using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Core;
using Flyback.Editor.Composition;
using Flyback.Editor.Controls;
using Flyback.Editor.Windows;
using Shouldly;

namespace Flyback.Editor.Tests.Windows;

/// <summary>
/// The menu macOS shows under the program's name: each item does what the toolbar
/// button of the same name does, and nothing while a dialog is up.
/// </summary>
public sealed class AppMenuTests : EditorTest
{
    private static NativeMenuItem Item(AppMenu menu, string header) =>
        Items(menu).Single(i => i.Header == header);

    private static IEnumerable<NativeMenuItem> Items(AppMenu menu) =>
        menu.Menu.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator);

    // The way the platform's menu exporter clicks an item; NativeMenuItem has no other.
    // ReSharper disable once CSharpWarnings::CS0618
    private static void Choose(AppMenu menu, string header) =>
        ((INativeMenuItemExporterEventsImplBridge)Item(menu, header)).RaiseClicked();

    private (AppMenu Menu, MainWindow Window) Served()
    {
        var window = Open();
        var menu = new AppMenu();

        menu.Serve(Service<IServiceProvider>(window));

        return (menu, window);
    }

    private static bool Says(Window window, string text) =>
        All<TextBlock>(window).Any(t => t.Text == text);

    [AvaloniaFact]
    public void It_names_the_program_and_offers_the_settings_on_the_mac_key()
    {
        var menu = new AppMenu();

        Items(menu).Select(i => i.Header)
            .ShouldBe([$"About {GlobalConstants.ApplicationName}", "Settings…", "Plugins…"]);

        Item(menu, "Settings…").Gesture.ShouldBe(new KeyGesture(Key.OemComma, KeyModifiers.Meta));
    }

    [AvaloniaFact]
    public void About_shows_the_about_window()
    {
        var (menu, window) = Served();

        Choose(menu, $"About {GlobalConstants.ApplicationName}");
        Pump(() => All<ModalOverlay>(window).Any(), window);
        Settle(window);

        Says(window, About.Description).ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Settings_opens_the_settings()
    {
        var (menu, window) = Served();

        Choose(menu, "Settings…");
        Pump(() => All<ModalOverlay>(window).Any(), window);
        Settle(window);

        All<TabControl>(window).ShouldContain(t => t.Name == "settingsTabs");
    }

    [AvaloniaFact]
    public void Nothing_opens_over_a_dialog_already_up()
    {
        var (menu, window) = Served();

        Choose(menu, $"About {GlobalConstants.ApplicationName}");
        Pump(() => All<ModalOverlay>(window).Any(), window);

        Choose(menu, "Settings…");
        Settle(window);

        All<ModalOverlay>(window).Count().ShouldBe(1);
        All<TabControl>(window).ShouldNotContain(t => t.Name == "settingsTabs");
    }

    /// <summary>macOS can be asked before the editor exists, between the menu bar appearing and the window.</summary>
    [AvaloniaFact]
    public void Before_the_editor_is_built_an_item_does_nothing()
    {
        var menu = new AppMenu();

        Should.NotThrow(() => Choose(menu, "Settings…"));
    }
}
