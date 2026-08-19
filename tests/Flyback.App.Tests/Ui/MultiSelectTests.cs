using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Selecting more than one module at a time, which is what a copy has to be
/// built on: nothing can be copied out of a selection that holds one thing.
/// </summary>
/// <remarks>
/// The interesting case throughout is a plain press on a module already part of
/// a larger selection. It cannot be answered when the button goes down —
/// collapsing there would make a group impossible to drag by one of its own
/// members, and not collapsing would make one impossible to pick apart — so it
/// is answered on the way up, and half of what is below is about that.
/// </remarks>
public class MultiSelectTests : UiTest
{
    private const double Wide = 1200;
    private const double Tall = 800;

    /// <summary>Three modules well apart, so that a click lands on exactly one of them.</summary>
    private static Patch Three(out NodeInstance a, out NodeInstance b, out NodeInstance c)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        a = builder.Add("value", 0, 0);
        b = builder.Add("time", 0, 240);
        c = builder.Add("coord", 0, 480);
        builder.Add(NodeCatalog.OutputTypeId, 520, 240);

        return builder.Patch;
    }

    private static (NodeEditor Editor, Window Window) Editing(Patch patch)
    {
        var editor = new NodeEditor { Width = Wide, Height = Tall };
        var window = Show(editor, Wide);

        editor.Patch = patch;
        Settle(window);

        return (editor, window);
    }

    /// <summary>The middle of a module's title bar — somewhere no socket is.</summary>
    private static Point Body(NodeInstance node) =>
        new(node.X + NodeGeometry.Width / 2, node.Y + NodeGeometry.HeaderHeight / 2);

    private static Point Screen(NodeEditor editor, Window window, Point graph) =>
        editor.TranslatePoint(editor.GraphToScreen.Transform(graph), window)
        ?? throw new InvalidOperationException("the editor is not in this window");

    private static void Click(
        NodeEditor editor, Window window, Point graph, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var at = Screen(editor, window, graph);

        window.MouseDown(at, MouseButton.Left, modifiers);
        window.MouseUp(at, MouseButton.Left, modifiers);
        Settle(window);
    }

    private static void Drag(NodeEditor editor, Window window, Point fromGraph, Point toGraph)
    {
        var from = Screen(editor, window, fromGraph);
        var to = Screen(editor, window, toGraph);

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
        Settle(window);
    }

    private static string[] Selected(NodeEditor editor) =>
        [.. editor.SelectedNodes.Select(n => n.TypeId).Order()];

    // --- what a click does --------------------------------------------------

    [AvaloniaFact]
    public void A_plain_click_selects_exactly_one_module()
    {
        var patch = Three(out var a, out var b, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, Body(a));
        Selected(editor).ShouldBe(["value"]);

        Click(editor, window, Body(b));
        Selected(editor).ShouldBe(["time"], "a plain click replaces the selection rather than adding to it");
    }

    [AvaloniaFact]
    public void Control_click_adds_to_the_selection()
    {
        var patch = Three(out var a, out var b, out var c);
        var (editor, window) = Editing(patch);

        Click(editor, window, Body(a));
        Click(editor, window, Body(b), RawInputModifiers.Control);
        Click(editor, window, Body(c), RawInputModifiers.Control);

        Selected(editor).ShouldBe(["coord", "time", "value"]);
    }

    /// <summary>Command as well as Control, so the gesture is the machine's own.</summary>
    [AvaloniaFact]
    public void Command_click_adds_to_the_selection_too()
    {
        var patch = Three(out var a, out var b, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, Body(a));
        Click(editor, window, Body(b), RawInputModifiers.Meta);

        Selected(editor).ShouldBe(["time", "value"]);
    }

    [AvaloniaFact]
    public void Control_click_on_a_selected_module_takes_it_back_out()
    {
        var patch = Three(out var a, out var b, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, Body(a));
        Click(editor, window, Body(b), RawInputModifiers.Control);
        Click(editor, window, Body(a), RawInputModifiers.Control);

        Selected(editor).ShouldBe(["time"]);
    }

    /// <summary>
    /// The inspector has room for one module, so one of the selection is the one
    /// it is about — the last the pointer named.
    /// </summary>
    [AvaloniaFact]
    public void The_inspector_follows_the_module_last_clicked()
    {
        var patch = Three(out var a, out var b, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, Body(a));
        editor.SelectedNode.ShouldNotBeNull().TypeId.ShouldBe("value");

        Click(editor, window, Body(b), RawInputModifiers.Control);
        editor.SelectedNode.ShouldNotBeNull().TypeId.ShouldBe("time");

        // And taking the focused one back out moves the focus rather than
        // leaving the panel showing a module that is no longer selected.
        Click(editor, window, Body(b), RawInputModifiers.Control);
        editor.SelectedNode.ShouldNotBeNull().TypeId.ShouldBe("value");
    }

    [AvaloniaFact]
    public void Clicking_the_empty_canvas_clears_the_selection_and_control_clicking_it_does_not()
    {
        var patch = Three(out var a, out var b, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, Body(a));
        Click(editor, window, Body(b), RawInputModifiers.Control);

        // The gap between the two columns of the patch: on screen, and nothing
        // is there. Somewhere off the canvas entirely would prove nothing,
        // since the press would never reach the editor at all.
        var nowhere = new Point(350, 240);

        Click(editor, window, nowhere, RawInputModifiers.Control);
        Selected(editor).ShouldBe(["time", "value"], "the gesture was about adding, and it found nothing");

        Click(editor, window, nowhere);
        Selected(editor).ShouldBeEmpty();
    }

    // --- the deferred click -------------------------------------------------

    [AvaloniaFact]
    public void Clicking_one_module_of_a_group_picks_it_out_of_the_group()
    {
        var patch = Three(out var a, out var b, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, Body(a));
        Click(editor, window, Body(b), RawInputModifiers.Control);

        // Pressed and released without moving, which is a click and not a drag.
        Click(editor, window, Body(a));

        Selected(editor).ShouldBe(["value"]);
    }

    [AvaloniaFact]
    public void Dragging_one_module_of_a_group_moves_all_of_them()
    {
        var patch = Three(out var a, out var b, out var c);
        var (editor, window) = Editing(patch);

        Click(editor, window, Body(a));
        Click(editor, window, Body(b), RawInputModifiers.Control);
        Click(editor, window, Body(c), RawInputModifiers.Control);

        var before = patch.Nodes.ToDictionary(n => n.Id, n => (n.X, n.Y));
        var from = Body(a);

        Drag(editor, window, from, from + new Vector(120, 60));

        // All three moved, by the same amount, keeping the shape they had.
        foreach (var node in new[] { a, b, c })
        {
            node.X.ShouldBe(before[node.Id].X + 120, 0.001);
            node.Y.ShouldBe(before[node.Id].Y + 60, 0.001);
        }

        Selected(editor).ShouldBe(["coord", "time", "value"], "a drag does not pick one out of the group");
    }

    [AvaloniaFact]
    public void Dragging_a_group_is_one_undo()
    {
        var patch = Three(out var a, out var b, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, Body(a));
        Click(editor, window, Body(b), RawInputModifiers.Control);

        var before = patch.Nodes.ToDictionary(n => n.Id, n => (n.X, n.Y));
        var from = Body(a);

        Drag(editor, window, from, from + new Vector(80, 0));
        editor.Undo().ShouldBeTrue();

        foreach (var node in editor.Patch.Nodes)
            (node.X, node.Y).ShouldBe(before[node.Id]);
    }

    /// <summary>
    /// Modules the pointer never touched are left alone. Dragging a group is the
    /// one gesture where that could quietly go wrong, and it would go wrong
    /// everywhere at once.
    /// </summary>
    [AvaloniaFact]
    public void Dragging_a_group_leaves_everything_else_where_it_was()
    {
        var patch = Three(out var a, out var b, out var c);
        var (editor, window) = Editing(patch);

        Click(editor, window, Body(a));
        Click(editor, window, Body(b), RawInputModifiers.Control);

        var stayed = (c.X, c.Y);
        var from = Body(a);

        Drag(editor, window, from, from + new Vector(100, 100));

        (c.X, c.Y).ShouldBe(stayed);
    }

    // --- deleting -----------------------------------------------------------

    [AvaloniaFact]
    public void Delete_removes_the_whole_selection_in_one_step()
    {
        var patch = Three(out var a, out var b, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, Body(a));
        Click(editor, window, Body(b), RawInputModifiers.Control);

        editor.DeleteSelected();

        patch.Nodes.ShouldNotContain(n => n.TypeId == "value" || n.TypeId == "time");
        editor.SelectedNodes.ShouldBeEmpty();

        editor.Undo().ShouldBeTrue();
        editor.Patch.Nodes.Count(n => n.TypeId is "value" or "time")
            .ShouldBe(2, "one gesture removed both, so one undo brings both back");
    }

    /// <summary>
    /// The Output cannot be deleted, and a selection holding it is not a reason
    /// to refuse the rest — nor to lose it from the selection afterwards.
    /// </summary>
    [AvaloniaFact]
    public void Deleting_a_selection_that_holds_the_output_takes_everything_else()
    {
        var patch = Three(out var a, out _, out _);
        var (editor, window) = Editing(patch);

        var sink = patch.Output;

        Click(editor, window, Body(a));
        Click(editor, window, Body(sink), RawInputModifiers.Control);

        editor.DeleteSelected();

        patch.Nodes.ShouldNotContain(n => n.TypeId == "value");
        patch.FirstOf(NodeCatalog.OutputTypeId).ShouldNotBeNull();
        Selected(editor).ShouldBe([NodeCatalog.OutputTypeId]);
    }

    // --- everything else that names a selection -----------------------------

    /// <summary>
    /// Adding a module selects it, rather than joining it to whatever was
    /// selected — the palette places one thing and that is what you are then
    /// working on.
    /// </summary>
    [AvaloniaFact]
    public void Adding_a_module_makes_it_the_whole_selection()
    {
        var patch = Three(out var a, out var b, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, Body(a));
        Click(editor, window, Body(b), RawInputModifiers.Control);

        editor.AddNode("math.mul").ShouldNotBeNull();

        Selected(editor).ShouldBe(["math.mul"]);
    }

    /// <summary>
    /// Laying the patch out keeps the selection, because it is the same modules
    /// in different places — and a button that cleared what you had picked would
    /// be a poor thing to press mid-edit.
    /// </summary>
    [AvaloniaFact]
    public void Laying_out_keeps_whatever_was_selected()
    {
        var patch = Three(out var a, out var b, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, Body(a));
        Click(editor, window, Body(b), RawInputModifiers.Control);

        editor.Tidy();

        Selected(editor).ShouldBe(["time", "value"]);
    }

    /// <summary>
    /// Undo restores the patch as a fresh set of objects, so the selection can
    /// only survive by id. What it named still exists, so it should.
    /// </summary>
    [AvaloniaFact]
    public void The_selection_survives_an_undo_that_kept_the_modules()
    {
        var patch = Three(out var a, out var b, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, Body(a));
        Click(editor, window, Body(b), RawInputModifiers.Control);

        var from = Body(a);
        Drag(editor, window, from, from + new Vector(60, 0));

        editor.Undo().ShouldBeTrue();

        Selected(editor).ShouldBe(["time", "value"]);
    }

    // --- selecting everything -----------------------------------------------

    /// <summary>
    /// The Output comes too. It is on the canvas, and this is not a gesture that
    /// does anything to it — what follows already knows to leave it alone.
    /// </summary>
    [AvaloniaFact]
    public void Select_all_takes_every_module_including_the_output()
    {
        var patch = Three(out _, out _, out _);
        var (editor, _) = Editing(patch);

        editor.SelectAll();

        Selected(editor).ShouldBe(["coord", NodeCatalog.OutputTypeId, "time", "value"]);
        editor.SelectedNode.ShouldNotBeNull("something has to be the one the inspector is about");
    }

    /// <summary>
    /// A module whose plugin is missing is not drawn and cannot be clicked, so
    /// selecting it would be the one way to drag or delete something invisible.
    /// </summary>
    [AvaloniaFact]
    public void Select_all_leaves_out_what_the_canvas_cannot_draw()
    {
        var patch = Three(out _, out _, out _);
        patch.Nodes.Add(new NodeInstance { Id = Guid.NewGuid(), TypeId = "nobody.knows", X = 40, Y = 40 });

        var (editor, _) = Editing(patch);

        editor.SelectAll();

        Selected(editor).ShouldNotContain("nobody.knows");
        editor.SelectedNodes.Count.ShouldBe(4);
    }

    [AvaloniaFact]
    public void Select_all_on_an_empty_canvas_selects_the_output_and_nothing_else()
    {
        var (editor, _) = Editing(new Patch());

        editor.SelectAll();

        Selected(editor).ShouldBe([NodeCatalog.OutputTypeId], "every patch has one (ADR-0037)");
    }

    /// <summary>
    /// Selecting everything and pressing Delete clears the patch down to the
    /// Output, which the graph refuses to remove. The two gestures already agree
    /// about that without select-all having to know which was coming.
    /// </summary>
    [AvaloniaFact]
    public void Select_all_and_delete_leaves_the_output_standing()
    {
        var patch = Three(out _, out _, out _);
        var (editor, _) = Editing(patch);

        editor.SelectAll();
        editor.DeleteSelected();

        patch.Nodes.Select(n => n.TypeId).ShouldBe([NodeCatalog.OutputTypeId]);
    }

    [AvaloniaFact]
    public void Control_a_is_the_gesture_and_the_canvas_takes_it()
    {
        var patch = Three(out var a, out _, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, Body(a));
        Selected(editor).Length.ShouldBe(1);

        var pressed = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.A,
            KeyModifiers = KeyModifiers.Control,
            Source = editor,
        };

        editor.RaiseEvent(pressed);

        pressed.Handled.ShouldBeTrue();
        Selected(editor).ShouldBe(["coord", NodeCatalog.OutputTypeId, "time", "value"]);
    }

    /// <summary>Plain A is not the gesture — it would fire on anyone typing.</summary>
    [AvaloniaFact]
    public void A_on_its_own_selects_nothing()
    {
        var patch = Three(out _, out _, out _);
        var (editor, _) = Editing(patch);

        var pressed = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.A,
            KeyModifiers = KeyModifiers.None,
            Source = editor,
        };

        editor.RaiseEvent(pressed);

        pressed.Handled.ShouldBeFalse();
        editor.SelectedNodes.ShouldBeEmpty();
    }
}
