using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.App.Canvas;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Moving modules into and out of a group after it is made: a Shift-drag, a module
/// picked from the palette over a ring, and Ctrl+G over a group and loose modules.
/// </summary>
public class RegroupTests : UiTest
{
    /// <summary>An open group of three in a row, and a Time and the Output beneath it.</summary>
    private static Patch Row(out NodeGroup group, out NodeInstance first, out NodeInstance middle, out NodeInstance loose)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        first = builder.Add("osc.sine", 0, 0);
        middle = builder.Add("math.mul", 300, 0);
        var last = builder.Add("math.mul", 600, 0);
        loose = builder.Add("time", 0, 400);
        var sink = builder.Add(NodeCatalog.OutputTypeId, 900, 400);

        builder.Wire(first, 0, middle, 0)
               .Wire(middle, 0, last, 0)
               .Wire(last, 0, sink, NodeCatalog.OutputLeftPort);

        group = builder.Patch.Group([first.Id, middle.Id, last.Id]).ShouldNotBeNull();
        group.Name = "Voice";
        group.Collapsed = false;

        return builder.Patch;
    }

    private static NodeInstance Place(Patch patch, string typeId, double x, double y)
    {
        var node = NodeInstance.Create(NodeCatalog.BuiltIn.Require(typeId), x, y);

        patch.Nodes.Add(node);
        return node;
    }

    /// <summary>Inside the ring, between the first two modules.</summary>
    private static Point InsideRing => new(NodeGeometry.Width + (300 - NodeGeometry.Width) / 2, 40);

    [AvaloniaFact]
    public void Shift_dragging_a_module_onto_an_open_group_puts_it_in()
    {
        var patch = Row(out var group, out _, out _, out var loose);
        var (editor, window) = Editing(patch);

        Drag(editor, window, Body(loose), InsideRing, RawInputModifiers.Shift);

        group.Members.ShouldContain(loose.Id);
        group.Name.ShouldBe("Voice");
    }

    [AvaloniaFact]
    public void The_group_it_would_land_in_is_known_while_the_drag_is_under_way()
    {
        var patch = Row(out var group, out _, out _, out var loose);
        var (editor, window) = Editing(patch);

        window.MouseDown(Screen(editor, window, Body(loose)), MouseButton.Left);
        window.MouseMove(Screen(editor, window, InsideRing));
        Settle(window);

        editor.Gestures.Regrouping.ShouldBeFalse("without Shift a carry is a move and nothing else");
        editor.Gestures.Landing.ShouldBeNull();

        window.MouseMove(Screen(editor, window, InsideRing + new Vector(2, 0)), RawInputModifiers.Shift);
        Settle(window);

        editor.Gestures.Regrouping.ShouldBeTrue();
        editor.Gestures.Landing.ShouldBe(group);

        window.MouseUp(Screen(editor, window, InsideRing), MouseButton.Left);
        Settle(window);

        group.Members.ShouldNotContain(loose.Id, "Shift let go of before the button leaves it a move");
    }

    /// <summary>
    /// Shift on a member lights its own group, carrying it off puts the light out, and
    /// bringing it back lights it again; let go there, it stays in.
    /// </summary>
    [AvaloniaFact]
    public void A_members_own_group_is_lit_while_it_is_over_it()
    {
        var patch = Row(out var group, out var first, out _, out _);
        var (editor, window) = Editing(patch);

        // The first module, at the ring's edge: the rest of the ring does not reach it.
        var from = Body(first);
        var away = from + new Vector(0, 500);

        window.MouseDown(Screen(editor, window, from), MouseButton.Left, RawInputModifiers.Shift);
        Settle(window);

        editor.Gestures.Landing.ShouldBe(group, "pressed with Shift, a member is over its own group");
        editor.Gestures.Regrouping.ShouldBeFalse();

        window.MouseMove(Screen(editor, window, away), RawInputModifiers.Shift);
        Settle(window);

        editor.Gestures.Landing.ShouldBeNull();
        editor.Gestures.Regrouping.ShouldBeTrue();

        window.MouseMove(Screen(editor, window, from + new Vector(10, 10)), RawInputModifiers.Shift);
        Settle(window);

        editor.Gestures.Landing.ShouldBe(group, "back over where its group stood");
        editor.Gestures.Regrouping.ShouldBeFalse();

        window.MouseUp(Screen(editor, window, from + new Vector(10, 10)), MouseButton.Left, RawInputModifiers.Shift);
        Settle(window);

        group.Members.ShouldContain(first.Id);
        editor.Gestures.Landing.ShouldBeNull("the light goes out with the carry");
    }

    [AvaloniaFact]
    public void A_plain_drag_onto_an_open_group_only_moves_the_module()
    {
        var patch = Row(out var group, out _, out _, out var loose);
        var (editor, window) = Editing(patch);

        Drag(editor, window, Body(loose), InsideRing);

        group.Members.ShouldNotContain(loose.Id);
        loose.Y.ShouldBeLessThan(400);
    }

    [AvaloniaFact]
    public void Shift_dragging_a_member_off_its_ring_takes_it_out()
    {
        var patch = Row(out var group, out _, out var middle, out _);
        var (editor, window) = Editing(patch);

        Drag(editor, window, Body(middle), Body(middle) + new Vector(0, 450), RawInputModifiers.Shift);

        group.Members.ShouldNotContain(middle.Id);
        group.Members.Count.ShouldBe(2);
        group.Exposed.ShouldContain(new GroupSocket(group.Members[0], 0, IsOutput: true), "the wire it left behind crosses the edge");
    }

    [AvaloniaFact]
    public void Shift_dragging_out_of_a_box_being_looked_into_takes_the_module_out()
    {
        var patch = Row(out var group, out _, out var middle, out _);
        group.Collapsed = true;
        var (editor, window) = Editing(patch);

        editor.Selection.Peek(group);
        Settle(window);

        Drag(editor, window, Body(middle), Body(middle) + new Vector(0, 450), RawInputModifiers.Shift);

        group.Members.ShouldNotContain(middle.Id);
        editor.Selection.Peeked.ShouldBeNull("what is selected is outside the box now");
        editor.Selection.Nodes.ShouldBe([middle]);
    }

    [AvaloniaFact]
    public void Looking_into_a_box_selects_nothing()
    {
        var patch = Row(out var group, out _, out _, out _);
        group.Collapsed = true;
        var (editor, window) = Editing(patch);

        var header = editor.Selection.Scene.Boxes().Single().Bounds.TopLeft + new Vector(40, 8);

        Click(editor, window, header, count: 2);

        editor.Selection.Peeked.ShouldBe(group);
        editor.Selection.Count.ShouldBe(0);
    }

    [AvaloniaFact]
    public void Shift_dragging_a_member_of_a_group_selected_whole_carries_that_one_out()
    {
        var patch = Row(out var group, out _, out var middle, out _);
        var (editor, window) = Editing(patch);

        editor.Selection.SelectGroup(group);
        Settle(window);

        Drag(editor, window, Body(middle), Body(middle) + new Vector(0, 450), RawInputModifiers.Shift);

        group.Members.ShouldNotContain(middle.Id);
        group.Members.Count.ShouldBe(2);
    }

    [AvaloniaFact]
    public void Shift_clicking_a_member_without_moving_it_changes_nothing()
    {
        var patch = Row(out var group, out var first, out _, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, first, RawInputModifiers.Shift);

        group.Members.ShouldContain(first.Id);
        editor.History.CanUndo.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Shift_dragging_a_whole_box_moves_it_and_leaves_it_whole()
    {
        var patch = Row(out var group, out _, out _, out var loose);
        group.Collapsed = true;
        var (editor, window) = Editing(patch);

        var header = editor.Selection.Scene.Boxes().Single().Bounds.TopLeft + new Vector(40, 8);

        Drag(editor, window, header, header + new Vector(0, 300), RawInputModifiers.Shift);

        editor.History.Patch.Groups.ShouldNotBeNull().ShouldHaveSingleItem().ShouldBe(group);
        group.Members.Count.ShouldBe(3);
        group.Members.ShouldNotContain(loose.Id);
    }

    [AvaloniaFact]
    public void One_undo_takes_back_the_move_and_the_regroup_together()
    {
        var patch = Row(out _, out _, out _, out var loose);
        var (editor, window) = Editing(patch);

        Drag(editor, window, Body(loose), InsideRing, RawInputModifiers.Shift);

        editor.History.Undo().ShouldBeTrue();
        Settle(window);

        var after = editor.History.Patch;

        after.GroupOf(loose.Id).ShouldBeNull();
        after.Find(loose.Id).ShouldNotBeNull().Y.ShouldBe(400);
    }

    [AvaloniaFact]
    public void A_module_picked_over_an_open_group_lands_in_it()
    {
        var patch = Row(out var group, out _, out _, out _);
        var (editor, _) = Editing(patch);

        var added = editor.Edits.AddNode("osc.sine", InsideRing).ShouldNotBeNull();

        group.Members.ShouldContain(added.Id);
    }

    [AvaloniaFact]
    public void A_module_picked_over_bare_canvas_is_in_no_group()
    {
        var patch = Row(out _, out _, out _, out _);
        var (editor, _) = Editing(patch);

        var added = editor.Edits.AddNode("osc.sine", new Point(300, 700)).ShouldNotBeNull();

        editor.History.Patch.GroupOf(added.Id).ShouldBeNull();
    }

    /// <summary>
    /// Looking into a box and adding to it keeps looking: the new module is in the box,
    /// and selected inside it.
    /// </summary>
    [AvaloniaFact]
    public void A_module_picked_inside_a_box_being_looked_into_joins_it()
    {
        var patch = Row(out var group, out _, out _, out _);
        group.Collapsed = true;
        var (editor, window) = Editing(patch);

        editor.Selection.Peek(group);
        Settle(window);

        var added = editor.Edits.AddNode("osc.sine", InsideRing).ShouldNotBeNull();
        Settle(window);

        group.Members.ShouldContain(added.Id);
        group.Collapsed.ShouldBeTrue();
        editor.Selection.Peeked.ShouldBe(group);
        editor.Selection.Nodes.ShouldBe([added]);
    }

    [AvaloniaFact]
    public void Ctrl_g_on_a_group_and_loose_modules_adds_them_to_it_under_its_name()
    {
        var patch = Row(out var group, out _, out _, out var loose);
        group.Collapsed = true;
        var (editor, window) = Editing(patch);

        var header = editor.Selection.Scene.Boxes().Single().Bounds.TopLeft + new Vector(40, 8);

        Click(editor, window, header);
        Click(editor, window, loose, RawInputModifiers.Control);

        window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control);
        Settle(window);

        var kept = editor.History.Patch.Groups.ShouldNotBeNull().ShouldHaveSingleItem();

        kept.Name.ShouldBe("Voice");
        kept.Members.Count.ShouldBe(4);
        kept.Members.ShouldContain(loose.Id);
    }

    [AvaloniaFact]
    public void Ctrl_g_on_several_groups_keeps_the_one_name_among_them()
    {
        var patch = Row(out var voice, out _, out _, out var loose);
        var left = Place(patch, "osc.sine", 0, 700);
        var right = Place(patch, "math.mul", 300, 700);
        var unnamed = patch.Group([left.Id, right.Id]).ShouldNotBeNull();

        var (editor, window) = Editing(patch);

        editor.Selection.Take([.. voice.Members, .. unnamed.Members, loose.Id]);
        editor.Selection.Announce();
        editor.Focus();

        window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control);
        Settle(window);

        var kept = editor.History.Patch.Groups.ShouldNotBeNull().ShouldHaveSingleItem();

        kept.Name.ShouldBe("Voice");
        kept.Members.Count.ShouldBe(6);
    }

    [AvaloniaFact]
    public void Ctrl_g_on_two_named_groups_makes_a_group_of_no_name()
    {
        var patch = Row(out var voice, out _, out _, out _);
        var left = Place(patch, "osc.sine", 0, 700);
        var right = Place(patch, "math.mul", 300, 700);
        var bass = patch.Group([left.Id, right.Id]).ShouldNotBeNull();
        bass.Name = "Bass";

        var (editor, window) = Editing(patch);

        editor.Selection.Take([.. voice.Members, .. bass.Members]);
        editor.Selection.Announce();
        editor.Focus();

        window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control);
        Settle(window);

        var made = editor.History.Patch.Groups.ShouldNotBeNull().ShouldHaveSingleItem();

        made.Name.ShouldBeNull("two names and no way to choose between them");
        made.Members.Count.ShouldBe(5);
    }

    [AvaloniaFact]
    public void Ctrl_g_on_a_group_and_the_output_keeps_the_group_as_it_was()
    {
        var patch = Row(out var group, out _, out _, out _);
        group.Collapsed = true;
        var (editor, window) = Editing(patch);

        var sink = patch.FirstOf(NodeCatalog.OutputTypeId).ShouldNotBeNull();
        var header = editor.Selection.Scene.Boxes().Single().Bounds.TopLeft + new Vector(40, 8);

        Click(editor, window, header);
        Click(editor, window, sink, RawInputModifiers.Control);

        window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control);
        Settle(window);

        editor.History.Patch.Groups.ShouldNotBeNull().ShouldHaveSingleItem().Name.ShouldBe("Voice");
    }
}
