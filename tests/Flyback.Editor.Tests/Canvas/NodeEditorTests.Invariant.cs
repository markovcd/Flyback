using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Flyback.Editor.Canvas;
using Flyback.Editor.Notices;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Shouldly;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Tests.Canvas;

public partial class NodeEditorTests
{
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
                Geometry.InputPort(sink, Sink, port),
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
            Geometry.InputPort(sink, Sink, NodeCatalog.OutputLeftPort));

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

        var first = Geometry.InputPort(sink, Sink, 0);
        var second = Geometry.InputPort(sink, Sink, 1);
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
}
