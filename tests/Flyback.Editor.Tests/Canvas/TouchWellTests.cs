using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Flyback.Editor.Notices;
using Shouldly;

namespace Flyback.Editor.Tests.Canvas;

/// <summary>
/// A value's well under a finger: a tap picks it and shows the step bar, a hold then
/// a slide turns it with a ring and a tether where it began, and a second finger
/// makes the turn fine. The fingers are handed to <see cref="Fingers"/> directly, as
/// headless Avalonia has no touch input.
/// </summary>
public class TouchWellTests : EditorTest
{
    private static Pointer Finger() => new(Pointer.GetNextFreeId(), PointerType.Touch, false);

    private Avalonia.Controls.Window? window;

    private (NodeEditor Editor, NodeInstance Sine, int Freq) Board()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var sine = builder.Add(NodeCatalog.SineTypeId, 300, 200);
        var freq = NodeCatalog.Get(sine.TypeId)!.Inputs.ToList().FindIndex(port => port.Name == "freq");
        (var editor, window) = Editing(builder.Patch);

        return (editor, sine, freq);
    }

    private static Point Well(NodeEditor editor, NodeInstance node, int port)
    {
        var def = NodeCatalog.Get(node.TypeId)!;

        return editor.GraphToScreen.Transform(ValueWell.At(Geometry.Bounds(node, def), Geometry.InputPort(node, def, port)).Center);
    }

    private static void Tap(NodeEditor editor, Point at, ulong time = 1_000)
    {
        var finger = Finger();

        editor.Fingers.Down(editor, finger, at, time);
        editor.Fingers.Up(editor, finger, at, time + 50);
    }

    private static Point Button(NodeEditor editor, int index)
    {
        var box = editor.StepBar.Box.ShouldNotBeNull();
        var width = (box.Width - 3 * StepBar.Gap) / 4;

        return new Point(box.Left + index * (width + StepBar.Gap) + width / 2, box.Center.Y);
    }

    [AvaloniaFact]
    public void A_tap_on_a_well_picks_it_and_shows_the_step_bar()
    {
        var (editor, sine, freq) = Board();

        Tap(editor, Well(editor, sine, freq));

        editor.WellFocus.Focused.ShouldBe(new SocketPick(sine.Id, freq));
        editor.StepBar.Box.ShouldNotBeNull();
        editor.Selection.Ids.ShouldBe([sine.Id]);
        Shoot(window!, "step-bar.png");
    }

    [AvaloniaFact]
    public void The_step_bar_steps_the_value_and_Fine_makes_the_steps_smaller()
    {
        var (editor, sine, freq) = Board();
        Tap(editor, Well(editor, sine, freq));
        var was = sine.InputValues[freq];

        Tap(editor, Button(editor, 3), 2_000);
        var step = sine.InputValues[freq] - was;
        step.ShouldBeGreaterThan(0);

        Tap(editor, Button(editor, 1), 3_000);
        editor.WellFocus.FineLatched.ShouldBeTrue();

        var now = sine.InputValues[freq];
        Tap(editor, Button(editor, 0), 4_000);
        (now - sine.InputValues[freq]).ShouldBeInRange(float.Epsilon, step / 2);
    }

    [AvaloniaFact]
    public void Type_on_the_step_bar_opens_the_well_for_a_value()
    {
        var (editor, sine, freq) = Board();
        Tap(editor, Well(editor, sine, freq));

        Tap(editor, Button(editor, 2), 2_000);

        editor.WellFocus.Typed.ShouldBe(string.Empty);
    }

    [AvaloniaFact]
    public void A_held_finger_slid_up_a_well_turns_it_with_a_tether_to_where_it_began()
    {
        var (editor, sine, freq) = Board();
        var finger = Finger();
        var at = Well(editor, sine, freq);
        var was = sine.InputValues[freq];

        editor.Fingers.Down(editor, finger, at, 1_000);
        editor.Fingers.Held();
        editor.Fingers.Move(editor, finger, at - new Vector(0, 30));

        sine.InputValues[freq].ShouldBeGreaterThan(was);
        editor.Dial.Tether.ShouldNotBeNull().Home.ShouldBe(at);
        Shoot(window!, "tether.png");

        editor.Fingers.Up(editor, finger, at - new Vector(0, 30), 2_000);
        editor.Dial.Turning.ShouldBeFalse();
        editor.Dial.Tether.ShouldBeNull();
    }

    [AvaloniaFact]
    public void A_second_finger_makes_a_turn_fine_rather_than_pinching()
    {
        var (editor, sine, freq) = Board();
        var turning = Finger();
        var other = Finger();
        var at = Well(editor, sine, freq);
        var zoom = editor.View.Zoom;

        editor.Fingers.Down(editor, turning, at, 1_000);
        editor.Fingers.Held();
        editor.Fingers.Down(editor, other, new Point(40, 40), 1_500);
        editor.Fingers.Move(editor, turning, at - new Vector(0, 30));

        editor.Dial.Turning.ShouldBeTrue();
        editor.Dial.Fine.ShouldBeTrue();

        editor.Fingers.Move(editor, other, new Point(90, 90));
        editor.View.Zoom.ShouldBe(zoom);

        editor.Fingers.Up(editor, other, new Point(90, 90), 2_000);
        editor.Fingers.Move(editor, turning, at - new Vector(0, 40));
        editor.Dial.Fine.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_double_tap_on_a_well_opens_it_for_typing()
    {
        var (editor, sine, freq) = Board();
        var at = Well(editor, sine, freq);

        Tap(editor, at, 1_000);
        Tap(editor, at, 1_200);

        editor.WellFocus.Typed.ShouldBe(string.Empty);
    }

    [AvaloniaFact]
    public void A_turn_by_finger_is_one_step_to_undo()
    {
        var (editor, sine, freq) = Board();
        var finger = Finger();
        var at = Well(editor, sine, freq);
        var was = sine.InputValues[freq];
        var letGo = new List<SocketPick>();
        editor.Reactions.Add<InputLetGo>(notice => letGo.Add(notice.Pick));

        editor.Fingers.Down(editor, finger, at, 1_000);
        editor.Fingers.Held();
        editor.Fingers.Move(editor, finger, at - new Vector(0, 20));
        editor.Fingers.Move(editor, finger, at - new Vector(0, 40));
        editor.Fingers.Up(editor, finger, at - new Vector(0, 40), 2_000);

        letGo.ShouldBe([new SocketPick(sine.Id, freq)]);
        editor.History.Undo().ShouldBeTrue();
        editor.History.Patch.Find(sine.Id)!.InputValues[freq].ShouldBe(was);
    }
}
