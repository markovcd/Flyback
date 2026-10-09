using Avalonia;
using Avalonia.Media;
using Flyback.Core.Graph;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Canvas;

/// <summary>A wire drawn out of a socket, or lifted off one and carried to another.</summary>
internal sealed class WireDrag(CanvasHistory history, CanvasSelection selection)
{
    private Guid wireNode;
    private int wirePort;
    private bool wireFromOutput;

    /// <summary>
    /// Which re-patch this is. Unplugging an input and plugging it in elsewhere is two
    /// edits and one gesture, so both carry this and fold into one step.
    /// </summary>
    private int wireGesture;

    private Point wireEnd;

    /// <summary>The wire this re-patch picked up and where in the patch's list it was, or null for a new wire.</summary>
    private (Connection Wire, int At)? lifted;

    /// <summary>
    /// The wire the last press lifted off an output, so pressing the same socket again
    /// straight after putting it back takes the next wire along.
    /// </summary>
    private Connection? liftedOffOutput;

    /// <summary>A wire was let go over empty canvas.</summary>
    public event EventHandler<WireDrop>? Dropped;

    /// <summary>Whether the wire is held by its output end, and so plugs into an input.</summary>
    public bool FromOutput => wireFromOutput;

    /// <summary>
    /// Where the wire is anchored. Through the scene's anchors like every other wire,
    /// since the port may be behind a box.
    /// </summary>
    public Point? Anchor
    {
        get
        {
            if (history.Patch.Find(wireNode) is not { } node) return null;
            if (NodeCatalog.Get(node.TypeId) is not { } def) return null;

            var scene = selection.Scene;

            return wireFromOutput ? scene.OutputAnchor(node, wirePort) : scene.InputAnchor(node, def, wirePort);
        }
    }

    private string WireGesture => $"wire {wireGesture}";

    /// <summary>
    /// A left press went down: the wire lifted off an output by the press before, which
    /// only the very next press walks on from.
    /// </summary>
    public Connection? Pressed()
    {
        var liftedLast = liftedOffOutput;
        liftedOffOutput = null;
        return liftedLast;
    }

    /// <summary>
    /// Grabbing a connected socket picks the existing wire up by the end that was not
    /// grabbed, so re-patching works the way it does on a real rig.
    /// </summary>
    /// <param name="lifting">
    /// Whether Ctrl was held, which only matters on an output: dragging from one already
    /// means "start another wire", so taking one that is there asks for the modifier.
    /// </param>
    /// <param name="liftedLast">The wire the press before this lifted off an output, if it did.</param>
    public void Start(Guid nodeId, int portIndex, bool isOutput, bool lifting, Connection? liftedLast, Point graph)
    {
        var patch = history.Patch;

        wireGesture++;
        lifted = null;
        wireEnd = graph;

        if (!isOutput && patch.IncomingTo(nodeId, portIndex) is { } existing)
        {
            lifted = (existing, patch.Connections.IndexOf(existing));
            patch.Disconnect(nodeId, portIndex);
            wireNode = existing.SourceNode;
            wirePort = existing.SourcePort;
            wireFromOutput = true;
            history.Record(WireGesture);
        }
        else if (isOutput && lifting && NextOutgoing(nodeId, portIndex, liftedLast) is { } taken)
        {
            // The mirror of the case above: the wire comes off the socket grabbed and
            // stays in the one at its far end, so what changes is where the signal
            // comes from while what it feeds stays put.
            lifted = (taken, patch.Connections.IndexOf(taken));
            liftedOffOutput = taken;
            patch.Disconnect(taken.TargetNode, taken.TargetPort);
            wireNode = taken.TargetNode;
            wirePort = taken.TargetPort;
            wireFromOutput = false;
            history.Record(WireGesture);
        }
        else
        {
            wireNode = nodeId;
            wirePort = portIndex;
            wireFromOutput = isOutput;
        }
    }

    /// <summary>Moves the loose end to <paramref name="graph"/>.</summary>
    public void Stretch(Point graph) => wireEnd = graph;

    /// <summary>
    /// The wire a Ctrl-press on an output takes: the one after <paramref name="liftedLast"/>
    /// when that was put back on this socket, and the first otherwise.
    /// </summary>
    private Connection? NextOutgoing(Guid nodeId, int portIndex, Connection? liftedLast)
    {
        var leaving = history.Patch.Connections
            .Where(c => c.SourceNode == nodeId && c.SourcePort == portIndex)
            .ToList();

        if (leaving.Count == 0) return null;

        var at = liftedLast is { } last ? leaving.IndexOf(last) : -1;

        return leaving[(at + 1) % leaving.Count];
    }

    public void Complete(Point graph)
    {
        var patch = history.Patch;
        var scene = selection.Scene;

        if (!scene.HitPort(graph, out var node, out var port, out var isOutput))
        {
            // Dropped on a module's body it is a miss; on bare canvas it asks for
            // something to plug into.
            if (scene.HitNode(graph) is null) OfferSomethingToPlugInto(graph);

            return;
        }

        // A wire only means something between opposite kinds of socket.
        if (isOutput == wireFromOutput) return;

        var (sourceNode, sourcePort, targetNode, targetPort) = wireFromOutput
            ? (wireNode, wirePort, node, port)
            : (node, port, wireNode, wirePort);

        // A wire that closes a loop is drawn like any other and carries the previous
        // evaluation (ADR-0075), so there is nothing to put on it.
        patch.Connect(sourceNode, sourcePort, targetNode, targetPort);

        // Put straight back where it was lifted from, into its old place in the list,
        // so the history sees the patch the gesture began with and drops the step.
        if (lifted is { } was
            && was.Wire == new Connection(sourceNode, sourcePort, targetNode, targetPort)
            && patch.Connections.Remove(was.Wire))
        {
            patch.Connections.Insert(Math.Min(was.At, patch.Connections.Count), was.Wire);
        }

        history.Record(WireGesture);
    }

    /// <summary>
    /// Puts a lifted wire back into its place in the list, under the name the lifting was
    /// recorded under, so the history sees the patch it began with and drops the step.
    /// </summary>
    public void Abort()
    {
        if (lifted is not { } was) return;

        lifted = null;
        history.Patch.Connections.Insert(Math.Min(was.At, history.Patch.Connections.Count), was.Wire);
        history.Record(WireGesture);
    }

    /// <summary>A wire let go over bare canvas, handed to whoever can offer something to plug it into.</summary>
    private void OfferSomethingToPlugInto(Point graph)
    {
        if (history.Patch.Find(wireNode) is not { } holding) return;
        if (NodeCatalog.Get(holding.TypeId) is not { } def) return;

        var sockets = wireFromOutput ? def.Outputs : def.Inputs;
        if (wirePort < 0 || wirePort >= sockets.Count) return;

        Dropped?.Invoke(this, new WireDrop(graph, wireNode, wirePort, wireFromOutput, sockets[wirePort].Kind));
    }

    /// <summary>The wire being drawn, from its anchor to the pointer, routed as it will be once dropped.</summary>
    public void Draw(DrawingContext context, Point anchor)
    {
        var pen = new Pen(new SolidColorBrush(Colors.Attention, 0.9), 2.2, DashStyle.Dash);

        // Handed over in whichever order makes the curve leave an output and arrive at an input.
        var (from, to) = wireFromOutput ? (anchor, wireEnd) : (wireEnd, anchor);

        if (from.X <= to.X)
        {
            WirePath.Draw(context, from, to, pen);
            return;
        }

        // The pointer is a module of no size.
        var holding = history.Patch.Find(wireNode) is { } node && NodeCatalog.Get(node.TypeId) is { } def
            ? selection.Scene.RouteBounds(node, def)
            : new Rect(anchor, anchor);

        WirePath.DrawReturn(context, from, to, WirePath.ReturnRun(holding, new Rect(wireEnd, wireEnd)), pen);
    }
}
