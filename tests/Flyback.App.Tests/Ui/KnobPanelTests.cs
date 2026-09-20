using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The knob panel as it is used: adding a knob, linking sockets to it by clicking
/// them, and turning it without an edit or a recompile.
/// </summary>
public class KnobPanelTests : UiTest
{
    private MainWindow Open(Patch patch)
    {
        var window = NewMainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Editor(window).Patch = patch;
        Settle(window);

        return window;
    }

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    private static ControlsPanel Panel(MainWindow window) => All<ControlsPanel>(window).Single();

    /// <summary>A Value module whose knob colors the picture.</summary>
    private static (Patch Patch, NodeInstance Value) Board(float knob = 0.2f)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(NodeCatalog.OutputTypeId, 700, 40);
        var value = b.Add("value", 200, 40, (0, knob));

        b.Wire(value, 0, output, NodeCatalog.OutputColorPort);

        return (b.Patch, value);
    }

    private static Point OnWindow(MainWindow window, Visual visual, Point local) =>
        visual.TranslatePoint(local, window)!.Value;

    private static void Click(MainWindow window, Point at)
    {
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }

    private static void ClickInputRow(MainWindow window, NodeInstance node, int port)
    {
        var editor = Editor(window);
        var def = NodeCatalog.BuiltIn.Require(node.TypeId);
        var centre = NodeGeometry.InputPort(node, def, port);
        var graph = new Point(centre.X + NodeGeometry.Width / 2, centre.Y);

        Click(window, OnWindow(window, editor, editor.GraphToScreen.Transform(graph)));
    }

    private static void AddKnob(MainWindow window)
    {
        var add = All<Button>(window).Single(b => b.Name == "add-knob");

        Click(window, OnWindow(window, add, new Point(add.Bounds.Width / 2, add.Bounds.Height / 2)));
    }

    private static void Turn(MainWindow window, double up)
    {
        var knob = All<Knob>(window).First();
        var from = OnWindow(window, knob, new Point(knob.Bounds.Width / 2, knob.Bounds.Height / 2));

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from - new Point(0, up));
        window.MouseUp(from - new Point(0, up), MouseButton.Left);
        Settle(window);
    }

    [AvaloniaFact]
    public void Ctrl_K_shows_and_hides_the_panel()
    {
        var (patch, _) = Board();
        var window = Open(patch);

        Panel(window).IsVisible.ShouldBeFalse();

        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        Settle(window);
        Panel(window).IsVisible.ShouldBeTrue();

        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        Settle(window);
        Panel(window).IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Adding_a_knob_starts_linking_and_undo_takes_it_away()
    {
        var (patch, _) = Board();
        var window = Open(patch);

        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        Settle(window);
        AddKnob(window);

        var knob = Editor(window).Patch.Controls.ShouldHaveSingleItem();
        Editor(window).LinkingControl.ShouldBe(knob.Id);

        Editor(window).Undo().ShouldBeTrue();
        Settle(window);

        Editor(window).Patch.Controls.ShouldBeNull();
        Editor(window).LinkingControl.ShouldBeNull();
    }

    [AvaloniaFact]
    public void Clicking_a_socket_while_linking_links_it_without_moving_it()
    {
        var (patch, value) = Board(knob: 0.2f);
        var window = Open(patch);

        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        Settle(window);
        AddKnob(window);

        var placed = Editor(window).Patch.Find(value.Id)!;
        ClickInputRow(window, placed, 0);

        var knob = Editor(window).Patch.Controls!.Single();
        var link = ControlMap.Of(Editor(window).Patch.Find(value.Id)!, 0).ShouldNotBeNull();

        link.Control.ShouldBe(knob.Id);
        link.At(knob.Value).ShouldBe(0.2f, 1e-4f);
        All<PreviewHost>(window).Single().Program.LiveInputs.ShouldContain(knob.Key);
    }

    [AvaloniaFact]
    public void Turning_a_knob_moves_the_socket_without_an_edit_or_a_recompile()
    {
        var (patch, value) = Board(knob: 0.2f);
        var window = Open(patch);

        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        Settle(window);
        AddKnob(window);
        ClickInputRow(window, Editor(window).Patch.Find(value.Id)!, 0);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Settle(window);

        var knob = Editor(window).Patch.Controls!.Single();
        var before = knob.Value;
        var program = All<PreviewHost>(window).Single().Program;
        var modified = Editor(window).IsModified;

        Turn(window, up: 40);

        knob.Value.ShouldBeGreaterThan(before);
        All<PreviewHost>(window).Single().Program.ShouldBeSameAs(program);
        Editor(window).IsModified.ShouldBe(modified);
    }

    [AvaloniaFact]
    public void A_patch_that_arrives_with_knobs_opens_the_panel()
    {
        var (patch, value) = Board();
        var knob = patch.AddControl("Brightness");
        ControlMap.Link(value, 0, new ControlLink(knob.Id, 0f, 1f));

        var window = Open(patch);

        Panel(window).IsVisible.ShouldBeTrue();
        All<TextBlock>(Panel(window)).ShouldContain(t => t.Text == "Brightness");
    }

    [AvaloniaFact]
    public void Escape_stops_linking()
    {
        var (patch, _) = Board();
        var window = Open(patch);

        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        Settle(window);
        AddKnob(window);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Settle(window);

        Editor(window).LinkingControl.ShouldBeNull();
    }

    [AvaloniaFact]
    public void The_knob_menu_opens_and_removes_the_knob()
    {
        var (patch, _) = Board();
        patch.AddControl("Glow");
        for (var i = 0; i < 14; i++) patch.AddControl();
        var window = Open(patch);

        var more = All<Button>(Panel(window)).First(b => b.Name == "knob-menu");
        Click(window, OnWindow(window, more, new Point(more.Bounds.Width / 2, more.Bounds.Height / 2)));

        var menu = more.Flyout.ShouldBeOfType<MenuFlyout>();
        menu.IsOpen.ShouldBeTrue();

        var remove = menu.Items.OfType<MenuItem>().Single(item => (item.Header as string) == "Remove knob");
        remove.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Settle(window);

        Editor(window).Patch.Controls!.ShouldNotContain(c => c.Name == "Glow");
    }

    /// <summary>
    /// A knob taken off and brought back by Ctrl+Z comes back where the hand
    /// left it.
    /// </summary>
    /// <remarks>
    /// Turning a knob is not an edit (ADR-0086), so the snapshot the undo
    /// restores holds the knob where the last edit found it. The hub keeps the
    /// position of a knob that has left the patch, for the one that comes back.
    /// </remarks>
    [AvaloniaFact]
    public void A_removed_knob_comes_back_where_it_was_left()
    {
        var (patch, value) = Board(knob: 0.2f);
        var window = Open(patch);

        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        Settle(window);
        AddKnob(window);
        ClickInputRow(window, Editor(window).Patch.Find(value.Id)!, 0);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Settle(window);

        ControlMap.Of(Editor(window).Patch.Find(value.Id)!, 0).ShouldNotBeNull("the knob is linked to the socket");

        Turn(window, up: 60);

        var left = Editor(window).Patch.Controls!.Single().Value;
        left.ShouldNotBe(0.5f, "the knob was turned");

        var more = All<Button>(Panel(window)).First(b => b.Name == "knob-menu");
        var menu = more.Flyout.ShouldBeOfType<MenuFlyout>();

        menu.Items.OfType<MenuItem>().Single(item => (item.Header as string) == "Remove knob")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Settle(window);

        (Editor(window).Patch.Controls?.Count ?? 0).ShouldBe(0);

        All<Button>(window).Single(b => b.Name == "undo")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle(window);

        Editor(window).Patch.Controls!.Single().Value.ShouldBe(left, 1e-4f, "where a hand left a knob is not something Ctrl+Z takes back");
    }

    /// <summary>
    /// A linked socket's range box is still there after a number has gone into
    /// it.
    /// </summary>
    /// <remarks>
    /// The range is part of what the inspector takes its shape from, so that one
    /// changed from elsewhere rebuilds the row. Changed from the box itself the
    /// row already says it, and a rebuild would take the box out from under the
    /// typing: a number box takes its value on every keystroke, so "2.5" would
    /// get as far as "2" before the rest was typed at the window.
    /// </remarks>
    [AvaloniaFact]
    public void A_range_box_survives_being_typed_into()
    {
        var (patch, value) = Board();
        var knob = patch.AddControl();
        ControlMap.Link(value, 0, new ControlLink(knob.Id, 0f, 1f));

        var window = Open(patch);

        Editor(window).Select(value.Id);
        Settle(window);

        var inspector = All<StackPanel>(window).Single(p => p.Name == "inspector");
        var unlink = All<Button>(inspector).Single(b => b.Name == "unlink");
        var box = All<NumericUpDown>(unlink.GetVisualParent()!).Last();

        box.Value = 2m;
        Settle(window);

        ControlMap.Of(value, 0)!.Value.Max.ShouldBe(2f, "the range was taken");
        All<NumericUpDown>(window).ShouldContain(box, "and the box it was typed into is still on the screen");
    }

    [AvaloniaFact]
    public void Knobs_that_run_out_of_width_wrap_onto_another_row_before_anything_scrolls()
    {
        var (patch, _) = Board();
        for (var i = 0; i < 20; i++) patch.AddControl();

        var window = Open(patch);
        var knobs = All<Knob>(Panel(window)).ToList();
        var rows = knobs.Select(k => Math.Round(OnWindow(window, k, default).Y)).Distinct().Count();

        rows.ShouldBeGreaterThan(1);
        knobs.ShouldAllBe(k => OnWindow(window, k, new Point(k.Bounds.Width, 0)).X <= OnWindow(window, Panel(window), new Point(Panel(window).Bounds.Width, 0)).X);
    }

    [AvaloniaFact]
    public void The_panel_can_be_dragged_taller()
    {
        var (patch, _) = Board();
        patch.AddControl();

        var window = Open(patch);
        var splitter = All<GridSplitter>(window).Single(s => s.Name == "controls-splitter");
        var before = Panel(window).Bounds.Height;
        var from = OnWindow(window, splitter, new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2));

        splitter.IsVisible.ShouldBeTrue();

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from - new Point(0, 120));
        window.MouseUp(from - new Point(0, 120), MouseButton.Left);
        Settle(window);

        Panel(window).Bounds.Height.ShouldBeGreaterThan(before + 60);
    }

    [AvaloniaFact]
    public void Adding_a_knob_keeps_the_height_the_panel_was_dragged_to()
    {
        var (patch, _) = Board();
        patch.AddControl();

        var window = Open(patch);
        var splitter = All<GridSplitter>(window).Single(s => s.Name == "controls-splitter");
        var from = OnWindow(window, splitter, new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2));

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from - new Point(0, 120));
        window.MouseUp(from - new Point(0, 120), MouseButton.Left);
        Settle(window);

        var dragged = Panel(window).Bounds.Height;

        AddKnob(window);

        Panel(window).Bounds.Height.ShouldBe(dragged, 1);
    }

    private static string Order(MainWindow window) =>
        string.Join(",", Editor(window).Patch.Controls!.Select(c => c.Name));

    [AvaloniaFact]
    public void Dragging_a_knob_by_its_name_moves_it_and_undo_puts_it_back()
    {
        var (patch, _) = Board();
        for (var i = 0; i < 3; i++) patch.AddControl();
        var window = Open(patch);

        var names = All<TextBlock>(Panel(window)).Where(t => t.Name == "knob-name").ToList();
        var from = OnWindow(window, names[0], new Point(names[0].Bounds.Width / 2, names[0].Bounds.Height / 2));
        var last = names[2];
        var to = OnWindow(window, last, new Point(last.Bounds.Width + 20, last.Bounds.Height / 2));

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Point(10, 0));
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
        Settle(window);

        Order(window).ShouldBe("Knob 2,Knob 3,Knob 1");
        Editor(window).LinkingControl.ShouldBeNull("a drag is not a click");

        Editor(window).Undo().ShouldBeTrue();
        Settle(window);

        Order(window).ShouldBe("Knob 1,Knob 2,Knob 3");
    }

    [AvaloniaFact]
    public void The_knob_menu_moves_a_knob_one_place()
    {
        var (patch, _) = Board();
        for (var i = 0; i < 3; i++) patch.AddControl();
        var window = Open(patch);

        var more = All<Button>(Panel(window)).First(b => b.Name == "knob-menu");
        var menu = more.Flyout.ShouldBeOfType<MenuFlyout>();
        var items = menu.Items.OfType<MenuItem>().ToList();

        items.Single(i => (i.Header as string) == "Move left").IsEnabled.ShouldBeFalse();

        items.Single(i => (i.Header as string) == "Move right")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Settle(window);

        Order(window).ShouldBe("Knob 2,Knob 1,Knob 3");
    }

    /// <summary>
    /// Escape after a learn that found no controller to learn from. The field
    /// Escape cancels must never be left holding a source that has been disposed.
    /// </summary>
    [AvaloniaFact]
    public void Escape_after_learning_found_no_controller_does_nothing()
    {
        var (patch, _) = Board();
        patch.AddControl("Glow");
        var window = Open(patch);

        var more = All<Button>(Panel(window)).First(b => b.Name == "knob-menu");
        var menu = more.Flyout.ShouldBeOfType<MenuFlyout>();

        // No MIDI backend is installed in a test run, which is what a machine with
        // no controller plugged in looks like to this.
        menu.Items.OfType<MenuItem>().Single(item => (item.Header as string) == "Learn MIDI controller")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Settle(window);

        Editor(window).Focus();

        Should.NotThrow(() =>
        {
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Settle(window);
        });
    }
}
