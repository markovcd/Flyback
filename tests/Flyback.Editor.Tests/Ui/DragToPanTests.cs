using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Editor.Canvas;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Editor.Tests.Ui;

/// <summary>
/// Drag to pan (ADR-0182): the left button drags empty canvas to move the view, and
/// the right one draws the rubber band there or, clicked, opens the module list.
/// </summary>
public class DragToPanTests : EditorTest
{
    private static Patch Row(out NodeInstance a)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        a = builder.Add("value", 0, 0);
        builder.Add("time", 0, 200);
        builder.Add(NodeCatalog.OutputTypeId, 700, 200);

        return builder.Patch;
    }

    private (NodeEditor Editor, Window Window) Panning(out NodeInstance a)
    {
        var (editor, window) = Editing(Row(out a));
        editor.Gestures.DragToPan = true;

        return (editor, window);
    }

    private static void Sweep(NodeEditor editor, Window window, Point fromGraph, Point toGraph, MouseButton button)
    {
        window.MouseDown(Screen(editor, window, fromGraph), button);
        window.MouseMove(Screen(editor, window, toGraph));
        window.MouseUp(Screen(editor, window, toGraph), button);
        Settle(window);
    }

    private static Point Origin(NodeEditor editor) => editor.GraphToScreen.Transform(new Point(0, 0));

    /// <summary>How far across the screen a drag between two points of the patch goes, at the view's zoom.</summary>
    private static Vector Across(NodeEditor editor, Point fromGraph, Point toGraph) =>
        editor.GraphToScreen.Transform(toGraph) - editor.GraphToScreen.Transform(fromGraph);

    [AvaloniaFact]
    public void Dragging_empty_canvas_moves_the_view_and_selects_nothing()
    {
        var (editor, window) = Panning(out _);
        var before = Origin(editor);
        var across = Across(editor, new Point(-40, -40), new Point(240, 300));

        Sweep(editor, window, new Point(-40, -40), new Point(240, 300), MouseButton.Left);

        Origin(editor).ShouldBe(before + across);
        editor.Selection.Nodes.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void Dragging_empty_canvas_keeps_the_selection()
    {
        var (editor, window) = Panning(out var a);
        Click(editor, window, a);

        Sweep(editor, window, new Point(-40, -40), new Point(240, 300), MouseButton.Left);

        Selected(editor).ShouldBe(["value"]);
    }

    [AvaloniaFact]
    public void Clicking_empty_canvas_clears_the_selection()
    {
        var (editor, window) = Panning(out var a);
        Click(editor, window, a);

        Sweep(editor, window, new Point(-40, -40), new Point(-40, -40), MouseButton.Left);

        editor.Selection.Nodes.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void Dragging_a_module_still_carries_it()
    {
        var (editor, window) = Panning(out var a);
        var body = new Point(a.X + NodeGeometry.Width / 2, a.Y + NodeGeometry.HeaderHeight / 2);

        Sweep(editor, window, body, body + new Vector(90, 30), MouseButton.Left);

        a.X.ShouldBe(90, 0.001);
        a.Y.ShouldBe(30, 0.001);
    }

    [AvaloniaFact]
    public void Right_dragging_empty_canvas_selects_and_opens_no_list()
    {
        var (editor, window) = Panning(out _);
        var before = Origin(editor);
        var asked = 0;
        editor.Gestures.MenuRequested += (_, _) => asked++;

        Sweep(editor, window, new Point(-40, -40), new Point(240, 300), MouseButton.Right);

        Selected(editor).ShouldBe(["time", "value"]);
        asked.ShouldBe(0);
        Origin(editor).ShouldBe(before);
    }

    [AvaloniaFact]
    public void Right_clicking_empty_canvas_opens_the_list_there_and_keeps_the_selection()
    {
        var (editor, window) = Panning(out var a);
        Click(editor, window, a);
        Point? asked = null;
        editor.Gestures.MenuRequested += (_, at) => asked = at;

        // Left of the row, wandering less than a click may.
        Sweep(editor, window, new Point(-30, 150), new Point(-29, 151), MouseButton.Right);

        asked.ShouldNotBeNull();
        asked.Value.X.ShouldBe(-30, 0.5);
        asked.Value.Y.ShouldBe(150, 0.5);
        Selected(editor).ShouldBe(["value"]);
    }

    [AvaloniaFact]
    public void The_middle_button_still_pans()
    {
        var (editor, window) = Panning(out _);
        var before = Origin(editor);
        var across = Across(editor, new Point(-40, -40), new Point(240, 300));

        Sweep(editor, window, new Point(-40, -40), new Point(240, 300), MouseButton.Middle);

        Origin(editor).ShouldBe(before + across);
    }

    /// <summary>A finger keeps its own gestures (ADR-0165): one drags a band, two pan, and holding asks for the list.</summary>
    [AvaloniaFact]
    public void A_finger_is_not_changed_by_it()
    {
        var (editor, _) = Panning(out _);
        var finger = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, false);
        var from = editor.GraphToScreen.Transform(new Point(-40, -40));
        var to = editor.GraphToScreen.Transform(new Point(240, 300));
        var before = Origin(editor);

        editor.Fingers.Down(editor, finger, from, 1_000);
        editor.Fingers.Move(editor, finger, to);
        editor.Fingers.Up(editor, finger, to, 1_100);

        Selected(editor).ShouldBe(["time", "value"]);
        Origin(editor).ShouldBe(before);

        Point? asked = null;
        editor.Gestures.MenuRequested += (_, graph) => asked = graph;
        var held = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, false);
        var bare = editor.GraphToScreen.Transform(new Point(-30, 150));

        editor.Fingers.Down(editor, held, bare, 3_000);
        editor.Fingers.Held();

        asked.ShouldNotBeNull("the list opens while the finger is still down");
    }
}
