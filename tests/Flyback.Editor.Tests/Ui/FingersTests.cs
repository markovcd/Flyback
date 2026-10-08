using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Editor.Bars;
using Flyback.Editor.Canvas;
using Flyback.Ui.Controls;
using Flyback.Core.Graph;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Flyback.Editor.Tests.Ui;

/// <summary>
/// Fingers on the canvas, told to it as mouse buttons (ADR-0165). Headless Avalonia
/// has no touch input, so the fingers are handed to <see cref="Fingers"/> directly,
/// in the canvas's own coordinates.
/// </summary>
public class FingersTests : EditorTest
{
    private static Patch Pair(out NodeInstance clock, out NodeInstance sine)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        clock = builder.Add("time", 100, 100);
        sine = builder.Add("osc.sine", 500, 100);
        builder.Add(NodeCatalog.OutputTypeId, 900, 100);

        return builder.Patch;
    }

    private static Pointer Finger() => new(Pointer.GetNextFreeId(), PointerType.Touch, false);

    private static Point On(NodeEditor editor, Point graph) => editor.GraphToScreen.Transform(graph);

    /// <summary>Counts the pointer anchors taken, and holds nothing.</summary>
    private sealed class Counting : IPointerAnchors
    {
        public int Taken { get; private set; }

        public IPointerAnchor? Take(Visual visual)
        {
            Taken++;
            return null;
        }
    }

    [AvaloniaFact]
    public void A_tap_on_a_module_selects_it()
    {
        var (editor, window) = Editing(Pair(out var clock, out _));
        var finger = Finger();
        var at = On(editor, Body(clock));

        editor.Fingers.Down(editor, finger, at, 1_000);
        editor.Fingers.Up(editor, finger, at, 1_050);
        Settle(window);

        editor.Selection.Ids.ShouldBe([clock.Id]);
    }

    [AvaloniaFact]
    public void A_finger_that_lifts_where_it_landed_moves_nothing()
    {
        var (editor, _) = Editing(Pair(out var clock, out _));
        var finger = Finger();
        var at = On(editor, Body(clock));

        editor.Fingers.Down(editor, finger, at, 1_000);
        editor.Fingers.Move(editor, finger, at + new Vector(Fingers.Slop / 2, 0));
        editor.Fingers.Up(editor, finger, at + new Vector(Fingers.Slop / 2, 0), 1_050);

        (clock.X, clock.Y).ShouldBe((100d, 100d));
        editor.History.CanUndo.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_second_finger_mid_carry_pans_and_the_module_stays_under_the_first()
    {
        var (editor, _) = Editing(Pair(out var clock, out _));
        var carrying = Finger();
        var panning = Finger();
        var grip = Body(clock);
        var from = On(editor, grip);

        editor.Fingers.Down(editor, carrying, from, 1_000);
        editor.Fingers.Move(editor, carrying, from + new Vector(40, 0));

        var second = from + new Vector(200, 100);
        editor.Fingers.Down(editor, panning, second, 1_500);

        var panWas = editor.View.Pan;
        editor.Fingers.Move(editor, panning, second + new Vector(60, 0));

        editor.View.Pan.ShouldNotBe(panWas);

        editor.Fingers.Up(editor, panning, second + new Vector(60, 0), 1_600);
        editor.Fingers.Move(editor, carrying, from + new Vector(80, 0));
        editor.Fingers.Up(editor, carrying, from + new Vector(80, 0), 1_700);

        // Where the grip is now is where the first finger let go.
        var lifted = editor.View.ToGraph(from + new Vector(80, 0));
        (clock.X + NodeGeometry.Width / 2).ShouldBe(lifted.X, 0.5);
        editor.Gestures.Gesturing.ShouldBeFalse();
        editor.History.CanUndo.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void A_pinch_under_nothing_zooms_about_the_point_between_the_fingers()
    {
        var (editor, _) = Editing(Pair(out _, out _));
        var a = Finger();
        var b = Finger();
        var middle = new Point(600, 400);
        var anchored = editor.View.ToGraph(middle);

        editor.Fingers.Down(editor, a, middle - new Vector(50, 0), 1_000);
        editor.Fingers.Down(editor, b, middle + new Vector(50, 0), 1_010);
        editor.Fingers.Move(editor, a, middle - new Vector(100, 0));
        editor.Fingers.Move(editor, b, middle + new Vector(100, 0));

        editor.View.Zoom.ShouldBeGreaterThan(1.5);
        editor.View.ToGraph(middle).X.ShouldBe(anchored.X, 1);

        editor.Fingers.Up(editor, a, middle - new Vector(100, 0), 1_100);
        editor.Fingers.Up(editor, b, middle + new Vector(100, 0), 1_110);

        editor.Selection.Count.ShouldBe(0);
        editor.Gestures.Gesturing.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_finger_held_on_an_unpatched_input_turns_it_without_holding_the_pointer()
    {
        var anchors = new Counting();
        var patch = Pair(out _, out var sine);
        var (editor, _) = Editing(patch, services => services.AddSingleton<IPointerAnchors>(anchors));
        var def = NodeCatalog.Get(sine.TypeId)!;
        var port = Enumerable.Range(0, def.Inputs.Count).First(i => KnobLinking.Linkable(def.Inputs[i]));
        var was = sine.InputValues[port];
        var finger = Finger();

        // Beside the socket rather than on it: a fingertip within reach lands on it.
        var at = On(editor, Geometry.InputPort(sine, def, port)) - new Vector(Fingers.Reach - 4, 0);

        editor.Fingers.Down(editor, finger, at, 1_000);
        editor.Fingers.Held();
        editor.Fingers.Move(editor, finger, at - new Vector(0, 40));
        editor.Fingers.Up(editor, finger, at - new Vector(0, 40), 2_000);

        sine.InputValues[port].ShouldBeGreaterThan(was);
        anchors.Taken.ShouldBe(0);
    }

    [AvaloniaFact]
    public void A_finger_held_on_bare_canvas_asks_for_the_module_list_there()
    {
        var (editor, _) = Editing(Pair(out _, out _));
        var finger = Finger();
        var at = new Point(300, 600);
        Point? asked = null;

        editor.Gestures.MenuRequested += (_, graph) => asked = graph;

        editor.Fingers.Down(editor, finger, at, 1_000);
        editor.Fingers.Held();
        editor.Fingers.Up(editor, finger, at, 2_000);

        asked.ShouldBe(editor.View.ToGraph(at));
    }

    [AvaloniaFact]
    public void A_socket_tip_a_held_finger_brought_up_comes_down_when_it_lifts()
    {
        var (editor, window) = Editing(Pair(out _, out var sine));
        var finger = Finger();
        var output = On(editor, NodeGeometry.OutputPort(sine, 0));

        editor.Fingers.Down(editor, finger, On(editor, Body(sine)), 1_000);
        editor.Fingers.Held();
        editor.Fingers.Move(editor, finger, output);
        Settle(window);

        ToolTip.GetIsOpen(editor).ShouldBeTrue("sliding onto the socket should say what it is");

        editor.Fingers.Up(editor, finger, output, 2_000);
        Settle(window);

        ToolTip.GetIsOpen(editor).ShouldBeFalse();
    }

    /// <summary>Zooms the view to one screen pixel a patch unit, so a test can measure a fingertip's reach in the patch.</summary>
    private static void Unzoomed(NodeEditor editor) =>
        editor.View.ZoomAt(default, Math.Log(1 / editor.View.Zoom) / Math.Log(1.12));

    [AvaloniaFact]
    public void A_wire_let_go_nearer_an_output_lands_on_the_input_beside_it()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var left = builder.Add("time", 100, 100);
        var source = builder.Add("time", 100, 400);
        var right = builder.Add("osc.sine", 0, 0);
        var rightDef = NodeCatalog.Get(right.TypeId)!;

        // An input 24 units across a gap from the left module's first output, level with it.
        var output = NodeGeometry.OutputPort(left, 0);
        right.X = output.X + 24;
        right.Y += output.Y - Geometry.InputPort(right, rightDef, 0).Y;

        var (editor, _) = Editing(builder.Patch);
        Unzoomed(editor);

        var finger = Finger();
        var from = On(editor, NodeGeometry.OutputPort(source, 0));

        // Out of the output's own hit, within a fingertip of both, nearer the output.
        var drop = On(editor, output + new Vector(11, 0));

        editor.Fingers.Down(editor, finger, from, 1_000);
        editor.Fingers.Move(editor, finger, from + new Vector(40, 0));
        editor.Fingers.Move(editor, finger, drop);
        editor.Fingers.Up(editor, finger, drop, 1_300);

        editor.History.Patch.IncomingTo(right.Id, 0).ShouldBe(new Connection(source.Id, 0, right.Id, 0));
    }

    [AvaloniaFact]
    public void Two_fingers_landing_together_on_a_patched_input_pan_and_leave_the_wire()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var clock = builder.Add("time", 100, 100);
        var sine = builder.Add("osc.sine", 500, 100);
        builder.Wire(clock, 0, sine, 0);

        var (editor, _) = Editing(builder.Patch);
        var first = Finger();
        var second = Finger();
        var at = On(editor, Geometry.InputPort(sine, NodeCatalog.Get(sine.TypeId)!, 0));
        var other = at + new Vector(150, 100);

        // The first finger slides past the slop a moment before the second lands.
        editor.Fingers.Down(editor, first, at, 1_000);
        editor.Fingers.Move(editor, first, at + new Vector(0, Fingers.Slop + 2));
        editor.Fingers.Down(editor, second, other, 1_040);

        var panWas = editor.View.Pan;
        editor.Fingers.Move(editor, first, at + new Vector(0, 80));
        editor.Fingers.Move(editor, second, other + new Vector(0, 80));
        editor.Fingers.Up(editor, first, at + new Vector(0, 80), 1_300);
        editor.Fingers.Up(editor, second, other + new Vector(0, 80), 1_310);

        editor.View.Pan.ShouldNotBe(panWas);
        editor.History.Patch.IncomingTo(sine.Id, 0).ShouldBe(new Connection(clock.Id, 0, sine.Id, 0));
        editor.History.CanUndo.ShouldBeFalse();
        editor.Gestures.Gesturing.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Two_fingers_landing_together_on_a_module_pan_and_leave_it_where_it_was()
    {
        var (editor, _) = Editing(Pair(out var clock, out _));
        var first = Finger();
        var second = Finger();
        var at = On(editor, Body(clock));
        var other = at + new Vector(150, 100);

        editor.Fingers.Down(editor, first, at, 1_000);
        editor.Fingers.Move(editor, first, at + new Vector(Fingers.Slop + 2, 0));
        editor.Fingers.Down(editor, second, other, 1_040);
        editor.Fingers.Move(editor, first, at + new Vector(80, 0));
        editor.Fingers.Move(editor, second, other + new Vector(80, 0));
        editor.Fingers.Up(editor, first, at + new Vector(80, 0), 1_300);
        editor.Fingers.Up(editor, second, other + new Vector(80, 0), 1_310);

        (clock.X, clock.Y).ShouldBe((100d, 100d));
        editor.History.CanUndo.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_held_finger_beside_an_input_turns_it_only_as_far_as_it_moves()
    {
        var (editor, _) = Editing(Pair(out _, out var sine));
        Unzoomed(editor);

        var def = NodeCatalog.Get(sine.TypeId)!;
        var port = Enumerable.Range(0, def.Inputs.Count).First(i => KnobLinking.Linkable(def.Inputs[i]));
        var was = sine.InputValues[port];
        var finger = Finger();

        // Out of the socket's own hit and below it, nearer it than the socket on the next row.
        var at = On(editor, Geometry.InputPort(sine, def, port)) + new Vector(-8, 8);

        editor.Fingers.Down(editor, finger, at, 1_000);
        editor.Fingers.Held();
        editor.Fingers.Move(editor, finger, at - new Vector(0, 1));
        editor.Fingers.Up(editor, finger, at - new Vector(0, 1), 2_000);

        sine.InputValues[port].ShouldBeGreaterThan(was);
    }

    [AvaloniaFact]
    public void A_finger_put_back_on_a_pinch_zooms_again()
    {
        var (editor, _) = Editing(Pair(out _, out _));
        var a = Finger();
        var b = Finger();
        var middle = new Point(600, 400);

        editor.Fingers.Down(editor, a, middle - new Vector(50, 0), 1_000);
        editor.Fingers.Down(editor, b, middle + new Vector(50, 0), 1_010);
        editor.Fingers.Move(editor, b, middle + new Vector(80, 0));
        editor.Fingers.Up(editor, b, middle + new Vector(80, 0), 1_100);

        // The finger left down carries the view on its own.
        var panWas = editor.View.Pan;
        editor.Fingers.Move(editor, a, middle - new Vector(30, 0));
        editor.View.Pan.ShouldNotBe(panWas);

        editor.Fingers.Down(editor, b, middle + new Vector(50, 0), 1_200);

        var zoomWas = editor.View.Zoom;
        editor.Fingers.Move(editor, b, middle + new Vector(150, 0));

        editor.View.Zoom.ShouldBeGreaterThan(zoomWas);
    }

    [AvaloniaFact]
    public void A_second_finger_on_a_held_one_pinches_and_puts_the_turned_input_back()
    {
        var (editor, _) = Editing(Pair(out _, out var sine));
        var def = NodeCatalog.Get(sine.TypeId)!;
        var port = Enumerable.Range(0, def.Inputs.Count).First(i => KnobLinking.Linkable(def.Inputs[i]));
        var was = sine.InputValues[port];
        var held = Finger();
        var second = Finger();
        var at = On(editor, Geometry.InputPort(sine, def, port));

        editor.Fingers.Down(editor, held, at, 1_000);
        editor.Fingers.Held();
        editor.Fingers.Move(editor, held, at - new Vector(0, 5));
        editor.Fingers.Down(editor, second, at + new Vector(0, 60), 1_600);

        var zoomWas = editor.View.Zoom;
        editor.Fingers.Move(editor, second, at + new Vector(0, 160));
        editor.Fingers.Up(editor, second, at + new Vector(0, 160), 1_700);
        editor.Fingers.Up(editor, held, at - new Vector(0, 5), 1_710);

        editor.View.Zoom.ShouldBeGreaterThan(zoomWas);
        sine.InputValues[port].ShouldBe(was);
        editor.History.CanUndo.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_third_finger_lifting_leaves_the_pinch_going()
    {
        var (editor, _) = Editing(Pair(out _, out _));
        var a = Finger();
        var b = Finger();
        var c = Finger();
        var middle = new Point(600, 400);

        editor.Fingers.Down(editor, a, middle - new Vector(50, 0), 1_000);
        editor.Fingers.Down(editor, b, middle + new Vector(50, 0), 1_010);
        editor.Fingers.Down(editor, c, middle + new Vector(0, 100), 1_020);
        editor.Fingers.Up(editor, c, middle + new Vector(0, 100), 1_050);

        editor.Fingers.Move(editor, b, middle + new Vector(150, 0));

        editor.View.Zoom.ShouldBeGreaterThan(1.5);
        editor.Fingers.Count.ShouldBe(2);
    }

    [AvaloniaFact]
    public void Losing_the_panning_finger_leaves_the_module_in_the_hand()
    {
        var (editor, _) = Editing(Pair(out var clock, out _));
        var carrying = Finger();
        var panning = Finger();
        var from = On(editor, Body(clock));

        editor.Fingers.Down(editor, carrying, from, 1_000);
        editor.Fingers.Move(editor, carrying, from + new Vector(40, 0));
        editor.Fingers.Down(editor, panning, from + new Vector(200, 100), 1_500);
        editor.Fingers.Lost(editor, panning);
        editor.Fingers.Move(editor, carrying, from + new Vector(80, 0));

        editor.Gestures.Carrying.ShouldBeTrue();

        editor.Fingers.Up(editor, carrying, from + new Vector(80, 0), 1_700);

        var lifted = editor.View.ToGraph(from + new Vector(80, 0));
        (clock.X + NodeGeometry.Width / 2).ShouldBe(lifted.X, 0.5);
    }

    [AvaloniaFact]
    public void The_toolbar_shows_add_and_frame_once_a_finger_touches_the_canvas()
    {
        var window = NewMainWindow();
        window.Show();
        Settle(window);

        var add = All<Button>(window).Single(b => b.Name == "add");
        var frame = All<Button>(window).Single(b => b.Name == "frame");

        add.IsVisible.ShouldBeFalse();
        frame.IsVisible.ShouldBeFalse();

        var editor = Editor(window);
        var finger = Finger();

        editor.Fingers.Down(editor, finger, new Point(20, 20), 1_000);
        editor.Fingers.Up(editor, finger, new Point(20, 20), 1_050);
        Settle(window);

        add.IsVisible.ShouldBeTrue();
        frame.IsVisible.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Add_on_the_toolbar_opens_the_module_list_in_the_middle_of_the_view()
    {
        var window = NewMainWindow();
        window.Show();
        Settle(window);

        var editor = Editor(window);
        Point? asked = null;

        editor.Gestures.MenuRequested += (_, graph) => asked = graph;

        Press(Service<Toolbar>(window).Add);

        asked.ShouldBe(editor.View.Middle);
    }
}
