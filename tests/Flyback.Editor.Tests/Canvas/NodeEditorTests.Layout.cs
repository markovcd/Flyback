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
    // --- laying out ---------------------------------------------------------

    /// <summary>
    /// The layout lives in the engine and is handed the editor's own dimensions,
    /// because the assistant's workbench wants the same routine and has no canvas
    /// to ask. Nothing in the type system holds the two together, so this does:
    /// a node laid out to a size it is not drawn at overlaps its neighbour, which
    /// is the exact fault the layout was written to remove.
    /// </summary>
    [AvaloniaFact]
    public void The_layout_is_told_the_size_a_node_is_actually_drawn()
    {
        var metrics = Geometry.Metrics;

        metrics.Width.ShouldBe(NodeGeometry.Width);
        metrics.HeaderHeight.ShouldBe(NodeGeometry.HeaderHeight);
        metrics.RowHeight.ShouldBe(NodeGeometry.RowHeight);
        metrics.FooterPadding.ShouldBe(NodeGeometry.FooterPadding);
        metrics.GroupPadding.ShouldBe(NodeGeometry.GroupPadding);
        metrics.GroupHandleHeight.ShouldBe(NodeGeometry.GroupHandleHeight);

        // And the derived measurements agree, which is what actually gets used.
        var node = NodeInstance.Create(Sink, 0, 0);

        metrics.Height(Sink).ShouldBe(Geometry.Height(Sink));

        for (var port = 0; port < Sink.Inputs.Count; port++)
            metrics.InputPort(Sink, port).ShouldBe(Geometry.InputPort(node, Sink, port).Y);

        // Including a shut group's, which the layout reserves the room for and
        // the editor draws — a box laid out to a size it is not drawn at is the
        // same overlap as a module laid out to one.
        var sockets = new GroupSockets(
            [new GroupSocket(node.Id, 0, IsOutput: false), new GroupSocket(node.Id, 1, IsOutput: false)],
            [new GroupSocket(node.Id, 0, IsOutput: true)]);

        var bounds = new Rect(0, 0, NodeGeometry.Width, Geometry.GroupHeight(sockets));

        metrics.GroupHeight(sockets).ShouldBe(Geometry.GroupHeight(sockets));

        for (var row = 0; row < sockets.Outputs.Count; row++)
            metrics.GroupPort(sockets, row, isOutput: true)
                .ShouldBe(NodeGeometry.GroupOutputPort(bounds, row).Y);

        for (var row = 0; row < sockets.Inputs.Count; row++)
            metrics.GroupPort(sockets, row, isOutput: false)
                .ShouldBe(Geometry.GroupInputPort(bounds, sockets, row).Y);
    }

    /// <summary>
    /// Laying out is one edit. Ctrl+Z has to put every node back at once —
    /// a button that took eleven presses to undo would be worse than no button.
    /// </summary>
    [AvaloniaFact]
    public void Laying_out_is_a_single_undo()
    {
        var patch = Presets.Drone(NodeCatalog.BuiltIn);

        // Dragged out of place first, because a preset arrives laid out
        // (ADR-0070) and there would otherwise be nothing for the button to do.
        patch.Nodes[0].X += 240;
        patch.Nodes[0].Y += 160;

        var (editor, _) = Editing(patch);

        var before = patch.Nodes.ToDictionary(n => n.Id, n => (n.X, n.Y));

        editor.Edits.Tidy();
        editor.History.Patch.Nodes
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            .ShouldContain(n => n.X != before[n.Id].X || n.Y != before[n.Id].Y, "something should have moved");

        editor.History.Undo().ShouldBeTrue();

        foreach (var node in editor.History.Patch.Nodes)
            (node.X, node.Y).ShouldBe(before[node.Id]);
    }

    /// <summary>
    /// A patch too wide for the canvas with no box to shut is said rather than
    /// shown, and nothing is moved (ADR-0092).
    /// </summary>
    /// <remarks>
    /// Coordinates are held inside the canvas, so writing that drawing would fold
    /// its far end onto the boundary and stack it, which reads as a broken layout
    /// rather than as an oversized patch.
    /// </remarks>
    [AvaloniaFact]
    public void Laying_out_a_patch_wider_than_the_canvas_says_so_and_moves_nothing()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0);
        var last = builder.Add("time", 0, 0);

        // A node and the gap after it is 304 across and the canvas is 15000, so
        // a chain of sixty is a quarter wider than there is room for.
        for (var i = 0; i < 60; i++)
        {
            var next = builder.Add("math.mul", 0, 0);
            builder.Wire(last, 0, next, 0);
            last = next;
        }

        builder.Wire(last, 0, sink, NodeCatalog.OutputLeftPort);

        var (editor, _) = Editing(builder.Patch);

        var said = string.Empty;
        editor.Reactions.Add<CanvasSaid>(notice => said = notice.Message);

        var before = builder.Patch.Nodes.ToDictionary(n => n.Id, n => (n.X, n.Y));

        editor.Edits.Tidy();

        said.ShouldContain("too big to draw");

        foreach (var node in editor.History.Patch.Nodes)
            (node.X, node.Y).ShouldBe(before[node.Id], "nothing should have moved");
    }

    /// <summary>
    /// And one too wide only because its boxes are open has boxes shut until it
    /// fits, named in what it says (ADR-0092).
    /// </summary>
    [AvaloniaFact]
    public void Laying_out_a_patch_too_wide_with_its_boxes_open_shuts_boxes_and_names_them()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0);
        NodeInstance? last = null;

        // Six chains of twelve, each boxed: open, every box is a ring round twelve
        // columns of its own, and six of those in a row want twice the canvas.
        for (var chain = 0; chain < 6; chain++)
        {
            var made = new List<NodeInstance>();

            for (var i = 0; i < 12; i++)
            {
                var node = builder.Add("math.mul", 0, 0);

                if (last is { } feeding) builder.Wire(feeding, 0, node, 0);

                made.Add(node);
                last = node;
            }

            builder.Group($"Chain {chain}", [.. made]);
        }

        builder.Wire(last!, 0, sink, NodeCatalog.OutputLeftPort);

        foreach (var group in builder.Patch.Groups!) group.Collapsed = false;

        var (editor, _) = Editing(builder.Patch);

        var said = string.Empty;
        editor.Reactions.Add<CanvasSaid>(notice => said = notice.Message);

        editor.Edits.Tidy();

        said.ShouldContain("wider than the canvas");
        said.ShouldContain("Chain ");
        said.ShouldContain("shut");

        editor.History.Patch.Groups!.ShouldContain(group => group.Collapsed);
    }

    /// <summary>And one that fits says nothing, since a patch that laid out is a patch that laid out.</summary>
    [AvaloniaFact]
    public void Laying_out_a_patch_that_fits_says_nothing()
    {
        var (editor, _) = Editing(Presets.Drone(NodeCatalog.BuiltIn));

        var said = string.Empty;
        editor.Reactions.Add<CanvasSaid>(notice => said = notice.Message);

        editor.Edits.Tidy();

        said.ShouldBeEmpty();
    }

    /// <summary>
    /// And it is only an edit to the positions: the patch still compiles to the
    /// same program, so the picture and the sound are exactly what they were.
    /// </summary>
    [AvaloniaFact]
    public void Laying_out_leaves_the_program_alone()
    {
        var patch = Presets.Sequence(NodeCatalog.BuiltIn);
        var (editor, _) = Editing(patch);

        var before = patch.CompileForVideo(NodeCatalog.BuiltIn).Program.Ops;

        editor.Edits.Tidy();

        editor.History.Patch.CompileForVideo(NodeCatalog.BuiltIn).Program.Ops.ShouldBe(before);
    }
}
