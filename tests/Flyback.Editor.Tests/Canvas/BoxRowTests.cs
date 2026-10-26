using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Flyback.Editor.Composition;
using Flyback.Editor.Notices;
using Shouldly;

namespace Flyback.Editor.Tests.Canvas;

/// <summary>
/// A shut box's input row is the row of the module inside it: its well turns and
/// picks the member's value, Alt+L links it, and the readout and step bar stand at
/// the box's edge, where the row is.
/// </summary>
public class BoxRowTests : EditorTest
{
    /// <summary>A Value module shut in a box with a sine, its own input on the box's edge.</summary>
    private static (Patch Patch, NodeInstance Value, NodeGroup Box) Board()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(NodeCatalog.OutputTypeId, 700, 40);
        var value = b.Add("value", 200, 40, (0, 0.2f));
        var sine = b.Add(NodeCatalog.SineTypeId, 200, 200);

        b.Wire(value, 0, output, NodeCatalog.OutputColorPort);

        var box = b.Patch.Group([value.Id, sine.Id]).ShouldNotBeNull();
        box.Expose(new GroupSocket(value.Id, 0, IsOutput: false));

        return (b.Patch, value, box);
    }

    private static Rect Bounds(Patch patch, NodeGroup box) => Geometry.GroupBounds(patch, box, patch.SocketsOf(box));

    private static Point Well(MainWindow window, Patch patch, NodeGroup box)
    {
        var editor = Editor(window);
        var sockets = patch.SocketsOf(box);
        var bounds = Bounds(patch, box);
        var graph = ValueWell.At(bounds, Geometry.GroupInputPort(bounds, sockets, 0)).Center;

        return editor.TranslatePoint(editor.GraphToScreen.Transform(graph), window)!.Value;
    }

    private static void Click(MainWindow window, Point at)
    {
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }

    [AvaloniaFact]
    public void Dragging_a_box_well_up_raises_the_value_of_the_module_inside()
    {
        var (patch, value, box) = Board();
        var window = Open(patch);
        var at = Well(window, patch, box);

        window.MouseDown(at, MouseButton.Left);
        for (var i = 1; i <= 4; i++) window.MouseMove(at - new Point(0, 10 * i));
        window.MouseUp(at - new Point(0, 40), MouseButton.Left);
        Settle(window);

        Editor(window).History.Patch.Find(value.Id)!.InputValues[0].ShouldBeGreaterThan(0.2f);
    }

    [AvaloniaFact]
    public void A_click_on_a_box_well_picks_it_with_the_readout_at_the_box()
    {
        var (patch, value, box) = Board();
        var window = Open(patch);
        var editor = Editor(window);

        Click(window, Well(window, patch, box));

        editor.WellFocus.Focused.ShouldBe(new SocketPick(value.Id, 0));
        editor.Selection.Scene.RowOf(value.Id, 0).ShouldNotBeNull().Bounds.ShouldBe(Bounds(patch, box));
        Shoot(window, "box-well.png");
    }

    [AvaloniaFact]
    public void Alt_L_on_a_box_well_links_the_module_inside_and_the_box_shows_its_chip()
    {
        var (patch, value, box) = Board();
        var window = Open(patch);
        var history = Editor(window).History;

        Click(window, Well(window, patch, box));
        window.KeyPress(Key.L, RawInputModifiers.Alt, PhysicalKey.None, null);
        Settle(window);

        Editor(window).KnobChoice.For.ShouldBe(new SocketPick(value.Id, 0));

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        Settle(window);

        var knob = history.Patch.Controls.ShouldNotBeNull().ShouldHaveSingleItem();
        ControlMap.Of(history.Patch.Find(value.Id)!, 0).ShouldNotBeNull().Control.ShouldBe(knob.Id);

        Click(window, Well(window, history.Patch, history.Patch.Groups!.Single()));
        Editor(window).KnobChoice.For.ShouldBe(new SocketPick(value.Id, 0));
        Shoot(window, "box-chip.png");
    }
}
