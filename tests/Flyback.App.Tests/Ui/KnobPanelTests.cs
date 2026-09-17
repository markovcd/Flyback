using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
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
    private static MainWindow Open(Patch patch)
    {
        var window = new MainWindow();

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
}
