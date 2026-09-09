using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// Several modules drawn as one box: what a group is here, the commands that
/// make and break one, and how a shut box and an open one are painted.
/// </summary>
/// <remarks>
/// The group itself belongs to the patch; what this adds is the two anchors
/// every wire crossing the boundary is drawn to, which is the seam the whole
/// feature hangs on — a box's socket is a real port on a real module, borrowed.
/// </remarks>
public sealed partial class NodeEditor
{
    // --- groups ---------------------------------------------------------------
    //
    // Everything below is drawing and pointing. Nothing here touches the graph:
    // a collapsed box is several modules that are not being painted and one that
    // is, and every wire still runs between exactly the modules it always ran
    // between. See NodeGroup.

    /// <summary>
    /// Every group that is currently a box, with the sockets it shows and the
    /// room it takes up.
    /// </summary>
    /// <remarks>
    /// Worked out afresh each time rather than kept: the sockets come off the
    /// wires and their order comes off where the modules sit, so a cache would
    /// have to be dropped on every wire drawn, every module moved and every undo
    /// — three chances to forget, to save arithmetic over a few dozen wires.
    /// </remarks>
    private IEnumerable<(NodeGroup Group, GroupSockets Sockets, Rect Bounds)> Boxes()
    {
        if (patch.Groups is null) yield break;

        foreach (var group in patch.Groups)
        {
            if (!group.Collapsed) continue;

            var sockets = patch.SocketsOf(group);
            var bounds = NodeGeometry.GroupBounds(patch, group, sockets);

            if (bounds.Width > 0) yield return (group, sockets, bounds);
        }
    }

    /// <summary>Whether this module is inside a box, and so is not drawn itself.</summary>
    private bool Shut(Guid nodeId) => patch.CollapsedGroupOf(nodeId) is not null;

    /// <summary>Whether both ends of a wire are inside the same box.</summary>
    private bool Hidden(Connection wire) =>
        patch.CollapsedGroupOf(wire.SourceNode) is { } group
        && ReferenceEquals(patch.CollapsedGroupOf(wire.TargetNode), group);

    /// <summary>
    /// Where an output is to be reached, which is the box standing in front of it
    /// where one is and the module itself where none is.
    /// </summary>
    /// <remarks>
    /// The one seam the whole feature hangs on. Painting, hit-testing and wire
    /// dragging all ask this rather than <see cref="NodeGeometry"/> directly, so
    /// none of them has to know that a box can exist — a socket on a box names a
    /// module and a port, so what comes back is still an answer about the module,
    /// only somewhere else on the screen.
    /// </remarks>
    private Point OutputAnchor(NodeInstance node, int port)
    {
        if (patch.CollapsedGroupOf(node.Id) is { } group)
        {
            var sockets = patch.SocketsOf(group);
            var row = sockets.IndexOfOutput(new GroupSocket(node.Id, port, IsOutput: true));

            if (row >= 0)
                return NodeGeometry.GroupOutputPort(
                    NodeGeometry.GroupBounds(patch, group, sockets), row);
        }

        return NodeGeometry.OutputPort(node, port);
    }

    /// <inheritdoc cref="OutputAnchor"/>
    private Point InputAnchor(NodeInstance node, NodeDef def, int port)
    {
        if (patch.CollapsedGroupOf(node.Id) is { } group)
        {
            var sockets = patch.SocketsOf(group);
            var row = sockets.IndexOfInput(new GroupSocket(node.Id, port, IsOutput: false));

            if (row >= 0)
                return NodeGeometry.GroupInputPort(
                    NodeGeometry.GroupBounds(patch, group, sockets), sockets, row);
        }

        return NodeGeometry.InputPort(node, def, port);
    }

    private NodeGroup? HitBox(Point graph)
    {
        foreach (var (group, _, bounds) in Boxes())
            if (bounds.Contains(graph))
                return group;

        return null;
    }

    private NodeGroup? HitOpenGroupHandle(Point graph)
    {
        if (patch.Groups is null) return null;

        foreach (var group in patch.Groups)
            if (OpenGroup(group) is var (_, handle) && handle.Contains(graph))
                return group;

        return null;
    }

    /// <summary>
    /// How many of the selected modules a group would actually take, which is
    /// every one of them but the sink.
    /// </summary>
    /// <remarks>
    /// The same count the delete button shows and for the same reason: a gesture
    /// that says it will take six and takes five is lying about what it does.
    /// Selecting everything and grouping it is the case that makes the
    /// difference, because Ctrl+A takes the Output too.
    /// <para>
    /// A module already in another group is counted, because it is taken: it
    /// leaves the group it was in, since two boxes both claiming to draw one
    /// module is a picture that means nothing. This has to agree with
    /// <see cref="Patch.Group"/> exactly or the label is the lie above.
    /// </para>
    /// </remarks>
    public int Groupable => SelectedNodes.Count(n => !NodeCatalog.IsSink(n.TypeId));

    /// <summary>
    /// The group the selection is exactly, and null where it is anything else —
    /// which is what tells "ungroup this" from "group these".
    /// </summary>
    public NodeGroup? SelectedGroup
    {
        get
        {
            if (patch.Groups is null || selection.Count == 0) return null;

            foreach (var group in patch.Groups)
                if (group.Members.Count == selection.Count && group.Members.All(selection.Contains))
                    return group;

            return null;
        }
    }

    /// <summary>Draws the selected modules as one box.</summary>
    public void GroupSelected()
    {
        if (patch.Group(selection) is not { } made)
        {
            // Said rather than ignored, but only where something was actually
            // selected: a key pressed on an empty canvas is a key pressed by
            // mistake, and the status bar is not the place to point that out.
            if (Groupable > 0)
                Reported?.Invoke(
                    this,
                    $"A group needs {NodeGroup.Fewest} modules or more — a box round one "
                    + "would be the module again with a second name.");

            return;
        }

        // The box stands for what was selected, and what was selected is what
        // stays selected — so the panel goes on showing the same modules and a
        // second Ctrl+G has something to ungroup.
        selection.Clear();
        foreach (var id in made.Members) selection.Add(id);

        focus = made.Members[^1];

        SelectionChanged?.Invoke(this, EventArgs.Empty);
        NotifyPatchChanged();
        Reported?.Invoke(
            this,
            $"Grouped {made.Members.Count} modules. "
            + "Ctrl+Shift+G ungroups them, double-click opens them.");
    }

    /// <summary>
    /// Stops drawing whatever groups the selection is inside, leaving every
    /// module and every wire exactly where they were.
    /// </summary>
    public void UngroupSelected()
    {
        if (patch.Groups is null || selection.Count == 0) return;

        var going = patch.Groups
            .Where(g => g.Members.Any(selection.Contains))
            .Select(g => g.Id)
            .ToArray();

        if (going.Length == 0) return;

        foreach (var id in going) patch.Ungroup(id);

        NotifyPatchChanged();
        Reported?.Invoke(this, going.Length == 1 ? "Ungrouped." : $"Ungrouped {going.Length} groups.");
    }

    /// <summary>Shuts a group that is open, or opens one that is shut.</summary>
    public void ToggleBox(NodeGroup group)
    {
        group.Collapsed = !group.Collapsed;

        // Opening one selects what came out, so the panel is about the modules
        // rather than about a box that is no longer there.
        if (!group.Collapsed)
        {
            selection.Clear();
            foreach (var id in group.Members) selection.Add(id);

            focus = group.Members.Count == 0 ? null : group.Members[^1];
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        NotifyPatchChanged();
    }

    /// <summary>Toggles whatever group the selection is exactly, if it is one.</summary>
    public void ToggleSelectedGroup()
    {
        if (SelectedGroup is { } group) ToggleBox(group);
    }

    /// <summary>
    /// Takes a socket off a box's edge.
    /// </summary>
    /// <remarks>
    /// Refused while a wire is on it, and not out of caution: a crossing wire is
    /// a socket whatever the stored list says, so this would appear to do nothing
    /// and the row would still be there afterwards. Unplug it first, which is the
    /// order the panel offers them in anyway.
    /// </remarks>
    public void HideSocket(NodeGroup group, GroupSocket socket)
    {
        if (patch.Wired(group, socket)) return;
        if (!group.Hide(socket)) return;

        NotifyPatchChanged();
    }

    /// <summary>
    /// A press on a box, or on the strip above an open group: what is inside is
    /// what gets selected.
    /// </summary>
    private void PressGroup(NodeGroup group, bool adding)
    {
        pendingNarrow = null;

        if (!adding) selection.Clear();

        foreach (var id in group.Members) selection.Add(id);

        focus = group.Members.Count == 0 ? null : group.Members[^1];
        SelectionChanged?.Invoke(this, EventArgs.Empty);

        foreach (var moving in SelectedNodes) BringToFront(moving);

        drag = Drag.Node;

        dragOrigins.Clear();
        foreach (var moving in SelectedNodes)
            dragOrigins[moving.Id] = new Point(moving.X, moving.Y);
    }

    /// <summary>
    /// The outline drawn round a group that is open, and the strip above it that
    /// shuts it again.
    /// </summary>
    /// <remarks>
    /// An open group has no box, so without this there would be nothing on the
    /// canvas to say one was there and no way back to the box but the inspector.
    /// The strip is where a double-click lands, which is the same gesture that
    /// opened it — a thing that opens by being double-clicked should shut the
    /// same way.
    /// </remarks>
    private (Rect Outline, Rect Handle)? OpenGroup(NodeGroup group)
    {
        if (group.Collapsed) return null;

        var x = double.MaxValue;
        var y = double.MaxValue;
        var right = double.MinValue;
        var bottom = double.MinValue;

        foreach (var id in group.Members)
            if (patch.Find(id) is { } node && NodeCatalog.Get(node.TypeId) is { } def)
            {
                var bounds = NodeGeometry.Bounds(node, def);

                x = Math.Min(x, bounds.X);
                y = Math.Min(y, bounds.Y);
                right = Math.Max(right, bounds.Right);
                bottom = Math.Max(bottom, bounds.Bottom);
            }

        if (x == double.MaxValue) return null;

        var outline = new Rect(x, y, right - x, bottom - y).Inflate(OpenGroupPadding);
        var handle = new Rect(outline.X, outline.Y - OpenGroupHandle, outline.Width, OpenGroupHandle);

        return (outline, handle);
    }

    private const double OpenGroupPadding = 24;
    private const double OpenGroupHandle = 20;

    /// <summary>
    /// The ring, the ground inside it and the title above it, for every group
    /// that is open.
    /// </summary>
    /// <remarks>
    /// A wash as well as a line, because a line alone out here is nearly
    /// nothing: this is drawn under the wires and the modules, on ground a
    /// person is reading past rather than at. Both are held faint. What an open
    /// group has to do is say where it is while somebody works inside it, and a
    /// region that draws the eye harder than the modules standing in it is a
    /// region in the way of the work.
    /// <para>
    /// The strip is left bare, and the title on it stays the muted grey the rest
    /// of the canvas furniture is written in. Filling it made a header, and a
    /// header is what a group wears when it is <em>shut</em> — a second one up
    /// here reads as a box that is somehow both.
    /// </para>
    /// </remarks>
    private void DrawOpenGroups(DrawingContext context)
    {
        if (patch.Groups is null) return;

        foreach (var group in patch.Groups)
        {
            if (OpenGroup(group) is not var (outline, handle)) continue;

            // Selected when its modules are, which is the rule a shut box uses —
            // and it is the same gesture that selects them, since pressing the
            // strip takes the group.
            var isSelected = group.Members.Count > 0 && group.Members.All(selection.Contains);

            context.DrawRectangle(
                OpenGroupFill,
                isSelected ? OpenGroupPenSelected : OpenGroupPen,
                new RoundedRect(outline, NodeGeometry.CornerRadius));

            var label = Text(group.Title(), 11.5, NormalBrush, outline.Width - 12, true);
            context.DrawText(label, new Point(handle.X + 6, handle.Y + (handle.Height - label.Height) / 2));
        }
    }

    private void DrawBox(DrawingContext context, NodeGroup group, GroupSockets sockets, Rect bounds)
    {
        // Selected when its modules are, because pressing the box is what selects
        // them — there is nothing else it could mean for a box to be picked.
        var isSelected = group.Members.Count > 0 && group.Members.All(selection.Contains);

        context.DrawRectangle(
            isSelected ? NodeFillSelected : NodeFill,
            isSelected ? SelectionPenSecondary : NodeBorder,
            new RoundedRect(bounds, NodeGeometry.CornerRadius));

        var header = new Rect(bounds.X, bounds.Y, bounds.Width, NodeGeometry.HeaderHeight);
        context.DrawRectangle(
            GroupHeaderFill,
            null,
            new RoundedRect(header, NodeGeometry.CornerRadius, NodeGeometry.CornerRadius, 0, 0));

        context.DrawText(
            Text(group.Title(), 12.5, HeaderTextBrush, bounds.Width - 16, true),
            new Point(bounds.X + 9, bounds.Y + 5));

        for (var i = 0; i < sockets.Outputs.Count; i++)
            DrawBoxSocket(context, sockets.Outputs[i], NodeGeometry.GroupOutputPort(bounds, i), bounds);

        for (var i = 0; i < sockets.Inputs.Count; i++)
            DrawBoxSocket(
                context, sockets.Inputs[i], NodeGeometry.GroupInputPort(bounds, sockets, i), bounds);
    }

    /// <summary>
    /// One socket of a box, named for the port inside that it stands for.
    /// </summary>
    /// <remarks>
    /// "filter.cutoff" rather than a name of its own, and that is deliberate: the
    /// socket is a way of pointing at an inner port and reads as one. It also
    /// means renaming a module inside relabels the box for nothing, which is the
    /// whole of how a group gets a readable edge.
    /// </remarks>
    private void DrawBoxSocket(DrawingContext context, GroupSocket socket, Point centre, Rect bounds)
    {
        if (Named(socket) is not var (label, spec)) return;

        var text = Text(label, 11.5, LabelBrush, bounds.Width - 26, true);

        context.DrawText(
            text,
            socket.IsOutput
                ? new Point(bounds.Right - 14 - text.Width, centre.Y - text.Height / 2)
                : new Point(bounds.X + 14, centre.Y - text.Height / 2));

        DrawPort(context, centre, spec.Kind);
    }

    /// <summary>
    /// What a socket is called and what it is, or null where it names a module or
    /// a port that is not there.
    /// </summary>
    /// <remarks>
    /// "filter.cutoff" rather than a name of its own, and that is deliberate: the
    /// socket is a way of pointing at an inner port and reads as one. It also
    /// means renaming a module inside relabels the box for nothing, which is the
    /// whole of how a group gets a readable edge.
    /// </remarks>
    public (string Label, PortSpec Spec)? Named(GroupSocket socket)
    {
        if (patch.Find(socket.Node) is not { } node) return null;
        if (NodeCatalog.Get(node.TypeId) is not { } def) return null;

        var ports = socket.IsOutput ? def.Outputs : def.Inputs;
        if (socket.Port >= ports.Count) return null;

        return ($"{node.Title(def)}.{ports[socket.Port].Name}", ports[socket.Port]);
    }

    private static void DrawPort(DrawingContext context, Point centre, PortKind kind) =>
        context.DrawEllipse(
            new SolidColorBrush(Colors.PortColor(kind)),
            PortOutline,
            centre,
            NodeGeometry.PortRadius,
            NodeGeometry.PortRadius);

    private static FormattedText Text(string text, double size, IBrush brush, double maxWidth, bool trim)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            size,
            brush);

        if (trim)
        {
            formatted.MaxTextWidth = maxWidth;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }

        return formatted;
    }
}
