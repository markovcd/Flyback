using Avalonia;
using Avalonia.Controls;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// What hovering says where the canvas has no room to: a socket's help, a tag's note,
/// a mark's offer, and the whole of a title or a label drawn cut short.
/// </summary>
/// <remarks>
/// Opened by hand, because the canvas is one control: the pointer never enters
/// anything new for the tooltip service to notice.
/// </remarks>
internal sealed class CanvasTips(
    CanvasHistory history,
    CanvasSelection selection,
    RemapMarks marks,
    UndescribedTags tags)
{
    /// <summary>What the tooltip is up for, null while it is down.</summary>
    private object? tipped;

    private Patch Patch => history.Patch;

    /// <summary>Puts the tooltip up over what has one and takes it down off what has none.</summary>
    public void Over(Control canvas, Point graph)
    {
        var (over, tip) = At(graph);

        if (Equals(over, tipped)) return;

        tipped = over;

        if (tip is null)
        {
            Down(canvas);
            return;
        }

        ToolTip.SetTip(canvas, tip);
        ToolTip.SetIsOpen(canvas, true);
    }

    /// <summary>Takes the tooltip down, whatever it was up for.</summary>
    public void Down(Control canvas)
    {
        tipped = null;
        ToolTip.SetIsOpen(canvas, false);
        ToolTip.SetTip(canvas, null);
    }

    private (object? Over, string? Tip) At(Point graph)
    {
        if (tags.Hit(graph) is { } tag) return (tag.Id, AssistantPanel.UndescribedNote);

        if (marks.At(graph) is var (wire, _)) return (wire, "Fit the ranges: put an Auto remap in this wire");

        var scene = selection.Scene;

        if (scene.HitPort(graph, out var portNode, out var portIndex, out var isOutput)
            && Patch.Find(portNode) is { } owner
            && NodeCatalog.Get(owner.TypeId) is { } ownerDef
            && (isOutput ? ownerDef.Outputs : ownerDef.Inputs) is var ports
            && portIndex >= 0 && portIndex < ports.Count)
        {
            var said = SocketTip(owner, ownerDef, portIndex, isOutput);
            return ((portNode, portIndex, isOutput, said), said);
        }

        // A box's socket row, by the half of the box its label is drawn in.
        foreach (var (_, sockets, bounds) in scene.Boxes())
        {
            if (!bounds.Contains(graph)) continue;

            for (var p = 0; p < sockets.Outputs.Count; p++)
                if (OnRow(NodeGeometry.GroupOutputPort(bounds, p), graph, bounds, left: false)
                    && BoxRowTip(scene, sockets.Outputs[p], bounds) is { } said)
                {
                    return ((sockets.Outputs[p], said), said);
                }

            for (var p = 0; p < sockets.Inputs.Count; p++)
                if (OnRow(NodeGeometry.GroupInputPort(bounds, sockets, p), graph, bounds, left: true)
                    && BoxRowTip(scene, sockets.Inputs[p], bounds) is { } said)
                {
                    return ((sockets.Inputs[p], said), said);
                }

            return (null, null);
        }

        if (scene.HitNode(graph) is { } node && NodeCatalog.Get(node.TypeId) is { } def)
        {
            var bounds = NodeGeometry.Bounds(node, def);
            var title = node.Title(def);

            if (new Rect(bounds.X, bounds.Y, bounds.Width, NodeGeometry.HeaderHeight).Contains(graph)
                && CanvasText.Overflows(title, CanvasPainter.HeaderSize, CanvasPainter.HeaderWidth(bounds, tags.Tagged(def))))
            {
                return (node.Id, title);
            }

            // A formula too long for the body is cut on its last line. Only its area
            // matters here, not the ink it would be drawn in.
            if (FormulaLayout.FormulaBlock(Patch, node, def, bounds, static _ => CanvasText.ValueBrush) is { Cut: true, Area: var area }
                && area.Contains(graph)
                && NodeCatalog.FormulaOf(node) is { } formula)
            {
                return ((node.Id, "formula"), formula);
            }

            if (SocketTips.RowAt(graph, bounds, def, out var row, out var output) && SocketTip(node, def, row, output) is { } said)
                return ((node.Id, row, output, said), said);
        }

        return (null, null);
    }

    /// <summary>A socket's tooltip: its help, with what it rests at first on a compact module.</summary>
    private string? SocketTip(NodeInstance node, NodeDef def, int port, bool isOutput) =>
        NodeGeometry.Compact
            ? SocketTips.Say(Patch, node, def, port, isOutput)
            : (isOutput ? def.Outputs : def.Inputs)[port].Help;

    /// <summary>
    /// A box socket row's tooltip: its label where that is drawn cut short, and on a
    /// compact box what an input rests at.
    /// </summary>
    private string? BoxRowTip(CanvasScene scene, GroupSocket socket, Rect bounds)
    {
        if (scene.Named(socket) is not var (label, spec)) return null;

        var cut = CanvasText.Overflows(label, CanvasText.RowSize, CanvasPainter.BoxLabelRoom(bounds, resting: false)) ? label : null;
        var resting = NodeGeometry.Compact && !socket.IsOutput && Patch.Find(socket.Node) is { } node
            ? SocketTips.Resting(Patch, node, spec, socket.Port)
            : null;

        return SocketTips.Lines(cut, resting);
    }

    private static bool OnRow(Point port, Point graph, Rect bounds, bool left) =>
        Math.Abs(graph.Y - port.Y) <= NodeGeometry.RowHeight / 2
        && (left ? graph.X < bounds.Center.X : graph.X >= bounds.Center.X);
}
