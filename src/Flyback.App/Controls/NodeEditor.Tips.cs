using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// What hovering says where the canvas has no room to: a tag's note, and the whole
/// of a title or a box socket's label that is drawn cut short.
/// </summary>
public sealed partial class NodeEditor
{
    private const double HeaderSize = 12.5;

    /// <summary>What the tooltip is up for, null while it is down.</summary>
    private object? tipped;

    /// <summary>How wide a module's title may be drawn, leaving room for its tag.</summary>
    private double HeaderWidth(Rect bounds, NodeDef def) => bounds.Width - 16 - (Tagged(def) ? TagRoom : 0);

    /// <summary>
    /// Puts the tooltip up over what has one and takes it down off it. Opened by
    /// hand, because the canvas is one control: the pointer never enters anything
    /// new for the tooltip service to notice.
    /// </summary>
    private void TipOver(Point graph)
    {
        var (over, tip) = TipAt(graph);

        if (Equals(over, tipped)) return;

        tipped = over;

        if (tip is null)
        {
            ToolTip.SetIsOpen(this, false);
            ToolTip.SetTip(this, null);
            return;
        }

        ToolTip.SetTip(this, tip);
        ToolTip.SetIsOpen(this, true);
    }

    private (object? Over, string? Tip) TipAt(Point graph)
    {
        if (HitTag(graph) is { } tag) return (tag.Id, AssistantPanel.UndescribedNote);

        // A box's socket row, by the half of the box its label is drawn in.
        foreach (var (_, sockets, bounds) in Scene.Boxes())
        {
            if (!bounds.Contains(graph)) continue;

            for (var p = 0; p < sockets.Outputs.Count; p++)
                if (OnRow(NodeGeometry.GroupOutputPort(bounds, p), graph, bounds, left: false)
                    && Scene.Named(sockets.Outputs[p]) is var (label, _)
                    && CanvasText.Overflows(label, 11.5, bounds.Width - SocketLabelRoom))
                {
                    return (sockets.Outputs[p], label);
                }

            for (var p = 0; p < sockets.Inputs.Count; p++)
                if (OnRow(NodeGeometry.GroupInputPort(bounds, sockets, p), graph, bounds, left: true)
                    && Scene.Named(sockets.Inputs[p]) is var (label, _)
                    && CanvasText.Overflows(label, 11.5, bounds.Width - SocketLabelRoom))
                {
                    return (sockets.Inputs[p], label);
                }

            return (null, null);
        }

        if (Scene.HitNode(graph) is { } node && NodeCatalog.Get(node.TypeId) is { } def)
        {
            var bounds = NodeGeometry.Bounds(node, def);
            var title = node.Title(def);

            if (new Rect(bounds.X, bounds.Y, bounds.Width, NodeGeometry.HeaderHeight).Contains(graph)
                && CanvasText.Overflows(title, HeaderSize, HeaderWidth(bounds, def)))
            {
                return (node.Id, title);
            }

            // A formula too long for the body is cut on its last line.
            if (FormulaLayout.FormulaBlock(patch, node, def, bounds) is { Cut: true, Area: var area }
                && area.Contains(graph)
                && NodeCatalog.FormulaOf(node) is { } formula)
            {
                return ((node.Id, "formula"), formula);
            }
        }

        return (null, null);
    }

    private static bool OnRow(Point port, Point graph, Rect bounds, bool left) =>
        Math.Abs(graph.Y - port.Y) <= NodeGeometry.RowHeight / 2
        && (left ? graph.X < bounds.Center.X : graph.X >= bounds.Center.X);

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        tipped = null;
        ToolTip.SetIsOpen(this, false);
        ToolTip.SetTip(this, null);
    }
}
