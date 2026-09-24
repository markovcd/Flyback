using Avalonia;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// The commands that change the patch on the canvas: adding and removing modules,
/// switching them off, grouping them, pasting a fragment and laying the lot out.
/// </summary>
/// <remarks>
/// Each is one step however many modules it touches, because one gesture asked for
/// all of them (ADR-0044). None of them is refused here on a locked canvas: the
/// gestures and keys that reach them are.
/// </remarks>
internal sealed class CanvasEdits(
    CanvasHistory history,
    CanvasSelection selection,
    Viewport view,
    Repaint repaint,
    ReportLine report)
{
    /// <summary>How far a duplicate or a paste steps clear of what is already there.</summary>
    private const double Step = 28;

    private Patch Patch => history.Patch;

    /// <summary>
    /// Drops a new module on the canvas and hands it back, or selects the one already
    /// there where the patch may not hold another: the Output, of which there is one.
    /// </summary>
    /// <param name="typeId">Which module to add.</param>
    /// <param name="at">Where to center it, in graph space; the middle of the view when null.</param>
    public NodeInstance? AddNode(string typeId, Point? at = null)
    {
        var def = NodeCatalog.Require(typeId);

        if (!Patch.CanAdd(typeId))
        {
            if (Patch.FirstOf(typeId) is { } already) selection.Select(already.Id);

            repaint.Request();
            return null;
        }

        var node = Created(ref def, at ?? view.Middle);

        Patch.Nodes.Add(node);
        selection.Select(node.Id);
        history.Record();
        return node;
    }

    /// <summary>
    /// Adds a module where a wire was dropped and plugs the wire into it, as one edit.
    /// Which socket it lands on is <see cref="WireDrop.SocketOn"/>'s decision.
    /// </summary>
    public NodeInstance? AddNodeWired(string typeId, WireDrop drop)
    {
        var def = NodeCatalog.Require(typeId);

        if (!Patch.CanAdd(typeId)) return AddNode(typeId, drop.At);

        var node = Created(ref def, drop.At);

        Patch.Nodes.Add(node);

        if (drop.SocketOn(def) is { } socket)
        {
            if (drop.FromOutput) Patch.Connect(drop.Node, drop.Port, node.Id, socket);
            else Patch.Connect(node.Id, socket, drop.Node, drop.Port);
        }

        selection.Select(node.Id);
        history.Record();
        return node;
    }

    /// <summary>
    /// A new module centered on <paramref name="center"/>, and for a Maths module an
    /// Expression stands for, the Expression it is instead (ADR-0109).
    /// </summary>
    private static NodeInstance Created(ref NodeDef def, Point center)
    {
        var x = center.X - NodeGeometry.Width / 2;

        if (ExpressionFusion.Standing(def, NodeCatalog.Current, 0, 0) is { } standing)
        {
            def = NodeCatalog.Require(NodeCatalog.ExpressionTypeId);
            standing.X = x;
            standing.Y = center.Y - NodeGeometry.Height(def) / 2;
            return standing;
        }

        return NodeInstance.Create(def, x, center.Y - NodeGeometry.Height(def) / 2);
    }

    /// <summary>How many of the selected modules can be switched off: all but the Output.</summary>
    public int Switchable => selection.Nodes.Count(n => !NodeCatalog.IsSink(n.TypeId));

    /// <summary>Whether the next press would switch the selection back on, which it does only for one entirely off.</summary>
    public bool SelectionIsOff =>
        Switchable > 0 && selection.Nodes.Where(n => !NodeCatalog.IsSink(n.TypeId)).All(n => n.Off);

    /// <summary>
    /// Switches the selected modules off, or back on where every one of them is already
    /// off, so a press never leaves them half and half.
    /// </summary>
    public void SwitchSelected()
    {
        var switching = selection.Nodes.Where(n => !NodeCatalog.IsSink(n.TypeId)).ToArray();

        if (switching.Length == 0) return;

        var off = !SelectionIsOff;

        foreach (var node in switching) node.Off = off;

        history.Record();

        var what = off ? "Switched off" : "Switched on";

        report.Say(switching.Length == 1 ? $"{what} one module." : $"{what} {switching.Length} modules.");
    }

    /// <summary>
    /// Removes every selected module but the Output, which the graph refuses and which
    /// stays selected, so its panel stays.
    /// </summary>
    public void DeleteSelected()
    {
        if (selection.Count == 0) return;

        var went = false;

        foreach (var id in selection.Ids.ToArray())
            if (Patch.Remove(id))
            {
                selection.Remove(id);
                went = true;
            }

        if (!went) return;

        selection.Refocus();
        selection.Announce();
        history.Record();
    }

    /// <summary>
    /// How many of the selected modules a group would take: all but the Output. Agrees
    /// with <see cref="Patch.Group"/> exactly.
    /// </summary>
    public int Groupable => selection.Nodes.Count(n => !NodeCatalog.IsSink(n.TypeId));

    /// <summary>Draws the selected modules as one box.</summary>
    public void GroupSelected()
    {
        // Grouping a group again would dissolve it to make it: a new box round the
        // same modules, without its name and the sockets left on its edge.
        if (selection.Group is not null)
        {
            report.Say("These modules are a group already. Ctrl+Shift+G ungroups them.");
            return;
        }

        if (Patch.Group(selection.Ids) is not { } made)
        {
            // Only where something was selected: a key pressed on an empty canvas is
            // a key pressed by mistake.
            if (Groupable > 0)
                report.Say(
                    $"A group needs {NodeGroup.Fewest} modules or more — a box round one "
                    + "would be the module again with a second name.");

            return;
        }

        // What was selected stays selected, so a second Ctrl+G has something to ungroup.
        selection.Take(made.Members);
        selection.Announce();
        history.Record();

        report.Say(
            $"Grouped {made.Members.Count} modules. "
            + "Ctrl+Shift+G ungroups them, double-click looks inside.");
    }

    /// <summary>Stops drawing whatever groups the selection is inside, leaving every module and wire where it was.</summary>
    public void UngroupSelected()
    {
        if (selection.Count == 0) return;

        var going = selection.Groups.Select(g => g.Id).ToArray();

        if (going.Length == 0) return;

        foreach (var id in going) Patch.Ungroup(id);

        history.Record();
        report.Say(going.Length == 1 ? "Ungrouped." : $"Ungrouped {going.Length} groups.");
    }

    /// <summary>Shuts a group that is open, or opens one that is shut.</summary>
    public void ToggleBox(NodeGroup group)
    {
        group.Collapsed = !group.Collapsed;

        // Opening one selects what came out, so the panel is about the modules.
        if (!group.Collapsed)
        {
            selection.Take(group.Members);
            selection.Announce();
        }

        history.Record();
    }

    /// <summary>Toggles whatever group the selection is exactly, if it is one.</summary>
    public void ToggleSelectedGroup()
    {
        if (selection.Group is { } group) ToggleBox(group);
    }

    /// <summary>Opens every box the selection reaches into.</summary>
    public void OpenSelectedGroups() => SetBoxes(collapsed: false);

    /// <summary>Shuts every group the selection reaches into that is open.</summary>
    public void CloseSelectedGroups() => SetBoxes(collapsed: true);

    /// <summary>
    /// Draws every group the selection reaches into the same way round. Two gestures
    /// rather than a toggle: one shut group and one open one have no state to flip to.
    /// </summary>
    private void SetBoxes(bool collapsed)
    {
        if (selection.Count == 0) return;

        var moved = selection.Groups.Where(g => g.Collapsed != collapsed).ToArray();

        if (moved.Length == 0) return;

        foreach (var group in moved) group.Collapsed = collapsed;

        // Opening takes in what came out, so the panel is about modules on the canvas.
        // Shutting takes in the rest of each box: what was selected inside one is
        // about to be out of sight.
        if (!collapsed)
        {
            foreach (var group in moved) selection.Add(group.Members);

            selection.FocusTop();
            selection.Announce();
        }
        else if (selection.WholeBoxes())
        {
            selection.Announce();
        }

        history.Record();

        var what = collapsed ? "Shut" : "Opened";

        report.Say(moved.Length == 1 ? $"{what} one group." : $"{what} {moved.Length} groups.");
    }

    /// <summary>
    /// Takes a socket off a box's edge. Refused while a wire is on it: a crossing wire
    /// is a socket whatever the stored list says, so the row would still be there.
    /// </summary>
    public void HideSocket(NodeGroup group, GroupSocket socket)
    {
        if (Patch.Wired(group, socket)) return;
        if (!group.Hide(socket)) return;

        history.Record();
    }

    /// <summary>Puts an unwired socket of a module inside a box on its edge.</summary>
    public void ExposeSocket(NodeGroup group, GroupSocket socket)
    {
        if (!Patch.Exposable(group, socket)) return;
        if (!group.Expose(socket)) return;

        history.Record();
    }

    /// <summary>
    /// Puts a second copy of the selection a step down and right of the original, and
    /// selected, leaving the clipboard alone.
    /// </summary>
    public void DuplicateSelection()
    {
        if (selection.Count == 0) return;

        var fragment = PatchClipboard.Copy(Patch, selection.Ids);

        // Selected, and yet nothing of it can be duplicated, which can only be the
        // Output on its own. Said, because a gesture that does nothing reads as broken.
        if (fragment.Nodes.Count == 0)
        {
            report.Say("The Output cannot be duplicated.");
            return;
        }

        AddFragment(fragment, CanvasScene.Drawn(fragment, fragment.Nodes).Center + new Vector(Step, Step));
    }

    /// <summary>
    /// Merges a fragment into the patch and leaves what arrived selected, so it can be
    /// dragged straight into place.
    /// </summary>
    /// <param name="fragment">What to add. Not modified, so the same one may be added again.</param>
    /// <param name="at">
    /// Where its middle should land, or null for the middle of the view, stepped clear
    /// of what is there.
    /// </param>
    public IReadOnlyList<NodeInstance> AddFragment(Patch fragment, Point? at = null)
    {
        var arriving = fragment.Nodes.Where(n => !NodeCatalog.IsSink(n.TypeId)).ToArray();
        if (arriving.Length == 0) return [];

        var box = CanvasScene.Drawn(fragment, arriving);

        var (dx, dy) = at is { } point
            ? (point.X - box.Center.X, point.Y - box.Center.Y)
            : WhereToPaste(box);

        var added = PatchClipboard.Paste(Patch, fragment, dx, dy);
        if (added.Count == 0) return [];

        selection.Take([.. added.Select(node => node.Id)]);
        selection.Announce();
        history.Record();

        return added;
    }

    /// <summary>
    /// How far to shift what is arriving so it lands in the middle of the view, clear of
    /// anything already there.
    /// </summary>
    /// <remarks>
    /// Stepped down and right until it sits on nothing, since landing on top of what is
    /// there reads as nothing having happened. The steps are capped: a dense patch has no
    /// clear middle, and what arrives is selected, so moving it is one drag.
    /// </remarks>
    private (double X, double Y) WhereToPaste(Rect group)
    {
        const int tries = 40;

        var taken = selection.Scene.OnCanvas().ToArray();

        var center = view.Middle;
        var dx = center.X - group.Center.X;
        var dy = center.Y - group.Center.Y;

        for (var s = 0; s < tries; s++)
        {
            // Inflated, so "clear of" means with room to see the edge.
            var moved = group.Translate(new Vector(dx, dy)).Inflate(Step);
            if (!taken.Any(box => box.Intersects(moved))) break;

            dx += Step;
            dy += Step;
        }

        return (dx, dy);
    }

    /// <summary>
    /// Lays the patch out so it reads left to right with its wires clear of one another,
    /// and frames it. Nothing the compiler reads changes (ADR-0044); a box shut to make
    /// it fit is a fact about the canvas (ADR-0092).
    /// </summary>
    /// <param name="onlySelected">Lay out the selection alone, leaving the rest and the view where they are (ADR-0110).</param>
    public void Tidy(bool onlySelected = false)
    {
        if (Patch.Nodes.Count == 0) return;

        if (onlySelected && selection.Count == 0)
        {
            report.Say("Nothing is selected. Ctrl+L lays the whole patch out.");
            return;
        }

        var laid = PatchLayout.Arrange(
            Patch,
            NodeCatalog.Current,
            NodeGeometry.Metrics,
            onlySelected ? selection.Ids : null);

        if (!laid.Fitted)
        {
            report.Say(
                $"This {(onlySelected ? "selection" : "patch")} is too big to draw on the canvas "
                + "even with every box shut, so nothing has been moved.");

            return;
        }

        // A box the layout shut may have had part of itself selected.
        if (selection.WholeBoxes()) selection.Announce();

        history.Record();

        // Moving the view would lose the part of the patch being worked on, which is
        // the whole of why only part of it was laid out.
        if (!onlySelected) view.FrameAll();

        if (laid.Shut.Count > 0)
            report.Say(
                $"With every box open this {(onlySelected ? "selection" : "patch")} is wider than "
                + $"the canvas, so {Named(laid.Shut)} {(laid.Shut.Count == 1 ? "was" : "were")} shut.");
    }

    /// <summary>What to call the boxes the layout shut: three by name at most, since the line is one line.</summary>
    private static string Named(IReadOnlyList<NodeGroup> groups)
    {
        var names = groups.Take(3).Select(group => group.Title()).ToArray();
        var rest = groups.Count - names.Length;

        if (rest > 0) return $"{string.Join(", ", names)} and {rest} more";

        return names.Length == 1 ? names[0] : $"{string.Join(", ", names[..^1])} and {names[^1]}";
    }
}
