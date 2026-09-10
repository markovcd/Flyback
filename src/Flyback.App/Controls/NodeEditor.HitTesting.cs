using Avalonia;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// What is under a point, and what is selected because of it.
/// </summary>
/// <remarks>
/// Linear over the modules in reverse drawing order, so the one on top answers
/// first — fine for the hundreds a patch has and not for tens of thousands
/// (ADR-0017). Every measurement is in graph space, which is why nothing here
/// needs to know the zoom.
/// </remarks>
public sealed partial class NodeEditor
{
    // --- hit testing (all in graph space) ------------------------------------

    private NodeInstance? HitNode(Point graph)
    {
        for (var i = patch.Nodes.Count - 1; i >= 0; i--)
        {
            var node = patch.Nodes[i];
            var def = NodeCatalog.Get(node.TypeId);

            // A module inside a shut box is not on the canvas, so nothing can
            // land on it. Without this a click would reach a module it cannot
            // see and drag it out from under the box drawn over it.
            if (Shut(node.Id)) continue;

            if (def is not null && NodeGeometry.Bounds(node, def).Contains(graph))
                return node;
        }

        return null;
    }

    /// <summary>
    /// Which port is under the pointer, whether it is drawn on a module or on the box
    /// standing in front of one.
    /// </summary>
    /// <remarks>
    /// A box's socket answers with the module and port it stands for, so everything
    /// downstream goes on working on the graph without learning that groups exist —
    /// the dividend of a socket being a pointer rather than a port of its own.
    /// </remarks>
    private bool HitPort(Point graph, out Guid nodeId, out int portIndex, out bool isOutput)
    {
        var tolerance = NodeGeometry.PortRadius + NodeGeometry.HitPadding;

        // Boxes first, because they are painted over the modules they stand for
        // and a click should reach whatever is on top.
        foreach (var (_, sockets, bounds) in Boxes())
        {
            for (var p = 0; p < sockets.Outputs.Count; p++)
            {
                if (!Near(NodeGeometry.GroupOutputPort(bounds, p), graph, tolerance)) continue;

                var socket = sockets.Outputs[p];
                (nodeId, portIndex, isOutput) = (socket.Node, socket.Port, true);
                return true;
            }

            for (var p = 0; p < sockets.Inputs.Count; p++)
            {
                if (!Near(NodeGeometry.GroupInputPort(bounds, sockets, p), graph, tolerance)) continue;

                var socket = sockets.Inputs[p];
                (nodeId, portIndex, isOutput) = (socket.Node, socket.Port, false);
                return true;
            }
        }

        for (var i = patch.Nodes.Count - 1; i >= 0; i--)
        {
            var node = patch.Nodes[i];
            var def = NodeCatalog.Get(node.TypeId);
            if (def is null || Shut(node.Id)) continue;

            for (var p = 0; p < def.Outputs.Count; p++)
            {
                if (!Near(NodeGeometry.OutputPort(node, p), graph, tolerance)) continue;

                (nodeId, portIndex, isOutput) = (node.Id, p, true);
                return true;
            }

            for (var p = 0; p < def.Inputs.Count; p++)
            {
                if (!Near(NodeGeometry.InputPort(node, def, p), graph, tolerance)) continue;

                (nodeId, portIndex, isOutput) = (node.Id, p, false);
                return true;
            }
        }

        (nodeId, portIndex, isOutput) = (Guid.Empty, -1, false);
        return false;
    }

    private static bool Near(Point a, Point b, double tolerance)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy <= tolerance * tolerance;
    }

    /// <summary>
    /// Selects every module on the canvas.
    /// </summary>
    /// <remarks>
    /// Every module that is drawn, which is not quite the same thing: one whose plugin
    /// is missing has no size, and putting it into a selection would be the one way to
    /// drag or delete something invisible. The Output is included, because copy leaves
    /// it out (ADR-0045) and delete refuses it, so selecting everything and pressing
    /// either does the sensible thing.
    /// </remarks>
    public void SelectAll()
    {
        var all = patch.Nodes
            .Where(node => NodeCatalog.Get(node.TypeId) is not null)
            .Select(node => node.Id)
            .ToArray();

        if (all.Length == selection.Count && all.All(selection.Contains)) return;

        selection.Clear();
        foreach (var id in all) selection.Add(id);

        // The last in the patch's own order, which is the one drawn on top —
        // the same module Toggle falls back to, for the same reason.
        focus = all.Length == 0 ? null : all[^1];

        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>
    /// Makes the selection exactly this one module, or nothing at all — what an
    /// ordinary click does, and what every caller outside the pointer handling wants.
    /// </summary>
    /// <remarks>
    /// Public because the canvas is no longer the only thing that points at a module:
    /// a caret in the code view names one too (ADR-0068). It draws as well as selects,
    /// which a caller from outside cannot.
    /// </remarks>
    public void Select(Guid? id)
    {
        if (focus == id && selection.Count == (id is null ? 0 : 1)) return;

        selection.Clear();
        if (id is { } one) selection.Add(one);

        focus = id;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>
    /// Adds a module to the selection, or takes it out again if it was already in —
    /// what Ctrl held down turns a click into. Taking the focused one out moves the
    /// focus rather than dropping it, to whatever is last in the patch's own order,
    /// which is the module drawn on top.
    /// </summary>
    private void Toggle(Guid id)
    {
        if (!selection.Add(id))
        {
            selection.Remove(id);

            if (focus == id)
                focus = selection.Count == 0 ? null : SelectedNodes[^1].Id;
        }
        else
        {
            focus = id;
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Nodes paint in list order, so the last one drawn is the one on top.</summary>
    private void BringToFront(NodeInstance node)
    {
        patch.Nodes.Remove(node);
        patch.Nodes.Add(node);
    }
}
