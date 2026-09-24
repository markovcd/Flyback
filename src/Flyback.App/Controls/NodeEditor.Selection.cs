using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// Which modules are selected, and the commands that change that.
/// </summary>
public sealed partial class NodeEditor
{

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
    /// Makes the selection exactly one group's modules, which is what the panel
    /// takes for the group itself — what a caret in a group's block in the text
    /// view does.
    /// </summary>
    public void SelectGroup(NodeGroup group)
    {
        if (selection.Count == group.Members.Count && group.Members.All(selection.Contains)) return;

        selection.Clear();
        foreach (var id in group.Members) selection.Add(id);

        focus = group.Members.Count == 0 ? null : group.Members[^1];

        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>
    /// Makes the selection exactly this one module, or nothing at all — what an
    /// ordinary click does, and what every caller outside the pointer handling wants.
    /// </summary>
    /// <remarks>
    /// Public because the canvas is no longer the only thing that points at a module:
    /// a caret in the text view names one too (ADR-0068). It draws as well as selects,
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
