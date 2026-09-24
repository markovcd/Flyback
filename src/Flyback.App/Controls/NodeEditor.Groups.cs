using Avalonia;
using Avalonia.Media;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// Several modules drawn as one box: what a group is here, the commands that
/// make and break one, and how a shut box and an open one are painted.
/// </summary>
/// <remarks>
/// The group itself belongs to the patch, and where a box stands and what its
/// sockets are is <see cref="CanvasScene"/>'s — a box's socket is a real port on
/// a real module, borrowed.
/// </remarks>
public sealed partial class NodeEditor
{
    // --- groups ---------------------------------------------------------------

    /// <summary>
    /// How many of the selected modules a group would actually take, which is every
    /// one of them but the sink.
    /// </summary>
    /// <remarks>
    /// The same count the delete button shows: a gesture that says it will take six
    /// and takes five is lying about what it does. A module already in another
    /// group is counted, because it is taken — it leaves the group it was in. This
    /// has to agree with <see cref="Patch.Group"/> exactly.
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

    /// <summary>
    /// Takes in the rest of every shut box the selection reaches into, and says
    /// whether that changed anything.
    /// </summary>
    /// <remarks>
    /// A shut box is selected whole or not at all: it is drawn selected only when
    /// every member is, and a member nobody can see answering Delete or riding
    /// along on a drag is the module under a box answering a click again. Asked
    /// wherever a box can shut round a selection — the gesture, a layout that shut
    /// one to fit, and a step through the history.
    /// </remarks>
    private bool SelectWholeBoxes()
    {
        if (patch.Groups is null || selection.Count == 0) return false;

        var before = selection.Count;

        var peeked = Peeked;

        foreach (var group in patch.Groups)
            if (group.Collapsed && group != peeked && group.Members.Any(selection.Contains))
                selection.UnionWith(group.Members);

        return selection.Count != before;
    }

    /// <summary>
    /// Leaves the inspector about something selected wherever anything is: the
    /// module it was about if that is still one of them, and otherwise the one
    /// drawn on top — the rule the rubber band and Delete use.
    /// </summary>
    private void Refocus()
    {
        if (focus is { } kept && selection.Contains(kept)) return;

        focus = patch.Nodes.LastOrDefault(node => selection.Contains(node.Id))?.Id;
    }

    /// <summary>Draws the selected modules as one box.</summary>
    public void GroupSelected()
    {
        // Already one. Grouping it again would dissolve it to make it: a new box
        // round the same modules, without its name and without the sockets left
        // on its edge, under a line saying only that modules were grouped.
        if (SelectedGroup is not null)
        {
            Reported?.Invoke(this, "These modules are a group already. Ctrl+Shift+G ungroups them.");
            return;
        }

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
            + "Ctrl+Shift+G ungroups them, double-click looks inside.");
    }

    /// <summary>The shut box being looked into, which lives here and not in the patch.</summary>
    private Guid? peek;

    /// <summary>
    /// The shut box being looked into: drawn open over everything else, and still
    /// shut in the patch, in the file and in the history.
    /// </summary>
    /// <remarks>
    /// Forgotten once the box is gone, opened for good, or once anything outside it
    /// is selected — a module added, a paste, Ctrl+A. What is selected is where the
    /// work is, and a ring covering it would be in the way.
    /// </remarks>
    public NodeGroup? Peeked
    {
        get
        {
            if (peek is not { } id) return null;

            if (patch.Groups?.FirstOrDefault(g => g.Id == id) is { Collapsed: true } group
                && selection.All(group.Members.Contains))
                return group;

            peek = null;
            return null;
        }
    }

    /// <summary>
    /// Looks into a shut box without opening it: its modules come up over the rest of
    /// the canvas, with nothing else in the way, until Escape or a click outside.
    /// </summary>
    /// <remarks>
    /// Not an edit, so it is offered on a locked canvas and never reaches the history.
    /// </remarks>
    public void Peek(NodeGroup group)
    {
        if (!group.Collapsed) return;

        selection.Clear();
        foreach (var id in group.Members) selection.Add(id);

        focus = group.Members.Count == 0 ? null : group.Members[^1];
        peek = group.Id;

        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
        Reported?.Invoke(this, $"Looking into {group.Title()}. Esc or a click outside puts it back.");
    }

    /// <summary>Puts the box being looked into back, and says whether there was one.</summary>
    public bool EndPeek()
    {
        if (Peeked is null) return false;

        peek = null;

        // A module picked out inside is behind the box again.
        if (SelectWholeBoxes()) SelectionChanged?.Invoke(this, EventArgs.Empty);

        InvalidateVisual();
        return true;
    }

    /// <summary>
    /// Every group the selection reaches into, shut or open. One member is
    /// enough, where <see cref="SelectedGroup"/> wants the selection to be a
    /// group exactly.
    /// </summary>
    public IEnumerable<NodeGroup> SelectedGroups =>
        patch.Groups?.Where(g => g.Members.Any(selection.Contains)) ?? [];

    /// <summary>
    /// Stops drawing whatever groups the selection is inside, leaving every
    /// module and every wire exactly where they were.
    /// </summary>
    public void UngroupSelected()
    {
        if (selection.Count == 0) return;

        var going = SelectedGroups.Select(g => g.Id).ToArray();

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

    /// <summary>Opens every box the selection reaches into.</summary>
    public void OpenSelectedGroups() => SetBoxes(collapsed: false);

    /// <summary>Shuts every group the selection reaches into that is open.</summary>
    public void CloseSelectedGroups() => SetBoxes(collapsed: true);

    /// <summary>
    /// Draws every group the selection reaches into the same way round, and says
    /// how many moved.
    /// </summary>
    /// <remarks>
    /// Two gestures rather than one toggle: a selection over one shut group and
    /// one open one has no state to flip to that leaves both agreeing.
    /// </remarks>
    private void SetBoxes(bool collapsed)
    {
        if (selection.Count == 0) return;

        var moved = SelectedGroups.Where(g => g.Collapsed != collapsed).ToArray();

        if (moved.Length == 0) return;

        foreach (var group in moved) group.Collapsed = collapsed;

        // Opening takes in what came out, so the panel is about modules on the
        // canvas. Shutting takes in the rest of each box for the opposite reason:
        // what was selected inside one is about to be out of sight.
        if (!collapsed)
        {
            foreach (var group in moved)
                foreach (var id in group.Members)
                    selection.Add(id);

            // The module drawn on top, the same rule the rubber band uses.
            focus = patch.Nodes.LastOrDefault(node => selection.Contains(node.Id))?.Id;

            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (SelectWholeBoxes())
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        NotifyPatchChanged();

        var what = collapsed ? "Shut" : "Opened";

        Reported?.Invoke(
            this, moved.Length == 1 ? $"{what} one group." : $"{what} {moved.Length} groups.");
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

    /// <summary>Puts an unwired socket of a module inside a box on its edge.</summary>
    public void ExposeSocket(NodeGroup group, GroupSocket socket)
    {
        if (!patch.Exposable(group, socket)) return;
        if (!group.Expose(socket)) return;

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
    /// The ring, the ground inside it and the title above it, for every group that
    /// is open.
    /// </summary>
    /// <remarks>
    /// A wash as well as a line, because a line alone out here is nearly nothing —
    /// this is drawn under the wires and the modules. Both are held faint: a region
    /// that draws the eye harder than the modules standing in it is a region in the
    /// way of the work. The strip is left bare, because filling it makes a header,
    /// and a header is what a group wears when it is shut.
    /// </remarks>
    private void DrawOpenGroups(DrawingContext context, CanvasScene scene)
    {
        if (patch.Groups is null) return;

        foreach (var group in patch.Groups)
            if (group != scene.Peek && scene.OpenGroup(group) is var (outline, handle))
                DrawRing(context, group, outline, handle, lifted: false);
    }

    /// <summary>
    /// The box being looked into, and everything of it, over a canvas dimmed under it.
    /// </summary>
    /// <remarks>
    /// The ring is solid and its ground nearly opaque, so the canvas under it only
    /// just shows through. Its wires are drawn at full strength wherever they run, since what feeds the
    /// box and what it feeds are half of why it is being looked into.
    /// </remarks>
    private void DrawPeek(DrawingContext context, CanvasScene scene, IReadOnlySet<Guid> lifted)
    {
        if (scene.Peek is not { } group || scene.OpenGroup(group) is not var (outline, handle)) return;

        context.FillRectangle(PeekScrim, new Rect(ToGraph(default), ToGraph(new Point(Bounds.Width, Bounds.Height))));

        DrawRing(context, group, outline, handle, lifted: true);

        DrawConnections(context, lifted, theirs: false, peeked: true);

        foreach (var node in patch.Nodes)
            if (scene.InPeek(node.Id) && NodeCatalog.Get(node.TypeId) is { } def)
                DrawNode(context, node, def);

        DrawConnections(context, lifted, theirs: true, peeked: true);
    }

    private void DrawRing(DrawingContext context, NodeGroup group, Rect outline, Rect handle, bool lifted)
    {
        // Selected when its modules are, which is the rule a shut box uses —
        // and it is the same gesture that selects them, since pressing the
        // strip takes the group.
        var isSelected = group.Members.Count > 0 && group.Members.All(selection.Contains);

        var label = CanvasText.Text(group.Title(), CanvasText.RowSize, CanvasText.LabelBrush, outline.Width - TabPadding * 2, true);

        // A tab only as wide as the name it carries, sitting on the ring: it
        // joins the name to the region without becoming the header a shut box
        // wears.
        var tab = new Rect(
            handle.X,
            handle.Y,
            Math.Min(label.Width + TabPadding * 2, outline.Width),
            handle.Height);

        var tabShape = new RoundedRect(tab, GroupCornerRadius, GroupCornerRadius, 0, 0);

        // Square where the tab sits, so its foot meets the ring instead of
        // straddling the curve of a corner.
        var flushRight = tab.Width >= outline.Width;
        var ring = new RoundedRect(
            outline, 0, flushRight ? 0 : GroupCornerRadius, GroupCornerRadius, GroupCornerRadius);

        if (lifted)
        {
            context.DrawRectangle(PeekGround, null, ring);

            using (context.PushClip(ring)) DrawGrid(context);
        }

        context.DrawRectangle(
            OpenGroupFill,
            lifted ? PeekPen : isSelected ? OpenGroupPenSelected : OpenGroupPen,
            ring);

        if (lifted) context.DrawRectangle(Background, null, tabShape);

        context.DrawRectangle(isSelected ? OpenGroupTabSelected : OpenGroupTab, null, tabShape);

        context.DrawText(
            label, new Point(tab.X + TabPadding, tab.Y + (tab.Height - label.Height) / 2));
    }

    /// <summary>
    /// Draws a box, faintly and struck through where every module in it is
    /// switched off.
    /// </summary>
    /// <remarks>
    /// The same marking a module that is off wears, for the same reason: a box is
    /// the one place the modules cannot say it themselves. An open group is left
    /// alone — the strike through each of its modules is right there.
    /// </remarks>
    private void DrawBox(DrawingContext context, NodeGroup group, GroupSockets sockets, Rect bounds)
    {
        if (!Scene.SwitchedOff(group))
        {
            DrawBoxFace(context, group, sockets, bounds, off: false);
            return;
        }

        using (context.PushOpacity(OffOpacity))
            DrawBoxFace(context, group, sockets, bounds, off: true);
    }

    private void DrawBoxFace(
        DrawingContext context, NodeGroup group, GroupSockets sockets, Rect bounds, bool off)
    {
        // Selected when its modules are, because pressing the box is what selects
        // them — there is nothing else it could mean for a box to be picked.
        var isSelected = group.Members.Count > 0 && group.Members.All(selection.Contains);

        // A box has no focus of its own — a module does — but the inspector's
        // subject can be one of a shut box's hidden members, the same way
        // pressing the box leaves focus on one of them. That is the rule a
        // module's border follows, so a box follows it too.
        var isFocused = focus is { } f && group.Members.Contains(f);

        var body = new RoundedRect(bounds, NodeGeometry.CornerRadius);

        context.DrawRectangle(
            NodeSkin.Box(isSelected),
            !isSelected ? NodeSkin.Edge : isFocused ? SelectionPen : SelectionPenSecondary,
            body);

        NodeSkin.DrawMark(context, body, ModuleGlyphs.Group, NodeSkin.BoxMark);

        var header = new Rect(bounds.X, bounds.Y, bounds.Width, NodeGeometry.HeaderHeight);
        context.DrawRectangle(
            NodeSkin.BoxHeaderOf(isSelected),
            null,
            new RoundedRect(header, NodeGeometry.CornerRadius, NodeGeometry.CornerRadius, 0, 0));

        NodeSkin.Relief(context, header);

        var title = CanvasText.Text(group.Title(), HeaderSize, HeaderTextBrush, bounds.Width - 16, true);
        var titleAt = new Point(bounds.X + 9, bounds.Y + 5);

        context.DrawText(title, titleAt);

        if (off)
            context.DrawLine(
                OffStrike,
                new Point(titleAt.X, titleAt.Y + title.Height / 2),
                new Point(titleAt.X + title.Width, titleAt.Y + title.Height / 2));

        for (var i = 0; i < sockets.Outputs.Count; i++)
            DrawBoxSocket(context, sockets.Outputs[i], NodeGeometry.GroupOutputPort(bounds, i), bounds);

        for (var i = 0; i < sockets.Inputs.Count; i++)
            DrawBoxSocket(
                context, sockets.Inputs[i], NodeGeometry.GroupInputPort(bounds, sockets, i), bounds);
    }

    /// <summary>
    /// One socket of a box, named for the port inside that it stands for:
    /// "filter.cutoff" rather than a name of its own, so renaming a module inside
    /// relabels the box for nothing.
    /// </summary>
    private void DrawBoxSocket(DrawingContext context, GroupSocket socket, Point center, Rect bounds)
    {
        if (Scene.Named(socket) is not var (label, spec)) return;

        var width = bounds.Width - SocketLabelRoom;
        var text = CanvasText.Text(CanvasText.Fit(label, width), CanvasText.RowSize, CanvasText.LabelBrush, width, true);

        context.DrawText(
            text,
            socket.IsOutput
                ? new Point(bounds.Right - 14 - text.Width, center.Y - text.Height / 2)
                : new Point(bounds.X + 14, center.Y - text.Height / 2));

        NodeSkin.DrawPort(context, center, spec.Kind);
    }

    /// <summary>How much of a box's width its sockets and their margins take from a label.</summary>
    private const double SocketLabelRoom = 26;
}
