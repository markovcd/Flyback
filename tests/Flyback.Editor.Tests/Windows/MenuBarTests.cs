using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Flyback.Editor.Bars;
using Flyback.Editor.Composition;
using Flyback.Editor.Controls;
using Flyback.Editor.Notices;
using Flyback.Editor.Windows;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Windows;

/// <summary>
/// The editor's menus, in the macOS menu bar or along the top of the window: an item with
/// a key does what the key does where the focus is, and a tick follows the toolbar button
/// it stands for.
/// </summary>
public sealed class MenuBarTests : EditorTest
{
    private (MenuBar Menu, MainWindow Window) Opened()
    {
        var window = Open();

        return (Service<MenuBar>(window), window);
    }

    private static NativeMenuItem Item(MenuBar menu, string title, string header) =>
        Menu(menu, title).Items.OfType<NativeMenuItem>().Single(i => i.Header == header);

    private static NativeMenu Menu(MenuBar menu, string title) =>
        menu.Menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == title).Menu!;

    // The way the platform's menu exporter clicks an item; NativeMenuItem has no other.
    // ReSharper disable once CSharpWarnings::CS0618
    private static void Choose(NativeMenuItem item) =>
        ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();

    /// <summary>Window is the Mac's; elsewhere the title bar minimizes and maximizes.</summary>
    [AvaloniaFact]
    public void It_has_the_menus_a_program_has_on_its_platform()
    {
        var (menu, _) = Opened();

        menu.Menu.Items.OfType<NativeMenuItem>().Select(i => i.Header)
            .ShouldBe(OperatingSystem.IsMacOS()
                ? ["File", "Edit", "View", "Play", "Window", "Help"]
                : ["File", "Edit", "View", "Play", "Help"]);
    }

    /// <summary>Off a Mac there is no program menu, so its items join File and Help.</summary>
    [AvaloniaFact]
    public void Off_a_mac_file_and_help_hold_the_program_menu()
    {
        Assert.SkipWhen(OperatingSystem.IsMacOS(), "a Mac keeps these under the program's name");

        var (menu, window) = Opened();

        Menu(menu, "File").Items.OfType<NativeMenuItem>().Select(i => i.Header)
            .ShouldBe(["Open…", "Save…", "-", "Settings…", "Plugins…", "-", OperatingSystem.IsWindows() ? "Exit" : "Quit"]);

        Choose(Item(menu, "File", "Settings…"));
        Pump(() => All<ModalOverlay>(window).Any(), window);

        All<ModalOverlay>(window).ShouldHaveSingleItem();
    }

    [AvaloniaFact]
    public void Off_a_mac_the_bar_is_shown_only_once_the_setting_asks_for_it()
    {
        Assert.SkipWhen(OperatingSystem.IsMacOS(), "a Mac's menus are always in its menu bar");

        var (menu, window) = Opened();
        var bar = Named<NativeMenuBar>(window, "menuBar");

        bar.IsVisible.ShouldBeFalse();
        NativeMenu.GetMenu(window).ShouldBeNull();

        var dialog = OpenSettings(window, "Canvas");
        var box = Named<CheckBox>(dialog, "showMenuBar");

        box.IsVisible.ShouldBeTrue();
        box.IsChecked = true;
        CloseSettings(window, dialog);

        bar.IsVisible.ShouldBeTrue();
        NativeMenu.GetMenu(window).ShouldBeSameAs(menu.Menu);
        All<MenuItem>(bar).Select(i => i.Header).ShouldBe(["File", "Edit", "View", "Play", "Help"]);
    }

    [AvaloniaFact]
    public void An_item_in_the_window_bar_does_what_the_key_does()
    {
        Assert.SkipWhen(OperatingSystem.IsMacOS(), "a Mac's menus are in its menu bar");

        var (menu, window) = Opened();
        var heard = new List<string>();

        using var undo = Service<Reactions>(window).Add<UndoAsked>(_ => heard.Add("undo"));

        menu.Shown = true;
        Settle(window);

        var edit = All<MenuItem>(Named<NativeMenuBar>(window, "menuBar")).Single(i => (string?)i.Header == "Edit");

        edit.Open();
        Settle(window);

        var item = (MenuItem)edit.ContainerFromIndex(0)!;

        item.Header.ShouldBe("Undo");
        item.Focus();
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        heard.ShouldBe(["undo"]);
    }

    [AvaloniaFact]
    public void An_item_with_a_key_does_what_the_key_does()
    {
        var (menu, window) = Opened();
        var heard = new List<string>();

        using var undo = Service<Reactions>(window).Add<UndoAsked>(_ => heard.Add("undo"));
        using var redo = Service<Reactions>(window).Add<RedoAsked>(_ => heard.Add("redo"));

        Item(menu, "Edit", "Undo").Gesture.ShouldBe(new KeyGesture(Key.Z, MenuBar.Command));
        Item(menu, "Edit", "Redo").Gesture.ShouldBe(new KeyGesture(Key.Z, MenuBar.Command | KeyModifiers.Shift));

        Choose(Item(menu, "Edit", "Undo"));
        Choose(Item(menu, "Edit", "Redo"));

        heard.ShouldBe(["undo", "redo"]);
    }

    [AvaloniaFact]
    public void A_view_is_ticked_as_its_toolbar_button_is()
    {
        var (menu, window) = Opened();
        var toolbar = Service<Toolbar>(window);
        var text = Item(menu, "View", "Patch as Text");

        text.IsChecked.ShouldBeFalse();

        Choose(text);
        Settle(window);

        toolbar.Code.IsChecked.ShouldBe(true);
        text.IsChecked.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void An_item_with_no_key_turns_its_toolbar_button()
    {
        var (menu, window) = Opened();
        var transport = Service<Toolbar>(window).Transport;
        var item = Item(menu, "View", "Transport Row");

        item.IsChecked.ShouldBeTrue();

        Choose(item);
        Settle(window);

        transport.IsChecked.ShouldBe(false);
        item.IsChecked.ShouldBeFalse();
    }

    /// <summary>The keys on the screen are on the toolbar only while a MIDI In listens, and so in the menu.</summary>
    [AvaloniaFact]
    public void An_item_is_shown_only_while_its_button_is()
    {
        var (menu, window) = Opened();
        var keys = Service<Toolbar>(window).Keys;
        var item = Item(menu, "View", "Keys on the Screen");

        item.IsVisible.ShouldBe(keys.IsVisible);

        keys.IsVisible = !keys.IsVisible;

        item.IsVisible.ShouldBe(keys.IsVisible);
    }

    [AvaloniaFact]
    public void Tutorials_opens_the_tutorials()
    {
        var (menu, window) = Opened();

        Choose(Item(menu, "Help", "Tutorials"));
        Settle(window);

        Service<StatusBar>(window).Tutorial.IsChecked.ShouldBe(true);
        Item(menu, "Help", "Tutorials").IsChecked.ShouldBeTrue();
    }
}
