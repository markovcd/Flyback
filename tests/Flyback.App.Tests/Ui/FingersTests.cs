using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.App.Bars;
using Flyback.App.Canvas;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Fingers on the canvas, told to it as mouse buttons (ADR-0165). Headless Avalonia
/// has no touch input, so the fingers are handed to <see cref="Fingers"/> directly,
/// in the canvas's own coordinates.
/// </summary>
public class FingersTests : UiTest
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
        editor.Fingers.Down(editor, panning, second, 1_100);

        var panWas = editor.View.Pan;
        editor.Fingers.Move(editor, panning, second + new Vector(60, 0));

        editor.View.Pan.ShouldNotBe(panWas);

        editor.Fingers.Up(editor, panning, second + new Vector(60, 0), 1_200);
        editor.Fingers.Move(editor, carrying, from + new Vector(80, 0));
        editor.Fingers.Up(editor, carrying, from + new Vector(80, 0), 1_300);

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
