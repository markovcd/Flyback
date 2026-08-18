using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The canvas. ADR-0017 chose one custom-drawn control over composed ones and
/// rests that choice on a single claim: painting and hit-testing cannot drift
/// apart, because both go through NodeGeometry. Nothing checked it until these —
/// and a drift there is the worst kind of bug this program can have, because the
/// socket is drawn where you see it and answers somewhere else.
/// </summary>
public class NodeEditorTests : UiTest
{
    private const double Wide = 900;
    private const double Tall = 700;

    private static NodeDef Sink => NodeCatalog.BuiltIn.Require(NodeCatalog.OutputTypeId);

    /// <summary>
    /// An editor showing a patch, laid out large enough that framing it does not
    /// shrink the nodes to nothing.
    /// </summary>
    private static (NodeEditor Editor, Window Window) Editing(Patch patch)
    {
        var editor = new NodeEditor { Width = Wide, Height = Tall };
        var window = Show(editor, Wide);

        editor.Patch = patch;
        Settle(window);

        return (editor, window);
    }

    /// <summary>A source with one output, and the Output block to wire it into.</summary>
    private static Patch Pair(out NodeInstance source, out NodeInstance sink)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        source = builder.Add("value", 0, 0);
        sink = builder.Add(NodeCatalog.OutputTypeId, 420, 0);

        return builder.Patch;
    }

    /// <summary>Where a point on the canvas is on the window, through the transform painting uses.</summary>
    private static Point Screen(NodeEditor editor, Window window, Point graph) =>
        editor.TranslatePoint(editor.GraphToScreen.Transform(graph), window)
        ?? throw new InvalidOperationException("the editor is not in this window");

    private static void Drag(NodeEditor editor, Window window, Point fromGraph, Point toGraph)
    {
        var from = Screen(editor, window, fromGraph);
        var to = Screen(editor, window, toGraph);

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
        Settle(window);
    }

    private static void ClickAt(NodeEditor editor, Window window, Point graph)
    {
        var at = Screen(editor, window, graph);

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }

    // --- the invariant ------------------------------------------------------

    /// <summary>
    /// Every input socket, on the module with the most of them. Pressing where a
    /// socket was painted has to start a wire on that socket — press the sixth
    /// and get the fifth, and every patch anyone builds is quietly wrong.
    /// </summary>
    [AvaloniaFact]
    public void Pressing_where_a_socket_is_painted_grabs_that_socket()
    {
        for (var port = 0; port < Sink.Inputs.Count; port++)
        {
            var patch = Pair(out var source, out var sink);
            var (editor, window) = Editing(patch);

            Drag(editor, window,
                NodeGeometry.InputPort(sink, Sink, port),
                NodeGeometry.OutputPort(source, 0));

            var landed = patch.IncomingTo(sink.Id, port);

            landed.ShouldNotBeNull($"input {port} ({Sink.Inputs[port].Name}) should have taken the wire");
            landed.SourceNode.ShouldBe(source.Id);

            for (var other = 0; other < Sink.Inputs.Count; other++)
                if (other != port)
                    patch.IncomingTo(sink.Id, other).ShouldBeNull($"input {other} should be untouched");
        }
    }

    /// <summary>The same claim from the other end: an output answers where it is drawn.</summary>
    [AvaloniaFact]
    public void Pressing_where_an_output_is_painted_grabs_that_output()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var coords = builder.Add("coord", 0, 0);
        var sink = builder.Add(NodeCatalog.OutputTypeId, 420, 0);

        var (editor, window) = Editing(builder.Patch);

        // Coordinates has four outputs, so picking the third proves the index is
        // read off the position rather than assumed to be the first.
        Drag(editor, window,
            NodeGeometry.OutputPort(coords, 2),
            NodeGeometry.InputPort(sink, Sink, NodeCatalog.OutputLeftPort));

        var wire = builder.Patch.IncomingTo(sink.Id, NodeCatalog.OutputLeftPort);

        wire.ShouldNotBeNull();
        wire.SourceNode.ShouldBe(coords.Id);
        wire.SourcePort.ShouldBe(2, "the third output is the one that was pressed");
    }

    /// <summary>
    /// Between two sockets there is node body, and pressing it drags the node. If
    /// a socket's hit area had crept outwards this would start a wire instead.
    /// </summary>
    [AvaloniaFact]
    public void Pressing_the_body_between_sockets_moves_the_node()
    {
        var patch = Pair(out _, out var sink);
        var (editor, window) = Editing(patch);

        var first = NodeGeometry.InputPort(sink, Sink, 0);
        var second = NodeGeometry.InputPort(sink, Sink, 1);
        var between = new Point(first.X + NodeGeometry.Width / 2, (first.Y + second.Y) / 2);

        var was = sink.X;
        Drag(editor, window, between, between + new Vector(60, 40));

        sink.X.ShouldBeGreaterThan(was, "the node should have moved with the pointer");
        patch.Connections.ShouldBeEmpty("dragging the body should not have wired anything");
    }

    /// <summary>
    /// Outputs are laid out above inputs, so an output's position cannot depend
    /// on how many inputs the module has. Wires would move when they must not.
    /// </summary>
    [AvaloniaFact]
    public void An_outputs_position_does_not_depend_on_the_inputs()
    {
        var few = NodeInstance.Create(NodeCatalog.BuiltIn.Require("value"), 0, 0);
        var many = NodeInstance.Create(Sink, 0, 0);

        NodeGeometry.OutputPort(few, 0).Y.ShouldBe(NodeGeometry.OutputPort(many, 0).Y);
    }

    // --- re-patching --------------------------------------------------------

    /// <summary>
    /// ADR-0017 calls this out as what the one-control design made cheap:
    /// dragging a connected input picks the wire up by its far end rather than
    /// starting a new one. Broken, every attempted re-patch deletes a wire.
    /// </summary>
    [AvaloniaFact]
    public void Dragging_a_connected_input_moves_the_wire_rather_than_dropping_it()
    {
        var patch = Pair(out var source, out var sink);
        patch.Connect(source.Id, 0, sink.Id, NodeCatalog.OutputColourPort);

        var (editor, window) = Editing(patch);

        Drag(editor, window,
            NodeGeometry.InputPort(sink, Sink, NodeCatalog.OutputColourPort),
            NodeGeometry.InputPort(sink, Sink, NodeCatalog.OutputLeftPort));

        patch.IncomingTo(sink.Id, NodeCatalog.OutputColourPort)
            .ShouldBeNull("the wire should have left the socket it was picked up from");

        var moved = patch.IncomingTo(sink.Id, NodeCatalog.OutputLeftPort);

        moved.ShouldNotBeNull("and landed on the one it was dropped on");
        moved.SourceNode.ShouldBe(source.Id, "still coming from where it always came from");

        patch.Connections.Count.ShouldBe(1, "moving a wire should not make a second");
    }

    /// <summary>
    /// Dropped on nothing, the wire is gone — which is how an input is unplugged,
    /// and is why the disconnect happens when it is picked up rather than when it
    /// lands.
    /// </summary>
    [AvaloniaFact]
    public void Dragging_a_connected_input_into_space_unplugs_it()
    {
        var patch = Pair(out var source, out var sink);
        patch.Connect(source.Id, 0, sink.Id, NodeCatalog.OutputColourPort);

        var (editor, window) = Editing(patch);

        Drag(editor, window,
            NodeGeometry.InputPort(sink, Sink, NodeCatalog.OutputColourPort),
            new Point(sink.X + 100, sink.Y + 420));

        patch.Connections.ShouldBeEmpty();
    }

    /// <summary>An input takes one wire, so a second onto it replaces the first.</summary>
    [AvaloniaFact]
    public void A_second_wire_into_one_input_replaces_the_first()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var first = builder.Add("value", 0, 0);
        var second = builder.Add("value", 0, 260);
        var sink = builder.Add(NodeCatalog.OutputTypeId, 420, 0);

        builder.Patch.Connect(first.Id, 0, sink.Id, NodeCatalog.OutputColourPort);

        var (editor, window) = Editing(builder.Patch);

        Drag(editor, window,
            NodeGeometry.OutputPort(second, 0),
            NodeGeometry.InputPort(sink, Sink, NodeCatalog.OutputColourPort));

        builder.Patch.Connections.Count.ShouldBe(1);
        builder.Patch.IncomingTo(sink.Id, NodeCatalog.OutputColourPort)!.SourceNode.ShouldBe(second.Id);
    }

    // --- the sink is not deletable ------------------------------------------

    /// <summary>
    /// Delete on the Output does nothing at all — including not clearing the
    /// selection, which would take its settings panel away with it (ADR-0037).
    /// </summary>
    [AvaloniaFact]
    public void Delete_does_nothing_to_the_output_and_leaves_it_selected()
    {
        var patch = Pair(out _, out var sink);
        var (editor, window) = Editing(patch);

        ClickAt(editor, window, Body(sink));
        editor.SelectedNode.ShouldBe(sink);

        editor.DeleteSelected();
        Settle(window);

        patch.Nodes.ShouldContain(sink, "the Output cannot be removed");
        editor.SelectedNode.ShouldBe(sink, "and stays selected, or its panel would vanish");
    }

    [AvaloniaFact]
    public void Delete_removes_anything_else()
    {
        var patch = Pair(out var source, out _);
        var (editor, window) = Editing(patch);

        ClickAt(editor, window, Body(source));
        editor.SelectedNode.ShouldBe(source);

        editor.DeleteSelected();
        Settle(window);

        patch.Nodes.ShouldNotContain(source);
    }

    /// <summary>The middle of a node's header, which is body rather than socket.</summary>
    private static Point Body(NodeInstance node) =>
        new(node.X + NodeGeometry.Width / 2, node.Y + NodeGeometry.HeaderHeight / 2);
}
