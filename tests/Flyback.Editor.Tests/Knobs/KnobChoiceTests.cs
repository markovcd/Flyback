using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Flyback.Editor.Composition;
using Flyback.Editor.Notices;
using Shouldly;

namespace Flyback.Editor.Tests.Knobs;

/// <summary>
/// Linking a socket to a panel knob from the canvas: Alt+L on a picked well drops the
/// list of knobs, and the chip that then stands in the well drops it again to pick
/// another or unlink.
/// </summary>
public class KnobChoiceTests : EditorTest
{
    /// <summary>A Value module whose knob colors the picture.</summary>
    private static (Patch Patch, NodeInstance Value) Board(float knob = 0.2f)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(NodeCatalog.OutputTypeId, 700, 40);
        var value = b.Add("value", 200, 40, (0, knob));

        b.Wire(value, 0, output, NodeCatalog.OutputColorPort);

        return (b.Patch, value);
    }

    private static Point Well(MainWindow window, NodeInstance node, int port)
    {
        var editor = Editor(window);
        var def = NodeCatalog.Get(node.TypeId)!;
        var graph = ValueWell.At(Geometry.Bounds(node, def), Geometry.InputPort(node, def, port)).Center;

        return editor.TranslatePoint(editor.GraphToScreen.Transform(graph), window)!.Value;
    }

    private static void Click(MainWindow window, Point at)
    {
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }

    private static void Press(MainWindow window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        Settle(window);
    }

    private static KnobChoice Choice(MainWindow window) => Editor(window).KnobChoice;

    /// <summary>Picks the well, then drops the list with Alt+L.</summary>
    private static void AltL(MainWindow window, NodeInstance node)
    {
        Click(window, Well(window, node, 0));
        Press(window, Key.L, RawInputModifiers.Alt);
    }

    private static void Pick(MainWindow window, string text)
    {
        var choice = Choice(window);
        var index = choice.Rows.ToList().FindIndex(row => row.Text.Contains(text, StringComparison.Ordinal));
        index.ShouldBeGreaterThanOrEqualTo(0, $"no line reads {text}");

        while (choice.Index != index) Press(window, Key.Down);
        Press(window, Key.Enter);
    }

    [AvaloniaFact]
    public void Alt_L_on_a_picked_well_drops_the_knobs_with_a_new_one_last()
    {
        var (patch, value) = Board();
        patch.AddControl("Hue");
        var window = Open(patch);

        AltL(window, value);

        Choice(window).For.ShouldBe(new SocketPick(value.Id, 0));
        Choice(window).Rows.Select(row => row.Text).ShouldBe(["Hue · free", "New knob"]);
        Shoot(window, "knob-list.png");

        Press(window, Key.Escape);
        Choice(window).For.ShouldBeNull();
    }

    [AvaloniaFact]
    public void New_knob_adds_one_that_holds_the_value_where_it_was_as_one_step_to_undo()
    {
        var (patch, value) = Board(knob: 0.2f);
        var window = Open(patch);
        var history = Editor(window).History;

        AltL(window, value);
        Pick(window, "New knob");

        var knob = history.Patch.Controls.ShouldNotBeNull().ShouldHaveSingleItem();
        var link = ControlMap.Of(history.Patch.Find(value.Id)!, 0).ShouldNotBeNull();
        link.Control.ShouldBe(knob.Id);
        link.At(knob.Value).ShouldBe(0.2f, 1e-4f);

        history.Undo().ShouldBeTrue();
        history.Patch.Controls.ShouldBeNull();
        ControlMap.Of(history.Patch.Find(value.Id)!, 0).ShouldBeNull();
    }

    [AvaloniaFact]
    public void A_knob_on_the_panel_is_picked_from_the_list()
    {
        var (patch, value) = Board();
        var knob = patch.AddControl("Hue");
        var window = Open(patch);

        AltL(window, value);
        Pick(window, "Hue");

        ControlMap.Of(Editor(window).History.Patch.Find(value.Id)!, 0).ShouldNotBeNull().Control.ShouldBe(knob.Id);
        Shoot(window, "knob-linked.png");
    }

    [AvaloniaFact]
    public void The_chip_of_a_linked_socket_drops_the_list_on_its_knob_and_Unlink_keeps_its_value()
    {
        var (patch, value) = Board(knob: 0.2f);
        var window = Open(patch);
        var history = Editor(window).History;

        AltL(window, value);
        Pick(window, "New knob");
        var knob = history.Patch.Controls!.Single();
        var held = ControlMap.Of(history.Patch.Find(value.Id)!, 0)!.Value.At(knob.Value);

        Click(window, Well(window, history.Patch.Find(value.Id)!, 0));

        Choice(window).For.ShouldBe(new SocketPick(value.Id, 0));
        Choice(window).Rows[Choice(window).Index].Current.ShouldBeTrue();
        Choice(window).Rows[^1].Unlink.ShouldBeTrue();
        Shoot(window, "knob-chip-list.png");

        Pick(window, "Unlink");

        var after = history.Patch.Find(value.Id)!;
        ControlMap.Of(after, 0).ShouldBeNull();
        after.InputValues[0].ShouldBe(held);
    }

    [AvaloniaFact]
    public void A_line_of_the_list_is_picked_by_a_click()
    {
        var (patch, value) = Board();
        var knob = patch.AddControl("Hue");
        var window = Open(patch);

        AltL(window, value);

        var box = Choice(window).Box.ShouldNotBeNull();
        var line = new Point(box.Center.X, box.Top + KnobChoice.Padding + KnobChoice.RowHeight / 2);
        Click(window, Editor(window).TranslatePoint(line, window)!.Value);

        Choice(window).For.ShouldBeNull();
        ControlMap.Of(Editor(window).History.Patch.Find(value.Id)!, 0).ShouldNotBeNull().Control.ShouldBe(knob.Id);
    }

    [AvaloniaFact]
    public void Arrows_go_to_the_list_while_it_is_open_rather_than_the_value()
    {
        var (patch, value) = Board(knob: 0.2f);
        patch.AddControl("Hue");
        var window = Open(patch);

        AltL(window, value);
        Press(window, Key.Down);

        Choice(window).Index.ShouldBe(1);
        Editor(window).History.Patch.Find(value.Id)!.InputValues[0].ShouldBe(0.2f);
    }
}
