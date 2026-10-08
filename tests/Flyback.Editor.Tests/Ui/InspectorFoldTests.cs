using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Flyback.Core.Graph;
using Flyback.Editor.Inspect;
using Flyback.Editor.Windows;
using Flyback.Engine.Graph;
using Shouldly;

namespace Flyback.Editor.Tests.Ui;

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

        Short(window, 120);

        All<TextBlock>(window).Single(t => t.Name == InspectorFold.DescriptionName).IsVisible.ShouldBeFalse();

        var menu = new PlateMenu(Header(window).Plate!, () => { }).Content(() => { });
        All<TextBlock>(menu).ShouldContain(t => t.Text == "The basic waveform. Smooth bands and blobs.");
    }

    [AvaloniaFact]
    public void The_menus_tiles_press_the_plates_own_buttons()
    {
        var window = Selecting(out var sine);

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
    public void The_headers_switch_switches_the_module_off()
    {
        var window = Selecting(out var sine);

        Short(window, 120);
        Press(Named(Header(window), "header-switch-modules"));
        Settle(window);

        Editor(window).History.Patch.Find(sine.Id)!.Off.ShouldBeTrue();
    }
}
