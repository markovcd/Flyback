using Avalonia;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// Where every part of a node sits. The editor draws itself rather than
/// composing real controls, so these are the single source of truth for both
/// painting and hit-testing.
/// </summary>
internal static class NodeGeometry
{
    public const double Width = 196;
    public const double HeaderHeight = 26;
    public const double RowHeight = 20;
    public const double FooterPadding = 8;
    public const double PortRadius = 5.5;
    public const double HitPadding = 5;
    public const double CornerRadius = 6;

    /// <summary>Outputs are listed first, then inputs — the Blender convention.</summary>
    public static double Height(NodeDef def) =>
        HeaderHeight + (def.Inputs.Count + def.Outputs.Count) * RowHeight + FooterPadding;

    public static Rect Bounds(NodeInstance node, NodeDef def) =>
        new(node.X, node.Y, Width, Height(def));

    public static Point OutputPort(NodeInstance node, int index) =>
        new(node.X + Width, node.Y + HeaderHeight + (index + 0.5) * RowHeight);

    public static Point InputPort(NodeInstance node, NodeDef def, int index) =>
        new(node.X, node.Y + HeaderHeight + (def.Outputs.Count + index + 0.5) * RowHeight);

    // --- a collapsed group ---------------------------------------------------
    //
    // The same shape as a module and drawn from the same numbers, because that
    // is what it stands in for: a box with a header, outputs down one side and
    // inputs down the other. Only where it sits and how many rows it has come
    // from anywhere different.

    /// <summary>
    /// A box always has a header and a floor to stand on, whatever crosses its
    /// boundary — a group nothing is wired into or out of is still a box.
    /// </summary>
    public static double GroupHeight(GroupSockets sockets) =>
        HeaderHeight + Math.Max(sockets.Rows, 1) * RowHeight + FooterPadding;

    /// <summary>
    /// Where the box sits: the top left of the modules it stands for.
    /// </summary>
    /// <remarks>
    /// Derived rather than stored, which is what keeps collapsing and expanding
    /// exactly reversible — there is no second position to drift out of step
    /// with the first. Dragging a collapsed group moves its modules, so the box
    /// remembers where it was put by their remembering it.
    /// <para>
    /// The corner rather than the middle of their bounding box, because a
    /// coordinate names a corner everywhere else here and a box that grew
    /// downward from its centre as sockets appeared would be the one thing on
    /// the canvas that moved when it was not dragged.
    /// </para>
    /// </remarks>
    public static Rect GroupBounds(Patch patch, NodeGroup group, GroupSockets sockets)
    {
        var x = double.MaxValue;
        var y = double.MaxValue;

        foreach (var id in group.Members)
            if (patch.Find(id) is { } node)
            {
                x = Math.Min(x, node.X);
                y = Math.Min(y, node.Y);
            }

        // A group whose modules have all gone is drawn nowhere rather than at
        // infinity. Patch.Remove drops one before it can happen, and this is
        // what a hand-edited file gets instead of a crash.
        if (x == double.MaxValue) return default;

        return new Rect(x, y, Width, GroupHeight(sockets));
    }

    public static Point GroupOutputPort(Rect bounds, int index) =>
        new(bounds.Right, bounds.Y + HeaderHeight + (index + 0.5) * RowHeight);

    public static Point GroupInputPort(Rect bounds, GroupSockets sockets, int index) =>
        new(bounds.X, bounds.Y + HeaderHeight + (sockets.Outputs.Count + index + 0.5) * RowHeight);

    /// <summary>
    /// These same numbers, in the shape the layout wants them — plus how much
    /// room to leave between the nodes, which is the only part of this the
    /// editor decides rather than draws.
    /// </summary>
    /// <remarks>
    /// The layout lives in the engine because the assistant's workbench wants it
    /// too and has no canvas to ask. So the sizes travel to it rather than the
    /// other way round, and this is the one place they are handed over.
    /// <para>
    /// Wide enough between columns for the wires to be followed, and about a
    /// row's worth between nodes: closer and two modules read as one block, and
    /// further and a patch of any size stops fitting on a screen.
    /// </para>
    /// </remarks>
    public static PatchLayout.Metrics Metrics => new(
        Width, HeaderHeight, RowHeight, FooterPadding, ColumnGap: 108, RowGap: 40);
}
