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
/// Drawing several modules as one box: the gesture that makes one, what the box
/// stands in front of, and what happens to a wire that crosses it.
/// </summary>
/// <remarks>
/// Nothing here is about what a patch computes, because a group changes none of
/// it — see <see cref="NodeGroup"/>. What is worth pinning is that the canvas
/// stops drawing and stops answering for the modules a box covers, because the
/// bug this feature can have is a click reaching a module nobody can see.
/// </remarks>
public class GroupTests : UiTest
{
    private const double Wide = 1200;
    private const double Tall = 800;

    /// <summary>
    /// A chain with a module either side of the middle pair, so grouping the
    /// pair gives a boundary with one wire crossing each way.
    /// </summary>
    private static Patch Chain(
        out NodeInstance feed, out NodeInstance first, out NodeInstance second, out NodeInstance sink)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        feed = builder.Add("time", 0, 0);
        first = builder.Add("osc.sine", 300, 0);
        second = builder.Add("math.mul", 600, 0);
        sink = builder.Add(NodeCatalog.OutputTypeId, 900, 0);

        builder.Wire(feed, 0, first, 0)
               .Wire(first, 0, second, 0)
               .Wire(second, 0, sink, NodeCatalog.OutputLeftPort);

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

    private static Point Body(NodeInstance node) =>
        new(node.X + NodeGeometry.Width / 2, node.Y + NodeGeometry.HeaderHeight / 2);

    private static Point Screen(NodeEditor editor, Window window, Point graph) =>
        editor.TranslatePoint(editor.GraphToScreen.Transform(graph), window)
        ?? throw new InvalidOperationException("the editor is not in this window");

    private static void Click(
        NodeEditor editor,
        Window window,
        Point graph,
        RawInputModifiers modifiers = RawInputModifiers.None,
        int count = 1)
    {
        var at = Screen(editor, window, graph);

        for (var i = 0; i < count; i++)
        {
            window.MouseDown(at, MouseButton.Left, modifiers);
            window.MouseUp(at, MouseButton.Left, modifiers);
        }

        Settle(window);
    }

    /// <summary>Selects the two middle modules and presses Ctrl+G.</summary>
    private static NodeGroup GroupTheMiddle(
        NodeEditor editor, Window window, NodeInstance first, NodeInstance second)
    {
        Click(editor, window, Body(first));
        Click(editor, window, Body(second), RawInputModifiers.Control);

        window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control);
        Settle(window);

        return editor.Patch.Groups.ShouldNotBeNull().Single();
    }

    [AvaloniaFact]
    public void Ctrl_g_draws_the_selected_modules_as_one_box()
    {
        var patch = Chain(out _, out var first, out var second, out _);
        var (editor, window) = Editing(patch);

        var group = GroupTheMiddle(editor, window, first, second);

        group.Members.ShouldBe([first.Id, second.Id], ignoreOrder: true);
        group.Collapsed.ShouldBeTrue();

        // The patch is untouched: the box is a way of drawing it, not an edit to it.
        patch.Nodes.Count.ShouldBe(4);
        patch.Connections.Count.ShouldBe(3);
    }

    /// <summary>
    /// Ctrl+G on one module does nothing, and says so. A box round one is the
    /// module again with a second name — every socket it could show is one that
    /// module already has, in the same order.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_g_on_a_single_module_is_declined()
    {
        var patch = Chain(out _, out var first, out _, out _);
        var (editor, window) = Editing(patch);

        var said = string.Empty;
        editor.Reported += (_, message) => said = message;

        Click(editor, window, Body(first));
        window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control);
        Settle(window);

        patch.Groups.ShouldBeNull();
        said.ShouldContain("needs 2 modules");

        // And the selection is left exactly as it was, so the next thing tried
        // is tried on what was already picked.
        editor.SelectedNodes.ShouldBe([first]);
    }

    /// <summary>
    /// Selecting everything and grouping it is the case where the two rules meet:
    /// the Output is left out, and what is left still has to be enough.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_g_on_one_module_and_the_output_is_declined_too()
    {
        var patch = Chain(out _, out var first, out _, out var sink);
        var (editor, window) = Editing(patch);

        Click(editor, window, Body(first));
        Click(editor, window, Body(sink), RawInputModifiers.Control);

        window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control);
        Settle(window);

        patch.Groups.ShouldBeNull();
    }

    /// <summary>
    /// The bug this feature can have. A module under a box is not on the canvas,
    /// so a press where it used to be must not reach it — otherwise it could be
    /// dragged out from under the thing drawn over it.
    /// </summary>
    [AvaloniaFact]
    public void A_module_under_a_box_cannot_be_clicked()
    {
        var patch = Chain(out _, out var first, out var second, out _);
        var (editor, window) = Editing(patch);

        GroupTheMiddle(editor, window, first, second);

        // Where the second module's own body is — under the box, since the box is
        // drawn from the top left of the two and is narrower than they are wide.
        Click(editor, window, Body(second));

        editor.SelectedNodes.ShouldNotContain(second);
    }

    [AvaloniaFact]
    public void Pressing_the_box_selects_what_is_inside_it()
    {
        var patch = Chain(out var feed, out var first, out var second, out _);
        var (editor, window) = Editing(patch);

        var group = GroupTheMiddle(editor, window, first, second);

        Click(editor, window, Body(feed));
        editor.SelectedNodes.ShouldBe([feed]);

        Click(editor, window, BoxHeader(patch, group));

        editor.SelectedNodes.Select(n => n.Id).ShouldBe(group.Members, ignoreOrder: true);
    }

    [AvaloniaFact]
    public void Double_clicking_the_box_opens_it_and_double_clicking_the_strip_shuts_it()
    {
        var patch = Chain(out _, out var first, out var second, out _);
        var (editor, window) = Editing(patch);

        var group = GroupTheMiddle(editor, window, first, second);

        Click(editor, window, BoxHeader(patch, group), count: 2);
        group.Collapsed.ShouldBeFalse();

        // Open, the group is a dashed ring with a strip above it, and the strip
        // is where the same gesture lands.
        Click(editor, window, OpenHandle(patch, group), count: 2);
        group.Collapsed.ShouldBeTrue();
    }

    /// <summary>
    /// The whole of what makes the boundary work: a socket on a box names a
    /// module and a port, so dragging from one starts a wire on that module and
    /// everything downstream never learns a box was involved.
    /// </summary>
    [AvaloniaFact]
    public void A_wire_dragged_off_the_box_lands_on_the_module_the_socket_names()
    {
        var patch = Chain(out _, out var first, out var second, out var sink);
        var (editor, window) = Editing(patch);

        var group = GroupTheMiddle(editor, window, first, second);
        var sockets = patch.SocketsOf(group);

        sockets.Outputs.ShouldBe([new GroupSocket(second.Id, 0, IsOutput: true)]);

        var bounds = NodeGeometry.GroupBounds(patch, group, sockets);
        var from = NodeGeometry.GroupOutputPort(bounds, 0);
        var to = NodeGeometry.InputPort(sink, NodeCatalog.Require(sink.TypeId), NodeCatalog.OutputRightPort);

        window.MouseDown(Screen(editor, window, from), MouseButton.Left);
        window.MouseMove(Screen(editor, window, to));
        window.MouseUp(Screen(editor, window, to), MouseButton.Left);
        Settle(window);

        // The new wire leaves the module inside the box, not the box.
        patch.IncomingTo(sink.Id, NodeCatalog.OutputRightPort)
            .ShouldNotBeNull()
            .SourceNode.ShouldBe(second.Id);
    }

    [AvaloniaFact]
    public void Ctrl_shift_g_puts_the_modules_back_on_the_canvas()
    {
        var patch = Chain(out _, out var first, out var second, out _);
        var (editor, window) = Editing(patch);

        GroupTheMiddle(editor, window, first, second);

        window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control | RawInputModifiers.Shift);
        Settle(window);

        patch.Groups.ShouldBeNull();
        patch.Nodes.Count.ShouldBe(4);
        patch.Connections.Count.ShouldBe(3);

        // And the module it covered answers a click again.
        Click(editor, window, Body(second));
        editor.SelectedNodes.ShouldBe([second]);
    }

    /// <summary>
    /// Dragging a box moves what is inside it, which is the whole of how a box
    /// moves: it has no position of its own, so it goes where its modules go.
    /// </summary>
    [AvaloniaFact]
    public void Dragging_the_box_moves_the_modules_under_it()
    {
        var patch = Chain(out _, out var first, out var second, out _);
        var (editor, window) = Editing(patch);

        var group = GroupTheMiddle(editor, window, first, second);

        var wasFirst = new Point(first.X, first.Y);
        var wasSecond = new Point(second.X, second.Y);

        var from = BoxHeader(patch, group);
        var to = from + new Vector(120, 60);

        window.MouseDown(Screen(editor, window, from), MouseButton.Left);
        window.MouseMove(Screen(editor, window, to));
        window.MouseUp(Screen(editor, window, to), MouseButton.Left);
        Settle(window);

        // Both of them, by the same amount — the box is not a thing that can be
        // dragged out of shape.
        (first.X - wasFirst.X).ShouldBe(120, 0.5);
        (first.Y - wasFirst.Y).ShouldBe(60, 0.5);
        (second.X - wasSecond.X).ShouldBe(120, 0.5);
        (second.Y - wasSecond.Y).ShouldBe(60, 0.5);
    }

    /// <summary>
    /// A wire being dragged off a box has to leave from the socket it was
    /// grabbed at. The far end of a pending wire is a port that may be behind a
    /// box, and drawing it at the module's own position puts it somewhere the
    /// module is not — under the box, or off wherever the modules happen to sit.
    /// </summary>
    [AvaloniaFact]
    public void A_wire_being_dragged_off_a_box_leaves_from_the_box()
    {
        var patch = Chain(out _, out var first, out var second, out _);
        var (editor, window) = Editing(patch);

        var group = GroupTheMiddle(editor, window, first, second);
        var sockets = patch.SocketsOf(group);
        var bounds = NodeGeometry.GroupBounds(patch, group, sockets);

        var socket = NodeGeometry.GroupOutputPort(bounds, 0);

        window.MouseDown(Screen(editor, window, socket), MouseButton.Left);
        window.MouseMove(Screen(editor, window, socket + new Vector(80, 80)));
        Settle(window);

        // The wire is drawn from where it was grabbed, not from where the module
        // behind the socket would have put it.
        editor.PendingWireFrom.ShouldNotBeNull().ShouldBe(socket);

        window.MouseUp(Screen(editor, window, socket + new Vector(80, 80)), MouseButton.Left);
        Settle(window);
    }

    /// <summary>
    /// A socket outlives the wire that put it there, so unplugging leaves the box
    /// exactly the size and shape it was — and leaves somewhere to plug back
    /// into, which is what makes the next test possible at all.
    /// </summary>
    [AvaloniaFact]
    public void Unplugging_does_not_take_the_socket_off_the_box()
    {
        var patch = Chain(out _, out var first, out var second, out _);
        var (editor, window) = Editing(patch);

        var group = GroupTheMiddle(editor, window, first, second);
        var before = patch.SocketsOf(group);

        before.Inputs.ShouldNotBeEmpty();

        patch.Disconnect(first.Id, 0);
        editor.NotifyPatchChanged();
        Settle(window);

        patch.SocketsOf(group).Inputs.ShouldBe(before.Inputs);
        patch.SocketsOf(group).Outputs.ShouldBe(before.Outputs);
    }

    /// <summary>
    /// And so a wire can be plugged into a shut box, which it could not be while
    /// a socket existed only for as long as something was in it.
    /// </summary>
    [AvaloniaFact]
    public void A_wire_can_be_dropped_onto_an_empty_socket_of_a_shut_box()
    {
        var patch = Chain(out var feed, out var first, out var second, out _);
        var (editor, window) = Editing(patch);

        var group = GroupTheMiddle(editor, window, first, second);

        patch.Disconnect(first.Id, 0);
        editor.NotifyPatchChanged();
        Settle(window);

        var sockets = patch.SocketsOf(group);
        var bounds = NodeGeometry.GroupBounds(patch, group, sockets);

        var from = NodeGeometry.OutputPort(feed, 0);
        var to = NodeGeometry.GroupInputPort(bounds, sockets, 0);

        window.MouseDown(Screen(editor, window, from), MouseButton.Left);
        window.MouseMove(Screen(editor, window, to));
        window.MouseUp(Screen(editor, window, to), MouseButton.Left);
        Settle(window);

        // Landed on the module the socket names, not on the box.
        patch.IncomingTo(first.Id, 0).ShouldNotBeNull().SourceNode.ShouldBe(feed.Id);
    }

    /// <summary>
    /// Grouping is an edit like any other, so one press of Ctrl+Z takes it back
    /// — which works because history writes the patch out and groups are in it.
    /// </summary>
    [AvaloniaFact]
    public void One_undo_takes_a_group_back()
    {
        var patch = Chain(out _, out var first, out var second, out _);
        var (editor, window) = Editing(patch);

        GroupTheMiddle(editor, window, first, second);

        editor.Undo().ShouldBeTrue();
        Settle(window);

        editor.Patch.Groups.ShouldBeNull();
    }

    /// <summary>
    /// A box, and the strip above an open group, take the cursor a module takes.
    /// </summary>
    /// <remarks>
    /// They are taken hold of exactly as a module is — pressing either selects
    /// what is inside and the drag that follows is the ordinary one — so a
    /// pointer that went on saying "nothing here" over them was the picture
    /// disagreeing with the gesture. Compared against what a module gives rather
    /// than against a named cursor, because what matters is that the two agree.
    /// </remarks>
    [AvaloniaFact]
    public void A_box_and_the_strip_above_an_open_one_take_the_cursor_a_module_does()
    {
        var patch = Chain(out var feed, out var first, out var second, out _);
        var (editor, window) = Editing(patch);

        var group = GroupTheMiddle(editor, window, first, second);

        Hover(editor, window, Body(feed));
        var onModule = editor.Cursor.ShouldNotBeNull();

        Hover(editor, window, new Point(feed.X + 40, feed.Y + 320));
        editor.Cursor.ShouldNotBeSameAs(onModule, "bare canvas is not something to pick up");

        Hover(editor, window, BoxHeader(patch, group));
        editor.Cursor.ShouldBeSameAs(onModule);

        editor.ToggleBox(group);
        Settle(window);

        Hover(editor, window, OpenHandle(patch, group));
        editor.Cursor.ShouldBeSameAs(onModule);
    }

    private static void Hover(NodeEditor editor, Window window, Point graph)
    {
        window.MouseMove(Screen(editor, window, graph));
        Settle(window);
    }

    private static Point BoxHeader(Patch patch, NodeGroup group)
    {
        var bounds = NodeGeometry.GroupBounds(patch, group, patch.SocketsOf(group));

        return new Point(bounds.Center.X, bounds.Y + NodeGeometry.HeaderHeight / 2);
    }

    /// <summary>
    /// The strip above an open group, which is what shuts it again.
    /// </summary>
    /// <remarks>
    /// The two numbers are the editor's own padding and strip height, which are
    /// private to it. Aimed at the middle of the strip rather than at an edge, so
    /// this goes on landing if either is tuned by a few pixels.
    /// </remarks>
    private static Point OpenHandle(Patch patch, NodeGroup group)
    {
        var x = double.MaxValue;
        var y = double.MaxValue;

        foreach (var id in group.Members)
            if (patch.Find(id) is { } node)
            {
                x = Math.Min(x, node.X);
                y = Math.Min(y, node.Y);
            }

        const double padding = 24;
        const double strip = 20;

        return new Point(x - padding + NodeGeometry.Width / 2, y - padding - strip / 2);
    }
}
