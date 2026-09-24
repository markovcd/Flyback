using Avalonia;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>What the canvas shows of a patch, and what is under a point on it.</summary>
/// <remarks>
/// Every measurement is in graph space, which is why nothing here needs to know the
/// zoom. Hit-testing is linear over the modules in reverse drawing order, so the one
/// on top answers first — fine for the hundreds a patch has and not for tens of
/// thousands (ADR-0017).
/// </remarks>
/// <param name="patch">What is on the canvas.</param>
/// <param name="geometry">Where each part of a module sits in this window.</param>
/// <param name="peek">
/// A shut box being looked into: drawn open, over everything else, while it stays
/// shut in the patch. Its ring covers whatever lies under it.
/// </param>
internal readonly struct CanvasScene(Patch patch, NodeGeometry geometry, NodeGroup? peek = null)
{
    // Everything below is drawing and pointing. Nothing here touches the graph:
    // a collapsed box is several modules that are not being painted and one that
    // is, and every wire still runs between exactly the modules it always ran
    // between. See NodeGroup.

    /// <summary>The box being looked into, if one is.</summary>
    public NodeGroup? Peek => peek;

    /// <summary>Whether this module is inside the box being looked into.</summary>
    public bool InPeek(Guid nodeId) => peek is not null && peek.Members.Contains(nodeId);

    /// <summary>Whether the ring of the box being looked into lies over this point.</summary>
    public bool Covered(Point graph) =>
        peek is not null && OpenGroup(peek) is var (outline, handle)
        && (outline.Contains(graph) || handle.Contains(graph));

    /// <summary>The box this module is drawn behind, which the one being looked into is not.</summary>
    private NodeGroup? ShutGroupOf(Guid nodeId) =>
        patch.CollapsedGroupOf(nodeId) is { } group && !ReferenceEquals(group, peek) ? group : null;

    /// <summary>
    /// Every group that is currently a box, with the sockets it shows and the room
    /// it takes up.
    /// </summary>
    /// <remarks>
    /// Worked out afresh each time rather than kept: the sockets come off the wires
    /// and their order off where the modules sit, so a cache would have to be
    /// dropped on every wire drawn, every module moved and every undo.
    /// </remarks>
    public IEnumerable<(NodeGroup Group, GroupSockets Sockets, Rect Bounds)> Boxes()
    {
        if (patch.Groups is null) yield break;

        foreach (var group in patch.Groups)
        {
            if (!group.Collapsed || ReferenceEquals(group, peek)) continue;

            var sockets = patch.SocketsOf(group);
            var bounds = geometry.GroupBounds(patch, group, sockets);

            if (bounds.Width > 0) yield return (group, sockets, bounds);
        }
    }

    /// <summary>Whether this module is inside a box, and so is not drawn itself.</summary>
    public bool Shut(Guid nodeId) => ShutGroupOf(nodeId) is not null;

    /// <summary>
    /// Every rectangle the canvas has something in: a box for each group that is
    /// shut, a ring for each that is open, and the modules not behind a box.
    /// </summary>
    /// <remarks>
    /// What framing and pasting ask, rather than the list of modules. A module
    /// behind a shut box is not on the canvas at all — nothing paints it and
    /// <see cref="PatchLayout"/> parks it behind the box — so framing to one zooms
    /// out to fit a picture nobody can see.
    /// </remarks>
    public IEnumerable<Rect> OnCanvas()
    {
        foreach (var (_, _, bounds) in Boxes()) yield return bounds;

        if (patch.Groups is not null)
            foreach (var group in patch.Groups)
                if (OpenGroup(group) is var (outline, handle))
                    yield return outline.Union(handle);

        foreach (var node in patch.Nodes)
            if (!Shut(node.Id))
                yield return Footprint(node);
    }

    /// <summary>The room a module takes up, as far as keeping clear of it goes.</summary>
    /// <remarks>
    /// A module whose plugin is missing has no height to ask for. Counted at nothing
    /// rather than skipped, so its corner is still somewhere the canvas is occupied.
    /// </remarks>
    public Rect Footprint(NodeInstance node) => new(
        node.X,
        node.Y,
        NodeGeometry.Width,
        NodeCatalog.Get(node.TypeId) is { } def ? geometry.Height(def) : 0);

    /// <summary>Whether both ends of a wire are inside the same box.</summary>
    public bool Hidden(Connection wire) =>
        ShutGroupOf(wire.SourceNode) is { } group
        && ReferenceEquals(ShutGroupOf(wire.TargetNode), group);

    /// <summary>
    /// Where an output is to be reached, which is the box standing in front of it
    /// where one is and the module itself where none is.
    /// </summary>
    /// <remarks>
    /// The one seam the whole feature hangs on: painting, hit-testing and wire
    /// dragging ask this rather than <see cref="NodeGeometry"/>, so none of them
    /// has to know a box can exist. A socket on a box names a module and a port, so
    /// what comes back is still an answer about the module.
    /// </remarks>
    public Point OutputAnchor(NodeInstance node, int port)
    {
        if (ShutGroupOf(node.Id) is { } group)
        {
            var sockets = patch.SocketsOf(group);
            var row = sockets.IndexOfOutput(new GroupSocket(node.Id, port, IsOutput: true));

            if (row >= 0)
                return NodeGeometry.GroupOutputPort(
                    geometry.GroupBounds(patch, group, sockets), row);
        }

        return NodeGeometry.OutputPort(node, port);
    }

    /// <inheritdoc cref="OutputAnchor"/>
    public Point InputAnchor(NodeInstance node, NodeDef def, int port)
    {
        if (ShutGroupOf(node.Id) is { } group)
        {
            var sockets = patch.SocketsOf(group);
            var row = sockets.IndexOfInput(new GroupSocket(node.Id, port, IsOutput: false));

            if (row >= 0)
                return geometry.GroupInputPort(
                    geometry.GroupBounds(patch, group, sockets), sockets, row);
        }

        return geometry.InputPort(node, def, port);
    }

    public NodeGroup? HitBox(Point graph)
    {
        if (Covered(graph)) return null;

        foreach (var (group, _, bounds) in Boxes())
            if (bounds.Contains(graph))
                return group;

        return null;
    }

    public NodeGroup? HitOpenGroupHandle(Point graph)
    {
        if (patch.Groups is null) return null;

        if (peek is not null && OpenGroup(peek) is var (_, lifted) && lifted.Contains(graph)) return peek;
        if (Covered(graph)) return null;

        foreach (var group in patch.Groups)
            if (OpenGroup(group) is var (_, handle) && handle.Contains(graph))
                return group;

        return null;
    }

    /// <summary>
    /// Whether a group is switched off, which it is only where every module in it
    /// is — the reading a selection already gets, so a box and what it holds never
    /// disagree.
    /// </summary>
    public bool SwitchedOff(NodeGroup group)
    {
        // A lambda in a struct cannot capture the primary constructor's parameter.
        var nodes = patch;

        return group.Members.Count > 0 && group.Members.All(id => nodes.Find(id) is { Off: true });
    }

    /// <summary>
    /// The outline drawn round a group that is open, and the strip above it that
    /// shuts it again.
    /// </summary>
    /// <remarks>
    /// An open group has no box, so without this there would be nothing to say one
    /// was there and no way back but the inspector. The strip is where a
    /// double-click lands: a thing that opens by being double-clicked should shut
    /// the same way.
    /// </remarks>
    public (Rect Outline, Rect Handle)? OpenGroup(NodeGroup group)
    {
        if (group.Collapsed && !ReferenceEquals(group, peek)) return null;

        var x = double.MaxValue;
        var y = double.MaxValue;
        var right = double.MinValue;
        var bottom = double.MinValue;

        foreach (var id in group.Members)
            if (patch.Find(id) is { } node && NodeCatalog.Get(node.TypeId) is { } def)
            {
                var bounds = geometry.Bounds(node, def);

                x = Math.Min(x, bounds.X);
                y = Math.Min(y, bounds.Y);
                right = Math.Max(right, bounds.Right);
                bottom = Math.Max(bottom, bounds.Bottom);
            }

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (x == double.MaxValue) return null;

        var outline = new Rect(x, y, right - x, bottom - y).Inflate(NodeGeometry.GroupPadding);

        var handle = new Rect(
            outline.X,
            outline.Y - NodeGeometry.GroupHandleHeight,
            outline.Width,
            NodeGeometry.GroupHandleHeight);

        return (outline, handle);
    }

    /// <summary>
    /// What a socket is called and what it is, or null where it names a module or a
    /// port that is not there. "filter.cutoff" rather than a name of its own: the
    /// socket is a way of pointing at an inner port and reads as one.
    /// </summary>
    /// <remarks>
    /// An Expression's input is the exception, named for what is wired into it:
    /// "Clock.beats" says what socket a carries where "Expression.a" says nothing.
    /// </remarks>
    public (string Label, PortSpec Spec)? Named(GroupSocket socket)
    {
        if (patch.Find(socket.Node) is not { } node) return null;
        if (NodeCatalog.Get(node.TypeId) is not { } def) return null;

        var ports = socket.IsOutput ? def.Outputs : def.Inputs;
        if (socket.Port >= ports.Count) return null;

        if (!socket.IsOutput
            && node.TypeId == NodeCatalog.ExpressionTypeId
            && patch.IncomingTo(node.Id, socket.Port) is { } wire
            && patch.Find(wire.SourceNode) is { } source
            && NodeCatalog.Get(source.TypeId) is { } from
            && wire.SourcePort < from.Outputs.Count)
        {
            return ($"{source.Title(from)}.{from.Outputs[wire.SourcePort].Name}", ports[socket.Port]);
        }

        return ($"{node.Title(def)}.{ports[socket.Port].Name}", ports[socket.Port]);
    }

    public NodeInstance? HitNode(Point graph)
    {
        var covered = Covered(graph);

        for (var i = patch.Nodes.Count - 1; i >= 0; i--)
        {
            var node = patch.Nodes[i];
            var def = NodeCatalog.Get(node.TypeId);

            // A module inside a shut box is not on the canvas, so nothing can
            // land on it. Without this a click would reach a module it cannot
            // see and drag it out from under the box drawn over it.
            if (Shut(node.Id)) continue;
            if (covered && !InPeek(node.Id)) continue;

            if (def is not null && geometry.Bounds(node, def).Contains(graph))
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
    public bool HitPort(Point graph, out Guid nodeId, out int portIndex, out bool isOutput)
    {
        var tolerance = NodeGeometry.PortRadius + NodeGeometry.HitPadding;

        // The box being looked into is over everything, boxes included.
        if (peek is not null && HitModulePort(graph, tolerance, lifted: true, out nodeId, out portIndex, out isOutput))
            return true;

        if (Covered(graph))
        {
            (nodeId, portIndex, isOutput) = (Guid.Empty, -1, false);
            return false;
        }

        // Boxes next, because they are painted over the modules they stand for
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
                if (!Near(geometry.GroupInputPort(bounds, sockets, p), graph, tolerance)) continue;

                var socket = sockets.Inputs[p];
                (nodeId, portIndex, isOutput) = (socket.Node, socket.Port, false);
                return true;
            }
        }

        return HitModulePort(graph, tolerance, lifted: false, out nodeId, out portIndex, out isOutput);
    }

    /// <summary>
    /// Which port on a module is under the pointer, among the modules of the box being
    /// looked into or among the rest.
    /// </summary>
    private bool HitModulePort(
        Point graph, double tolerance, bool lifted, out Guid nodeId, out int portIndex, out bool isOutput)
    {
        for (var i = patch.Nodes.Count - 1; i >= 0; i--)
        {
            var node = patch.Nodes[i];
            var def = NodeCatalog.Get(node.TypeId);
            if (def is null || Shut(node.Id) || InPeek(node.Id) != lifted) continue;

            for (var p = 0; p < def.Outputs.Count; p++)
            {
                if (!Near(NodeGeometry.OutputPort(node, p), graph, tolerance)) continue;

                (nodeId, portIndex, isOutput) = (node.Id, p, true);
                return true;
            }

            for (var p = 0; p < def.Inputs.Count; p++)
            {
                if (!Near(geometry.InputPort(node, def, p), graph, tolerance)) continue;

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
    /// Every module the rubber band <paramref name="band"/> is over.
    /// </summary>
    /// <remarks>
    /// A module counts as swept when the band touches it rather than when it
    /// swallows it whole. Touching is the more forgiving of the two and it is
    /// what the gesture looks like it should do — dragging across a row of
    /// modules takes the row, without having to reach past the ends of it.
    /// </remarks>
    public IEnumerable<Guid> Swept(Rect band)
    {
        // A band drawn while looking into a box is drawn on its ring, and sweeps
        // only what is inside.
        if (peek is not null)
        {
            foreach (var node in patch.Nodes)
                if (InPeek(node.Id)
                    && NodeCatalog.Get(node.TypeId) is { } def
                    && geometry.Bounds(node, def).Intersects(band))
                    yield return node.Id;

            yield break;
        }

        foreach (var node in patch.Nodes)
            if (!Shut(node.Id)
                && NodeCatalog.Get(node.TypeId) is { } def
                && geometry.Bounds(node, def).Intersects(band))
                yield return node.Id;

        // A box is swept as the modules it stands for, all of them together and
        // none of them without the rest. Sweeping half a box would select a
        // module the band never touched — the one under it — which is the same
        // reason a module under a box does not answer a click.
        foreach (var (group, _, bounds) in Boxes())
            if (bounds.Intersects(band))
                foreach (var id in group.Members)
                    yield return id;
    }

    /// <summary>
    /// The rectangle between two corners, whichever way round they are. Built by hand
    /// rather than from <c>new Rect(a, b)</c>, which takes the first point as the top
    /// left: started from any other corner that gives a negative width, and a
    /// rectangle like that draws nothing and intersects nothing.
    /// </summary>
    public static Rect Band(Point a, Point b) => new(
        Math.Min(a.X, b.X),
        Math.Min(a.Y, b.Y),
        Math.Abs(b.X - a.X),
        Math.Abs(b.Y - a.Y));

    /// <summary>
    /// The one rectangle that holds what a fragment will look like on the canvas.
    /// </summary>
    /// <remarks>
    /// Which is not where its modules are, for a fragment with a shut box in it: a
    /// box is drawn at its members' least corner and one module wide, however far
    /// apart they were left. Placed by the spread instead, a group saved from a
    /// chain laid out by hand lands half its hidden width away from the click.
    /// </remarks>
    public Rect Drawn(Patch fragment, IReadOnlyList<NodeInstance> arriving)
    {
        var shut = fragment.Groups?.Where(group => group.Collapsed).ToArray() ?? [];
        var hidden = shut.SelectMany(group => group.Members).ToHashSet();

        var seen = BoxAround([.. arriving.Where(node => !hidden.Contains(node.Id))]);

        foreach (var group in shut)
        {
            var box = geometry.GroupBounds(fragment, group, fragment.SocketsOf(group));

            seen = seen == default ? box : seen.Union(box);
        }

        return seen;
    }

    /// <summary>The one rectangle that holds all of these modules.</summary>
    private Rect BoxAround(IReadOnlyList<NodeInstance> nodes)
    {
        double left = double.MaxValue, top = double.MaxValue;
        double right = double.MinValue, bottom = double.MinValue;

        foreach (var node in nodes)
        {
            var room = Footprint(node);

            left = Math.Min(left, room.X);
            top = Math.Min(top, room.Y);
            right = Math.Max(right, room.Right);
            bottom = Math.Max(bottom, room.Bottom);
        }

        return left > right ? default : new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// As much of a drag as keeps every module in it inside the canvas.
    /// </summary>
    /// <remarks>
    /// The whole gesture is cut back to what the nearest module to an edge can
    /// take, rather than each module being clamped where it lands: clamping one at
    /// a time would flatten the group against the edge, the ones already there
    /// stopped while the rest kept coming. Each axis is narrowed by every module in
    /// turn, and all the ranges hold zero — standing still at worst.
    /// </remarks>
    public Vector Held(Vector delta, Dictionary<Guid, Point> origins)
    {
        var (x, y) = (delta.X, delta.Y);

        foreach (var (id, from) in origins)
        {
            if (patch.Find(id) is not { } node) continue;
            if (NodeCatalog.Get(node.TypeId) is not { } def) continue;

            var room = Room(def);

            x = Math.Clamp(x, room.X - from.X, room.Right - from.X);
            y = Math.Clamp(y, room.Y - from.Y, room.Bottom - from.Y);
        }

        return new Vector(x, y);
    }

    /// <summary>
    /// Where a module's corner may be put, so the whole of it is on the canvas: the
    /// canvas less the room the module takes up. A coordinate names the top left
    /// and the body hangs below and right of it, so holding the coordinate inside
    /// leaves the body outside.
    /// </summary>
    private Rect Room(NodeDef def) => new(
        Viewport.CanvasBounds.X,
        Viewport.CanvasBounds.Y,
        Math.Max(0, Viewport.CanvasBounds.Width - NodeGeometry.Width),
        Math.Max(0, Viewport.CanvasBounds.Height - geometry.Height(def)));

    /// <summary>
    /// Puts every module wholly inside the canvas.
    /// </summary>
    /// <remarks>
    /// The coordinate holds itself inside on its own
    /// (<see cref="NodeInstance.Across"/>), but what it holds is a corner, and how
    /// far the body reaches past it is the view's arithmetic. So a paste, a layout
    /// or a file may leave a module standing half off, and this is where it is
    /// known enough to be put right. A module the catalog does not have is left
    /// where it is, since nothing here can measure one.
    /// </remarks>
    public void HoldInside()
    {
        foreach (var node in patch.Nodes)
        {
            if (NodeCatalog.Get(node.TypeId) is not { } def) continue;

            var room = Room(def);

            node.X = Math.Clamp(node.X, room.X, room.Right);
            node.Y = Math.Clamp(node.Y, room.Y, room.Bottom);
        }
    }
}
