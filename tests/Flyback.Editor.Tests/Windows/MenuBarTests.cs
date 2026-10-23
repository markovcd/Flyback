using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Editor.Bars;
using Flyback.Editor.Composition;
using Flyback.Editor.Notices;
using Flyback.Editor.Windows;
using Shouldly;

namespace Flyback.Editor.Tests.Windows;

/// <summary>
/// The macOS menu bar: an item with a key does what the key does where the focus is,
/// and a tick follows the toolbar button it stands for.
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

    [AvaloniaFact]
    public void It_has_the_menus_a_mac_program_has()
    {
        var (menu, _) = Opened();

        menu.Menu.Items.OfType<NativeMenuItem>().Select(i => i.Header)
            .ShouldBe(["File", "Edit", "View", "Play", "Window", "Help"]);
    }

    [AvaloniaFact]
    public void An_item_with_a_key_does_what_the_key_does()
    {
        var (menu, window) = Opened();
        var heard = new List<string>();

        using var undo = Service<Reactions>(window).Add<UndoAsked>(_ => heard.Add("undo"));
        using var redo = Service<Reactions>(window).Add<RedoAsked>(_ => heard.Add("redo"));

        Item(menu, "Edit", "Undo").Gesture.ShouldBe(new KeyGesture(Key.Z, KeyModifiers.Meta));
        Item(menu, "Edit", "Redo").Gesture.ShouldBe(new KeyGesture(Key.Z, KeyModifiers.Meta | KeyModifiers.Shift));

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
