using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Flyback.Editor.Notices;
using Flyback.Ui.Controls;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Flyback.Editor.Tests.Canvas;

/// <summary>
/// A turn under way moves to another input on the same module: Tab or the wheel with a
/// mouse, a second finger tapped above or below the turning one by touch. Zoomed out
/// past the rows, the input turning is ringed beside its socket, with its name on a plate.
/// </summary>
public class SocketSwitchTests : EditorTest
{
    private sealed class Free : IPointerAnchors
    {
        public IPointerAnchor? Take(Visual visual) => null;
    }

    private static Pointer Finger() => new(Pointer.GetNextFreeId(), PointerType.Touch, false);

    private (NodeEditor Editor, Window Window, NodeInstance Sine) Board(double width = 1200, double height = 800)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var sine = builder.Add(NodeCatalog.SineTypeId, 300, 200);
        var (editor, window) = Editing(builder.Patch, width, height, services => services.AddSingleton<IPointerAnchors>(new Free()));

        return (editor, window, sine);
    }

    /// <summary>The inputs of <paramref name="node"/> a turn can be on, top to bottom.</summary>
    private static List<int> Turnable(NodeEditor editor, NodeInstance node) =>
        [.. Enumerable.Range(0, NodeCatalog.Get(node.TypeId)!.Inputs.Count).Where(port => editor.Dial.Dialable(node.Id, port, out _, out _))];

    private static Point Socket(NodeEditor editor, NodeInstance node, int port) =>
        editor.GraphToScreen.Transform(Geometry.InputPort(node, NodeCatalog.Get(node.TypeId)!, port));

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        Settle(window);
    }

    [AvaloniaFact]
    public void Tab_moves_a_turn_to_the_next_input_and_Shift_Tab_back()
    {
        var (editor, window, sine) = Board();
        var turnable = Turnable(editor, sine);
        turnable.Count.ShouldBeGreaterThan(1);
        var at = Screen(editor, window, Geometry.InputPort(sine, NodeCatalog.Get(sine.TypeId)!, turnable[0]));

        window.MouseDown(at, MouseButton.Right);
        Settle(window);
        editor.Dial.Dialed.ShouldBe(new SocketPick(sine.Id, turnable[0]));

        Press(window, Key.Tab);
        editor.Dial.Dialed.ShouldBe(new SocketPick(sine.Id, turnable[1]));

        var was = sine.InputValues[turnable[1]];
        window.MouseMove(at - new Vector(0, 30), RawInputModifiers.RightMouseButton);
        Settle(window);
        sine.InputValues[turnable[1]].ShouldBeGreaterThan(was);

        Press(window, Key.Tab, RawInputModifiers.Shift);
        editor.Dial.Dialed.ShouldBe(new SocketPick(sine.Id, turnable[0]));

        window.MouseUp(at - new Vector(0, 30), MouseButton.Right);
        Settle(window);
        editor.Dial.Turning.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Each_input_switched_to_is_its_own_step_to_undo()
    {
        var (editor, window, sine) = Board();
        var turnable = Turnable(editor, sine);
        var at = Screen(editor, window, Geometry.InputPort(sine, NodeCatalog.Get(sine.TypeId)!, turnable[0]));
        var first = sine.InputValues[turnable[0]];
        var second = sine.InputValues[turnable[1]];
        var letGo = new List<SocketPick>();
        editor.Reactions.Add<InputLetGo>(notice => letGo.Add(notice.Pick));

        window.MouseDown(at, MouseButton.Right);
        window.MouseMove(at - new Vector(0, 20), RawInputModifiers.RightMouseButton);
        Press(window, Key.Tab);
        window.MouseMove(at - new Vector(0, 40), RawInputModifiers.RightMouseButton);
        window.MouseUp(at - new Vector(0, 40), MouseButton.Right);
        Settle(window);

        letGo.ShouldBe([new SocketPick(sine.Id, turnable[0]), new SocketPick(sine.Id, turnable[1])]);

        editor.History.Undo().ShouldBeTrue();
        editor.History.Patch.Find(sine.Id)!.InputValues[turnable[1]].ShouldBe(second);
        editor.History.Patch.Find(sine.Id)!.InputValues[turnable[0]].ShouldNotBe(first);

        editor.History.Undo().ShouldBeTrue();
        editor.History.Patch.Find(sine.Id)!.InputValues[turnable[0]].ShouldBe(first);
    }

    [AvaloniaFact]
    public void The_wheel_switches_a_turn_down_the_column_and_back_up_and_zooms_nothing()
    {
        var (editor, window, sine) = Board();
        var turnable = Turnable(editor, sine);
        var at = Screen(editor, window, Geometry.InputPort(sine, NodeCatalog.Get(sine.TypeId)!, turnable[0]));
        var zoom = editor.View.Zoom;

        window.MouseDown(at, MouseButton.Right);
        window.MouseWheel(at, new Vector(0, -1), RawInputModifiers.RightMouseButton);
        Settle(window);
        editor.Dial.Dialed.ShouldBe(new SocketPick(sine.Id, turnable[1]));

        // A trackpad's fractions of a notch add up to one switch.
        window.MouseWheel(at, new Vector(0, 0.5), RawInputModifiers.RightMouseButton);
        Settle(window);
        editor.Dial.Dialed.ShouldBe(new SocketPick(sine.Id, turnable[1]));

        window.MouseWheel(at, new Vector(0, 0.5), RawInputModifiers.RightMouseButton);
        Settle(window);
        editor.Dial.Dialed.ShouldBe(new SocketPick(sine.Id, turnable[0]));

        editor.View.Zoom.ShouldBe(zoom);
        window.MouseUp(at, MouseButton.Right);
    }

    [AvaloniaFact]
    public void Switching_up_from_the_top_input_goes_round_to_the_bottom()
    {
        var (editor, window, sine) = Board();
        var turnable = Turnable(editor, sine);
        var at = Screen(editor, window, Geometry.InputPort(sine, NodeCatalog.Get(sine.TypeId)!, turnable[0]));

        window.MouseDown(at, MouseButton.Right);
        Press(window, Key.Tab, RawInputModifiers.Shift);

        editor.Dial.Dialed.ShouldBe(new SocketPick(sine.Id, turnable[^1]));
        window.MouseUp(at, MouseButton.Right);
    }

    [AvaloniaFact]
    public void Zoomed_out_a_second_finger_tapped_below_the_turn_switches_it_down_and_one_above_back_up()
    {
        var (editor, window, sine) = Board(412, 700);
        ZoomOut(editor, window);

        var turnable = Turnable(editor, sine);
        var turning = Finger();
        var at = Socket(editor, sine, turnable[0]);

        editor.Fingers.Down(editor, turning, at, 1_000);
        editor.Fingers.Held();
        editor.Dial.Dialed.ShouldBe(new SocketPick(sine.Id, turnable[0]));

        var below = Finger();
        editor.Fingers.Down(editor, below, at + new Vector(60, 120), 1_500);
        editor.Fingers.Up(editor, below, at + new Vector(60, 120), 1_600);
        editor.Dial.Dialed.ShouldBe(new SocketPick(sine.Id, turnable[1]));

        Settle(window);
        Shoot(window, "switched-zoomed-out.png");

        var above = Finger();
        editor.Fingers.Down(editor, above, at - new Vector(-60, 120), 2_000);
        editor.Fingers.Up(editor, above, at - new Vector(-60, 120), 2_100);
        editor.Dial.Dialed.ShouldBe(new SocketPick(sine.Id, turnable[0]));

        editor.Fingers.Up(editor, turning, at, 3_000);
        editor.Dial.Turning.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_second_finger_that_rests_makes_the_turn_fine_and_switches_nothing()
    {
        var (editor, _, sine) = Board();
        var turnable = Turnable(editor, sine);
        var turning = Finger();
        var other = Finger();
        var at = Socket(editor, sine, turnable[0]);

        editor.Fingers.Down(editor, turning, at, 1_000);
        editor.Fingers.Held();
        editor.Fingers.Down(editor, other, at + new Vector(60, 120), 1_500);
        editor.Fingers.Up(editor, other, at + new Vector(60, 120), 2_500);

        editor.Dial.Dialed.ShouldBe(new SocketPick(sine.Id, turnable[0]));
        editor.Fingers.Up(editor, turning, at, 3_000);
    }

    [AvaloniaFact]
    public void Zoomed_out_a_right_drag_anywhere_on_a_module_turns_its_first_input()
    {
        var (editor, window, sine) = Board(412, 700);
        ZoomOut(editor, window);
        var turnable = Turnable(editor, sine);
        var middle = editor.View.OnScreen(editor.Selection.Scene.Footprint(sine)).Center;

        window.MouseDown(middle, MouseButton.Right);
        Settle(window);

        editor.Dial.Dialed.ShouldBe(new SocketPick(sine.Id, turnable[0]));
        editor.History.Patch.Find(sine.Id)!.Off.ShouldBeFalse();
        window.MouseUp(middle, MouseButton.Right);
    }

    [AvaloniaFact]
    public void Zoomed_out_a_finger_held_anywhere_on_a_module_turns_its_first_input()
    {
        var (editor, window, sine) = Board(412, 700);
        ZoomOut(editor, window);
        var turnable = Turnable(editor, sine);
        var middle = editor.View.OnScreen(editor.Selection.Scene.Footprint(sine)).Center;
        var finger = Finger();

        editor.Fingers.Down(editor, finger, middle, 1_000);
        editor.Fingers.Held();

        editor.Dial.Dialed.ShouldBe(new SocketPick(sine.Id, turnable[0]));
        editor.Fingers.Up(editor, finger, middle, 2_000);
    }

    [AvaloniaFact]
    public void A_finger_held_on_a_module_turns_the_input_on_the_row_nearest_it_and_mutes_nothing()
    {
        var (editor, _, sine) = Board();
        var turnable = Turnable(editor, sine);
        var last = turnable[^1];
        var row = Geometry.InputPort(sine, NodeCatalog.Get(sine.TypeId)!, last);
        var at = editor.GraphToScreen.Transform(new Point(sine.X + NodeGeometry.Width / 2, row.Y));
        var finger = Finger();

        editor.Fingers.Down(editor, finger, at, 1_000);
        editor.Fingers.Held();

        editor.Dial.Dialed.ShouldBe(new SocketPick(sine.Id, last));
        editor.History.Patch.Find(sine.Id)!.Off.ShouldBeFalse();
        editor.Fingers.Up(editor, finger, at, 2_000);
    }

    [AvaloniaFact]
    public void A_finger_held_on_a_module_with_no_values_mutes_nothing()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var clock = builder.Add(NodeCatalog.TimeTypeId, 300, 200);
        var (editor, _) = Editing(builder.Patch);
        var at = editor.GraphToScreen.Transform(Body(clock));
        var finger = Finger();

        editor.Fingers.Down(editor, finger, at, 1_000);
        editor.Fingers.Held();

        editor.Dial.Turning.ShouldBeFalse();
        editor.History.Patch.Find(clock.Id)!.Off.ShouldBeFalse();
        editor.Fingers.Up(editor, finger, at, 2_000);
    }

    [AvaloniaFact]
    public void Close_enough_to_read_a_right_drag_on_a_module_body_turns_nothing()
    {
        var (editor, window, sine) = Board();
        var middle = editor.View.OnScreen(editor.Selection.Scene.Footprint(sine)).Center;

        window.MouseDown(middle, MouseButton.Right);
        Settle(window);

        editor.Dial.Turning.ShouldBeFalse();
        window.MouseUp(middle, MouseButton.Right);
    }

    [AvaloniaFact]
    public void Zoomed_out_the_input_turning_is_ringed_beside_its_socket()
    {
        var (editor, window, sine) = Board(412, 700);
        ZoomOut(editor, window);
        var turnable = Turnable(editor, sine);
        var at = Screen(editor, window, Geometry.InputPort(sine, NodeCatalog.Get(sine.TypeId)!, turnable[0]));
        var before = Pixels(editor, window, UnderRing(editor, sine, turnable[0])).Count(Attention);

        window.MouseDown(at, MouseButton.Right);
        Settle(window);

        Pixels(editor, window, UnderRing(editor, sine, turnable[0])).Count(Attention).ShouldBeGreaterThan(before, "the socket is ringed in the turning color");
        Shoot(window, "socket-tag.png");

        window.MouseUp(at, MouseButton.Right);
    }

    private static void ZoomOut(NodeEditor editor, Window window)
    {
        while (!editor.View.Overview) editor.View.ZoomAt(new Point(206, 350), -1);

        Settle(window);
    }

    private static bool Attention(Avalonia.Media.Color color) => color.R > 0xC0 && color.G > 0x80 && color.B < 0x90;

    /// <summary>The bottom of the ring round a socket, below the socket's own dot, as a rectangle of the patch.</summary>
    private static Rect UnderRing(NodeEditor editor, NodeInstance node, int port)
    {
        var socket = Geometry.InputPort(node, NodeCatalog.Get(node.TypeId)!, port);
        var zoom = editor.View.Zoom;

        return new Rect(socket.X - 3 / zoom, socket.Y + (SocketTag.Radius - 1.5) / zoom, 6 / zoom, 3 / zoom);
    }
}
