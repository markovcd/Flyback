using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// What the panel shows when the selection is a group: its name, its edge, and
/// what can be done to it.
/// </summary>
/// <remarks>
/// A selection that is exactly a group is about the group. Pressing a box selects
/// the modules inside it, so without this the panel would show whichever of them
/// the focus happened to land on — a set of knobs belonging to one module,
/// presented as though it were what was clicked.
/// </remarks>
public class GroupInspectorTests : UiTest
{
    private static MainWindow Open(out NodeGroup group)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var time = b.Add("time", 40, 200);
        var osc = b.Add("osc.sine", 300, 60);
        var product = b.Add("math.mul", 560, 160);
        var screen = b.Add(NodeCatalog.OutputTypeId, 900, 200);

        b.Wire(time, 0, osc, 1)
         .Wire(osc, 0, product, 0)
         .Wire(product, 0, screen, NodeCatalog.OutputLeftPort);

        var window = new MainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var editor = Editor(window);
        editor.Patch = b.Patch;
        Settle(window);

        var sine = editor.Patch.Find(osc.Id)!;
        var mul = editor.Patch.Find(product.Id)!;

        group = editor.Patch.Group([sine.Id, mul.Id])!;
        editor.NotifyPatchChanged();

        SelectBox(window, editor.Patch, group);
        return window;
    }

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    private static void SelectBox(MainWindow window, Patch patch, NodeGroup group)
    {
        var editor = Editor(window);
        var bounds = NodeGeometry.GroupBounds(patch, group, patch.SocketsOf(group));
        var header = new Point(bounds.Center.X, bounds.Y + NodeGeometry.HeaderHeight / 2);

        var at = editor.TranslatePoint(editor.GraphToScreen.Transform(header), window)
            ?? throw new InvalidOperationException("the editor is not in this window");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }

    private static TextBlock Title(MainWindow window) =>
        All<TextBlock>(window).First(t => t.FontSize == 17);

    private static TextBox? Box(MainWindow window) =>
        All<TextBox>(window).FirstOrDefault(t => t.FontSize == 17);

    private static string[] Lines(MainWindow window) =>
        [.. All<TextBlock>(window).Select(t => t.Text ?? string.Empty)];

    private static string[] Buttons(MainWindow window) =>
        [.. All<Button>(window).Select(b => b.Content as string ?? string.Empty)];

    [AvaloniaFact]
    public void The_panel_is_about_the_group_rather_than_a_module_inside_it()
    {
        var window = Open(out _);

        // Not "Sine" or "Multiply", which is what pressing the box selects.
        Title(window).Text.ShouldBe("2 modules");
        Lines(window).ShouldContain("Group");
    }

    /// <summary>
    /// The edge, named the same way the box draws it: the module and port inside
    /// that each socket stands for.
    /// </summary>
    [AvaloniaFact]
    public void The_panel_lists_the_ports_where_wires_cross_the_edge()
    {
        var window = Open(out _);
        var lines = Lines(window);

        lines.ShouldContain("In");
        lines.ShouldContain("Sine.freq");

        lines.ShouldContain("Out");
        lines.ShouldContain("Multiply.out");

        // And nothing about what is inside: the wire from Sine to Multiply
        // crosses nothing, so it is not on the edge.
        lines.ShouldNotContain("Sine.out");
        lines.ShouldNotContain("Multiply.a");
    }

    [AvaloniaFact]
    public void The_panel_offers_opening_ungrouping_and_deleting()
    {
        var window = Open(out _);
        var buttons = Buttons(window);

        buttons.ShouldContain("Open group");
        buttons.ShouldContain("Ungroup");
        buttons.ShouldContain("Delete 2 modules");
    }

    /// <summary>
    /// The button says which way it goes, so it has to be rebuilt when the group
    /// opens — which is not a selection change and would otherwise go unheard.
    /// </summary>
    [AvaloniaFact]
    public void Opening_the_group_turns_the_button_round()
    {
        var window = Open(out var group);

        Press(window, "Open group");

        group.Collapsed.ShouldBeFalse();
        Buttons(window).ShouldContain("Close group");
    }

    [AvaloniaFact]
    public void Double_clicking_the_name_turns_it_into_a_box()
    {
        var window = Open(out _);

        DoubleClickTitle(window);

        // Empty rather than filled in with "2 modules": the box holds the name
        // the group has, and it has none.
        var box = Box(window).ShouldNotBeNull("the title should have become a box");
        box.Text.ShouldBeNullOrEmpty();
        box.PlaceholderText.ShouldBe("2 modules");
    }

    [AvaloniaFact]
    public void What_is_typed_becomes_the_name_on_the_panel_and_on_the_canvas()
    {
        var window = Open(out var group);

        DoubleClickTitle(window);

        var box = Box(window).ShouldNotBeNull();
        box.Text = "Voice";
        Settle(window);

        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Settle(window);

        group.Name.ShouldBe("Voice");
        group.Title().ShouldBe("Voice");
        Title(window).Text.ShouldBe("Voice");
    }

    [AvaloniaFact]
    public void Emptying_the_box_takes_the_name_off_again()
    {
        var window = Open(out var group);

        group.Rename("Voice");

        DoubleClickTitle(window);

        var box = Box(window).ShouldNotBeNull();
        box.Text = "   ";
        Settle(window);

        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Settle(window);

        group.Name.ShouldBeNull();
        Title(window).Text.ShouldBe("2 modules");
    }

    [AvaloniaFact]
    public void Escape_leaves_the_name_alone()
    {
        var window = Open(out var group);

        DoubleClickTitle(window);

        var box = Box(window).ShouldNotBeNull();
        box.Text = "Voice";
        Settle(window);

        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Settle(window);

        group.Name.ShouldBeNull();
    }

    private static void Press(MainWindow window, string caption)
    {
        var button = All<Button>(window).First(b => b.Content as string == caption);
        var at = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("the button is not in this window");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }

    private static void DoubleClickTitle(MainWindow window)
    {
        var title = Title(window);

        var at = title.TranslatePoint(new Point(title.Bounds.Width / 2, title.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("the title is not in this window");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }
}
