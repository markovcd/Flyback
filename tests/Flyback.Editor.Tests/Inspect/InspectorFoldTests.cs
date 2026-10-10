using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Flyback.Core.Graph;
using Flyback.Editor.Inspect;
using Flyback.Editor.Windows;
using Flyback.Engine.Graph;
using Shouldly;

namespace Flyback.Editor.Tests.Inspect;

/// <summary>
/// The plate at the head of the inspector, whole while there is room for it and folded into a
/// pinned header where there is not, or once it has scrolled away.
/// </summary>
public class InspectorFoldTests : EditorTest
{
    private MainWindow Selecting(out NodeInstance sine)
    {
        var window = Open(Presets.Plasma(NodeCatalog.BuiltIn));
        var editor = Editor(window);

        sine = editor.History.Patch.Nodes.First(n => n.TypeId == "osc.sine");
        editor.Selection.Take([sine.Id]);
        editor.Selection.Announce();
        Settle(window);

        return window;
    }

    /// <summary>Works the window with a finger, as a phone does, under which the plate folds.</summary>
    private void Fingered(MainWindow window)
    {
        Service<LastPress>(window).OnlyFingers();
        Settle(window);
    }

    /// <summary>Holds the inspector to <paramref name="height"/>, as a phone held sideways does.</summary>
    private static void Short(MainWindow window, double height)
    {
        All<Grid>(window).Single(g => g.Name == "columns").RowDefinitions[2].MaxHeight = height;
        Settle(window);
    }

    private static InspectorHeader Header(MainWindow window) => All<InspectorHeader>(window).Single();

    private static ContentControl PlateHost(MainWindow window) => All<ContentControl>(window).Single(c => c.Name == "plate-host");

    private static Button Named(Control root, string name) => All<Button>(root).Single(b => b.Name == name);

    [AvaloniaFact]
    public void A_short_inspector_pins_the_header_and_shows_the_rows_under_it()
    {
        var window = Selecting(out _);
        Fingered(window);

        Short(window, 120);

        Header(window).IsVisible.ShouldBeTrue();
        PlateHost(window).IsVisible.ShouldBeFalse();
        All<TextBlock>(Header(window)).Single(t => t.Name == "header-name").Text.ShouldBe("Sine");
        Named(Header(window), "header-switch-modules").ShouldNotBeNull();
        Named(Header(window), "header-delete-modules").ShouldNotBeNull();
        Named(Header(window), "header-more").ShouldNotBeNull();

        // The first row stands just under the header rather than under a plate that no longer fits.
        var first = Knobs(window).First();
        first.TranslatePoint(default, Header(window))!.Value.Y.ShouldBeLessThan(120);
    }

    [AvaloniaFact]
    public void A_tall_inspector_shows_the_plate_whole_until_its_name_has_scrolled_past()
    {
        var window = Selecting(out _);
        Fingered(window);

        // Tall enough for the plate, short enough to scroll.
        Short(window, 260);

        Header(window).IsVisible.ShouldBeFalse();
        PlateHost(window).IsVisible.ShouldBeTrue();

        var scroller = All<ScrollViewer>(window).Single(s => s.Content is StackPanel rows && rows.Children.Contains(PlateHost(window)));
        scroller.Offset = new Vector(0, 80);
        Settle(window);

        Header(window).IsVisible.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void A_short_inspector_leaves_the_description_to_the_menu()
    {
        var window = Selecting(out _);
        Fingered(window);

        Short(window, 120);

        All<TextBlock>(window).Single(t => t.Name == InspectorFold.DescriptionName).IsVisible.ShouldBeFalse();

        var menu = new PlateMenu(Header(window).Plate!, () => { }).Content(() => { });
        All<TextBlock>(menu).ShouldContain(t => t.Text == "The basic waveform. Smooth bands and blobs.");
    }

    [AvaloniaFact]
    public void The_menus_tiles_press_the_plates_own_buttons()
    {
        var window = Selecting(out var sine);
        Fingered(window);

        Short(window, 120);

        var closed = false;
        var menu = new PlateMenu(Header(window).Plate!, () => { }).Content(() => closed = true);
        Show(menu);

        All<Button>(menu).Select(b => b.Name).ShouldBe(
            ["menu-switch-modules", "menu-rename", "menu-duplicate-modules", "menu-delete-modules", "menu-copy-modules", "menu-cut-modules"]);

        Press(Named(menu, "menu-delete-modules"));
        Settle(window);

        closed.ShouldBeTrue();
        Editor(window).History.Patch.Find(sine.Id).ShouldBeNull();
    }

    [AvaloniaFact]
    public void Under_a_mouse_on_a_wide_panel_the_buttons_stand_on_the_band_beside_the_name()
    {
        var window = Selecting(out _);
        window.Width = 1440;
        window.Height = 900;
        Settle(window);

        var plate = (ModulePlate)PlateHost(window).Content!;

        plate.Layout.ShouldBe(PlateLayout.Wide);
        PlateHost(window).Parent.ShouldBeOfType<Decorator>("a wide plate is pinned above the rows");

        var name = All<TextBlock>(window).Single(t => t.Name == "moduleName");
        var off = Named(window, "switch-modules");

        off.TranslatePoint(default, name)!.Value.Y.ShouldBeLessThan(name.Bounds.Height, "on the band, not under it");
        off.TranslatePoint(default, name)!.Value.X.ShouldBeGreaterThan(name.Bounds.Width / 2, "to the right of the name");
        name.TextAlignment.ShouldBe(Avalonia.Media.TextAlignment.Left);
    }

    [AvaloniaFact]
    public void Under_a_mouse_on_a_narrow_panel_the_buttons_stand_under_the_name()
    {
        var window = Selecting(out _);
        window.Width = 1100;
        Settle(window);

        var plate = (ModulePlate)PlateHost(window).Content!;
        plate.Layout.ShouldBe(PlateLayout.Narrow);

        var name = All<TextBlock>(window).Single(t => t.Name == "moduleName");
        Named(window, "switch-modules").TranslatePoint(default, name)!.Value.Y.ShouldBeGreaterThan(name.Bounds.Height);
    }

    [AvaloniaFact]
    public void Under_a_finger_the_plate_has_a_strip_of_worded_buttons()
    {
        var window = Selecting(out var sine);

        Service<LastPress>(window).Finger();
        Settle(window);

        var plate = (ModulePlate)PlateHost(window).Content!;
        plate.Layout.ShouldBe(PlateLayout.Touch);

        var strip = All<Avalonia.Controls.Primitives.UniformGrid>(window).Single(g => g.Name == "plate-strip");
        All<Button>(strip).Select(b => b.Name).ShouldBe(["strip-switch-modules", "strip-duplicate-modules", "strip-delete-modules", "strip-picture", "strip-more"]);
        All<TextBlock>(strip).Select(t => t.Text).ShouldBe(["Bypass", "Duplicate", "Delete", "Expand", "More"]);

        Press(Named(strip, "strip-delete-modules"));
        Settle(window);

        Editor(window).History.Patch.Find(sine.Id).ShouldBeNull();
    }

    [AvaloniaFact]
    public void A_phones_keys_leave_it_a_finger()
    {
        var window = Selecting(out _);
        var lastPress = Service<LastPress>(window);

        lastPress.OnlyFingers();
        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.A, Avalonia.Input.RawInputModifiers.None);
        Settle(window);

        lastPress.ByFinger.ShouldBeTrue();
        ((ModulePlate)PlateHost(window).Content!).Layout.ShouldBe(PlateLayout.Touch);
    }

    [AvaloniaFact]
    public void Under_a_mouse_a_narrow_short_inspector_folds_as_well()
    {
        var window = Selecting(out _);
        window.Width = 1100;
        Settle(window);

        Short(window, 120);

        Header(window).IsVisible.ShouldBeTrue();
        PlateHost(window).IsVisible.ShouldBeFalse();
    }

    private static Control PictureBox(MainWindow window) => (Control)All<Flyback.Ui.Controls.PreviewHost>(window).Single().Parent!;

    [AvaloniaFact]
    public void Expand_gives_the_inspector_the_whole_side_and_presses_back()
    {
        var window = Selecting(out _);
        Fingered(window);

        var before = All<Grid>(window).Single(g => g.Name == "columns").RowDefinitions[2].ActualHeight;

        Press(Named(window, "strip-picture"));
        Settle(window);

        PictureBox(window).IsVisible.ShouldBeFalse();
        All<Grid>(window).Single(g => g.Name == "columns").RowDefinitions[2].ActualHeight.ShouldBeGreaterThan(before + 100);
        All<TextBlock>(Named(window, "strip-picture")).Single().Text.ShouldBe("Picture");

        Press(Named(window, "strip-picture"));
        Settle(window);

        PictureBox(window).IsVisible.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void The_picture_comes_back_once_nothing_is_selected()
    {
        var window = Selecting(out _);
        Fingered(window);

        Press(Named(window, "strip-picture"));
        Settle(window);
        PictureBox(window).IsVisible.ShouldBeFalse();

        Editor(window).Selection.Take([]);
        Editor(window).Selection.Announce();
        Settle(window);

        PictureBox(window).IsVisible.ShouldBeTrue();
    }

    /// <summary>The dialog's own control called <paramref name="name"/>, from the layer it is shown on.</summary>
    private static T InDialog<T>(MainWindow window, string name) where T : Control =>
        All<T>(Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(window)!).Single(c => c.Name == name);

    [AvaloniaFact]
    public void Under_a_finger_a_double_tap_on_the_name_asks_in_a_dialog_at_the_top()
    {
        var window = Selecting(out var sine);
        Fingered(window);
        Short(window, 120);

        var name = All<TextBlock>(Header(window)).Single(t => t.Name == "header-name");
        name.RaiseEvent(new Avalonia.Input.TappedEventArgs(Avalonia.Input.InputElement.DoubleTappedEvent, null!));
        Settle(window);

        All<TextBox>(Header(window)).ShouldBeEmpty("a finger is asked in the dialog, clear of its keyboard");

        var box = InDialog<TextBox>(window, "rename-box");
        box.TranslatePoint(default, window)!.Value.Y.ShouldBeLessThan(window.Bounds.Height / 2, "the dialog stands at the top");
        All<TextBlock>(Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(window)!).ShouldContain(t => t.Text == "Sine · Oscillators");

        box.Text = "Lead";
        Press(InDialog<Button>(window, "rename-keep"));
        Settle(window);

        Editor(window).History.Patch.Find(sine.Id)!.Name.ShouldBe("Lead");
        All<TextBlock>(Header(window)).Single(t => t.Name == "header-name").Text.ShouldBe("Lead");
    }

    [AvaloniaFact]
    public void Cancelling_the_rename_dialog_leaves_the_name()
    {
        var window = Selecting(out var sine);
        Fingered(window);

        ((ModulePlate)PlateHost(window).Content!).BeginRename!();
        Settle(window);

        InDialog<TextBox>(window, "rename-box").Text = "Lead";
        Press(InDialog<Button>(window, "rename-cancel"));
        Settle(window);

        Editor(window).History.Patch.Find(sine.Id)!.Name.ShouldBeNull();
    }

    [AvaloniaFact]
    public void The_headers_switch_switches_the_module_off()
    {
        var window = Selecting(out var sine);
        Fingered(window);

        Short(window, 120);
        Press(Named(Header(window), "header-switch-modules"));
        Settle(window);

        Editor(window).History.Patch.Find(sine.Id)!.Off.ShouldBeTrue();
    }
}
