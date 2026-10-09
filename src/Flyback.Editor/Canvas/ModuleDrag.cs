using Avalonia;
using Avalonia.Input;
using Flyback.Core.Graph;

namespace Flyback.Editor.Canvas;

/// <summary>
/// The selection carried under the pointer, and with Shift held, moved between groups
/// where it is let go.
/// </summary>
internal sealed class ModuleDrag(CanvasHistory history, CanvasSelection selection, CanvasEdits edits)
{
    /// <summary>
    /// Where on the canvas a module drag took hold, in the patch's own coordinates, so a
    /// module stays in the hand through a pan and a zoom mid-drag.
    /// </summary>
    private Point grip;

    /// <summary>
    /// Where each module of the selection was when the drag began, so a drag ending
    /// where it started can be told from one that moved.
    /// </summary>
    private readonly Dictionary<Guid, Point> origins = [];

    /// <summary>
    /// A module pressed while already part of a larger selection, which cannot be
    /// resolved until the button comes up: pressing must not narrow the selection, or a
    /// set could never be dragged by one of its members; releasing without a drag must.
    /// </summary>
    private Guid? pendingNarrow;

    /// <summary>
    /// The carried modules Shift would move between groups on release: every one not
    /// riding along with the whole of its own group, and not already where it would go.
    /// </summary>
    private readonly HashSet<Guid> regrouping = [];

    /// <summary>
    /// Where each group a carry reaches into stood when it began, so the carry is back
    /// in its own group whenever it is back over that ground.
    /// </summary>
    private readonly Dictionary<NodeGroup, Rect> startRings = [];

    /// <summary>Whether anything is in the hand.</summary>
    public bool Carrying => origins.Count > 0;

    /// <summary>Whether letting go now moves modules between groups.</summary>
    public bool Regrouping => regrouping.Count > 0;

    /// <summary>
    /// With Shift held, the group the carried modules belong to if let go now, their own
    /// included; null over bare canvas and without Shift.
    /// </summary>
    public NodeGroup? Landing { get; private set; }

    /// <summary>The modules letting go now moves, which their old group's ring is drawn without.</summary>
    public IReadOnlySet<Guid> Regrouped => regrouping;

    /// <summary>Takes hold at <paramref name="graph"/>, which a carry is measured from.</summary>
    public void Grip(Point graph) => grip = graph;

    /// <summary>What a press on a module does to the selection, and the start of a drag of what that leaves selected.</summary>
    public void PressModule(NodeInstance node, bool adding)
    {
        pendingNarrow = null;

        if (adding) selection.Toggle(node.Id);
        else if (!selection.Contains(node.Id)) selection.Select(node.Id);
        else
        {
            pendingNarrow = node.Id;

            // The panel follows the pointer even when the set does not.
            selection.FocusOn(node.Id);
        }

        StartCarrying();
    }

    /// <summary>A press on a box, or on the strip above an open group: what is inside is what gets selected.</summary>
    public void PressGroup(NodeGroup group, bool adding)
    {
        pendingNarrow = null;

        if (adding) selection.Take([.. selection.Ids, .. group.Members], group.Members[^1]);
        else selection.Take(group.Members);

        selection.Announce();

        StartCarrying();
    }

    /// <summary>Brings the selection to the front together and notes where each module started.</summary>
    private void StartCarrying()
    {
        // Nodes paint in list order, so a group being dragged does not pass under
        // members of itself.
        foreach (var moving in selection.Nodes)
        {
            history.Patch.Nodes.Remove(moving);
            history.Patch.Nodes.Add(moving);
        }

        origins.Clear();
        foreach (var moving in selection.Nodes)
            origins[moving.Id] = new Point(moving.X, moving.Y);

        startRings.Clear();

        var scene = selection.Scene;

        foreach (var group in selection.Groups)
            if (scene.OpenGroup(group) is var (outline, handle))
                startRings[group] = outline.Union(handle);
    }

    /// <summary>Carries the selection with the pointer to <paramref name="graph"/>.</summary>
    public void Carry(Point graph, KeyModifiers modifiers)
    {
        var delta = selection.Scene.Held(graph - grip, origins);

        foreach (var moving in selection.Nodes)
        {
            if (!origins.TryGetValue(moving.Id, out var from)) continue;

            moving.X = from.X + delta.X;
            moving.Y = from.Y + delta.Y;
        }

        if (!history.Locked) Aim(graph, modifiers);
    }

    /// <summary>
    /// Lets go at <paramref name="graph"/>. A move is a step and nothing the program can
    /// hear. A press on one module of a group that turned out not to be a drag was a
    /// click, which picks it out. Shift decides at the release as well, so letting go of
    /// it first changes nothing.
    /// </summary>
    public void Drop(Point graph, KeyModifiers modifiers)
    {
        if (!history.Locked && Displaced) Aim(graph, modifiers);
        else regrouping.Clear();

        if (regrouping.Count > 0) edits.Regroup([.. regrouping], Landing);
        else if (!RecordMove() && pendingNarrow is { } one) selection.Select(one);
    }

    /// <summary>Puts every carried module back where it started.</summary>
    public void Abort()
    {
        foreach (var moving in selection.Nodes)
        {
            if (!origins.TryGetValue(moving.Id, out var from)) continue;

            moving.X = from.X;
            moving.Y = from.Y;
        }
    }

    /// <summary>Lets go of everything, as a gesture ends however it ends.</summary>
    public void Clear()
    {
        pendingNarrow = null;
        origins.Clear();
        regrouping.Clear();
        startRings.Clear();
        Landing = null;
    }

    /// <summary>
    /// Works out, with Shift held, which group the carried modules would land in if let
    /// go over <paramref name="graph"/>: the one whose ring or box is under the pointer,
    /// or none.
    /// </summary>
    public void Aim(Point graph, KeyModifiers modifiers)
    {
        regrouping.Clear();
        Landing = null;

        if ((modifiers & KeyModifiers.Shift) == 0) return;

        var patch = history.Patch;
        var carried = origins.Keys.ToHashSet();

        // A whole group rides along as itself, since groups do not nest, and so does the Output.
        var loose = carried
            .Where(id => patch.GroupOf(id) is not { } own || !own.Members.All(carried.Contains))
            .Where(id => patch.Find(id) is { } node && !NodeCatalog.IsSink(node.TypeId))
            .ToArray();

        if (loose.Length == 0) return;

        Landing = selection.Scene.DropTarget(graph, carried, startRings);

        foreach (var id in loose)
            if (patch.GroupOf(id) != Landing)
                regrouping.Add(id);
    }

    /// <summary>
    /// Puts a drag that moved something into the history, and says whether it did. On a
    /// locked canvas the move stands but is not a step: a position the text does not
    /// carry is gone the moment it evaluates again.
    /// </summary>
    public bool RecordMove() => Displaced && (history.Locked || history.RecordMove());

    /// <summary>Whether the carry has taken anything away from where it started.</summary>
    private bool Displaced => selection.Nodes.Any(node =>
        origins.TryGetValue(node.Id, out var from)
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        && (node.X != from.X || node.Y != from.Y));
}
