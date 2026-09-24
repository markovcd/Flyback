using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// Which modules are selected, which of them the inspector is about, and the shut
/// box being looked into.
/// </summary>
/// <remarks>
/// A set rather than one id, so a gesture can name several: dragging a group and
/// copying one both need that. A shut box is selected whole or not at all.
/// </remarks>
internal sealed class CanvasSelection
{
    private readonly CanvasHistory history;
    private readonly Repaint repaint;
    private readonly ReportLine report;

    private readonly HashSet<Guid> ids = [];

    /// <summary>The shut box being looked into, which lives here and not in the patch.</summary>
    private Guid? peek;

    public CanvasSelection(CanvasHistory history, Repaint repaint, ReportLine report)
    {
        this.history = history;
        this.repaint = repaint;
        this.report = report;

        history.Replaced += (_, how) =>
        {
            if (how == Replacement.Opened) Reset();
            else Keep();

            // Every module is a fresh object after the patch is swapped, so anything
            // holding one has to be built again whether or not the ids changed.
            Announce();
        };
    }

    /// <summary>A different set of modules is selected, or the focus moved within it.</summary>
    public event EventHandler? Changed;

    private Patch Patch => history.Patch;

    public IReadOnlySet<Guid> Ids => ids;

    public int Count => ids.Count;

    /// <summary>
    /// Which of the selected modules the inspector is about: one of <see cref="Ids"/>
    /// or nothing, and the last one the pointer named.
    /// </summary>
    public Guid? Focus { get; private set; }

    /// <summary>The module the inspector is about, null when nothing is selected.</summary>
    public NodeInstance? Focused => Focus is { } id ? Patch.Find(id) : null;

    /// <summary>Every selected module, in the order the patch holds them, so a selection reads the same way twice.</summary>
    public IReadOnlyList<NodeInstance> Nodes => [.. Patch.Nodes.Where(node => ids.Contains(node.Id))];

    /// <summary>What the canvas shows of the patch, and what is under a point on it.</summary>
    public CanvasScene Scene => new(Patch, Peeked);

    public bool Contains(Guid id) => ids.Contains(id);

    /// <summary>The group the selection is exactly, null where it is anything else.</summary>
    public NodeGroup? Group
    {
        get
        {
            if (Patch.Groups is null || ids.Count == 0) return null;

            foreach (var group in Patch.Groups)
                if (group.Members.Count == ids.Count && group.Members.All(ids.Contains))
                    return group;

            return null;
        }
    }

    /// <summary>Every group the selection reaches into, shut or open. One member is enough.</summary>
    public IEnumerable<NodeGroup> Groups => Patch.Groups?.Where(g => g.Members.Any(ids.Contains)) ?? [];

    /// <summary>
    /// The shut box being looked into: drawn open over everything else, and still shut
    /// in the patch, the file and the history.
    /// </summary>
    /// <remarks>
    /// Forgotten once the box is gone, opened for good, or once anything outside it is
    /// selected: what is selected is where the work is, and a ring covering it would be
    /// in the way.
    /// </remarks>
    public NodeGroup? Peeked
    {
        get
        {
            if (peek is not { } id) return null;

            if (Patch.Groups?.FirstOrDefault(g => g.Id == id) is { Collapsed: true } group
                && ids.All(group.Members.Contains))
                return group;

            peek = null;
            return null;
        }
    }

    /// <summary>
    /// Makes the selection exactly this one module, or nothing: what an ordinary click
    /// does, and what a caret in the text naming a module does (ADR-0068).
    /// </summary>
    public void Select(Guid? id)
    {
        if (Focus == id && ids.Count == (id is null ? 0 : 1)) return;

        ids.Clear();
        if (id is { } one) ids.Add(one);

        Focus = id;
        Announce();
    }

    /// <summary>
    /// Selects every module that is drawn. One whose plugin is missing has no size, and
    /// selecting it would be the one way to drag or delete something invisible.
    /// </summary>
    public void SelectAll()
    {
        var all = Patch.Nodes
            .Where(node => NodeCatalog.Get(node.TypeId) is not null)
            .Select(node => node.Id)
            .ToArray();

        if (all.Length == ids.Count && all.All(ids.Contains)) return;

        // The focus on the last in the patch's order, which is the one drawn on top.
        Take(all, all.Length == 0 ? null : all[^1]);
        Announce();
    }

    /// <summary>Makes the selection exactly one group's modules, which the panel takes for the group itself.</summary>
    public void SelectGroup(NodeGroup group)
    {
        if (ids.Count == group.Members.Count && group.Members.All(ids.Contains)) return;

        Take(group.Members);
        Announce();
    }

    /// <summary>
    /// Adds a module, or takes it out if it was in: what Ctrl turns a click into. Taking
    /// the focused one out moves the focus to the module drawn on top.
    /// </summary>
    public void Toggle(Guid id)
    {
        if (!ids.Add(id))
        {
            ids.Remove(id);

            if (Focus == id) Focus = ids.Count == 0 ? null : Nodes[^1].Id;
        }
        else
        {
            Focus = id;
        }

        Announce();
    }

    /// <summary>
    /// Makes the selection <paramref name="members"/>, focused on <paramref name="focus"/>
    /// or on the last of them, without announcing it.
    /// </summary>
    public void Take(IReadOnlyList<Guid> members, Guid? focus = null)
    {
        ids.Clear();
        ids.UnionWith(members);

        Focus = focus ?? (members.Count == 0 ? null : members[^1]);
    }

    /// <summary>Makes the selection <paramref name="members"/>, leaving the focus where it was, without announcing it.</summary>
    public void Replace(IEnumerable<Guid> members)
    {
        var wanted = members.ToArray();

        ids.Clear();
        ids.UnionWith(wanted);
    }

    /// <summary>Adds <paramref name="members"/> without announcing it.</summary>
    public void Add(IEnumerable<Guid> members) => ids.UnionWith(members);

    /// <summary>Takes a module out without announcing it, and says whether it was in.</summary>
    public bool Remove(Guid id) => ids.Remove(id);

    /// <summary>Moves the inspector to <paramref name="id"/>, which must be selected, and announces it.</summary>
    public void FocusOn(Guid id)
    {
        if (Focus == id) return;

        Focus = id;
        Announce();
    }

    /// <summary>
    /// Leaves the inspector about something selected wherever anything is: the module it
    /// was about if still selected, and otherwise the one drawn on top.
    /// </summary>
    public void Refocus()
    {
        if (Focus is { } kept && ids.Contains(kept)) return;

        Focus = Patch.Nodes.LastOrDefault(node => ids.Contains(node.Id))?.Id;
    }

    /// <summary>Focuses the module drawn on top of the selection, whatever it was about before.</summary>
    public void FocusTop() => Focus = Patch.Nodes.LastOrDefault(node => ids.Contains(node.Id))?.Id;

    /// <summary>
    /// Takes in the rest of every shut box the selection reaches into, and says whether
    /// that changed anything.
    /// </summary>
    /// <remarks>
    /// A member nobody can see answering Delete or riding along on a drag would be the
    /// module under a box answering a click again. Asked wherever a box can shut round
    /// a selection: the gesture, a layout that shut one to fit, and the history.
    /// </remarks>
    public bool WholeBoxes()
    {
        if (Patch.Groups is null || ids.Count == 0) return false;

        var before = ids.Count;
        var peeked = Peeked;

        foreach (var group in Patch.Groups)
            if (group.Collapsed && group != peeked && group.Members.Any(ids.Contains))
                ids.UnionWith(group.Members);

        return ids.Count != before;
    }

    /// <summary>
    /// Looks into a shut box without opening it, until Escape or a click outside. Not an
    /// edit, so a locked canvas does it too.
    /// </summary>
    public void Peek(NodeGroup group)
    {
        if (!group.Collapsed) return;

        Take(group.Members);
        peek = group.Id;

        Announce();
        report.Say($"Looking into {group.Title()}. Esc or a click outside puts it back.");
    }

    /// <summary>Puts the box being looked into back, and says whether there was one.</summary>
    public bool EndPeek()
    {
        if (Peeked is null) return false;

        peek = null;

        // A module picked out inside is behind the box again.
        if (WholeBoxes()) Announce();

        repaint.Request();
        return true;
    }

    /// <summary>Tells whoever is listening that the selection moved.</summary>
    public void Announce()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        repaint.Request();
    }

    /// <summary>A new document: nothing selected and nothing looked into.</summary>
    private void Reset()
    {
        ids.Clear();
        Focus = null;
        peek = null;
    }

    /// <summary>
    /// The patch came back out of the history: the selection survives only where what
    /// it named does, and a box the step shut is selected whole.
    /// </summary>
    private void Keep()
    {
        ids.RemoveWhere(id => Patch.Find(id) is null);

        WholeBoxes();
        Refocus();
    }
}
