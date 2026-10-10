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
        patch.Connect(source.Id, 0, sink.Id, NodeCatalog.OutputColorPort);

        var (editor, window) = Editing(patch);

        Drag(editor, window,
            Geometry.InputPort(sink, Sink, NodeCatalog.OutputColorPort),
            Geometry.InputPort(sink, Sink, NodeCatalog.OutputLeftPort));

        patch.IncomingTo(sink.Id, NodeCatalog.OutputColorPort)
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
        patch.Connect(source.Id, 0, sink.Id, NodeCatalog.OutputColorPort);

        var (editor, window) = Editing(patch);

        Drag(editor, window,
            Geometry.InputPort(sink, Sink, NodeCatalog.OutputColorPort),
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

        builder.Patch.Connect(first.Id, 0, sink.Id, NodeCatalog.OutputColorPort);

        var (editor, window) = Editing(builder.Patch);

        Drag(editor, window,
            NodeGeometry.OutputPort(second, 0),
            Geometry.InputPort(sink, Sink, NodeCatalog.OutputColorPort));

        builder.Patch.Connections.Count.ShouldBe(1);
        builder.Patch.IncomingTo(sink.Id, NodeCatalog.OutputColorPort)!.SourceNode.ShouldBe(second.Id);
    }
}
