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

    /// <summary>
    /// Whether modules are drawn compact: an input and the output of the same index
    /// share a row and knob values move to the socket's tooltip. Otherwise outputs
    /// are listed first, then inputs, the Blender convention. Set from Settings, Canvas.
    /// </summary>
    public static bool Compact { get; set; }

    private static int Rows(int inputs, int outputs) => Compact ? Math.Max(inputs, outputs) : inputs + outputs;

    public static double Height(NodeDef def) =>
        HeaderHeight + Rows(def.Inputs.Count, def.Outputs.Count) * RowHeight + FooterPadding;

    public static Rect Bounds(NodeInstance node, NodeDef def) =>
        new(node.X, node.Y, Width, Height(def));

    /// <summary>The middle of row <paramref name="index"/>, measured from the top of the module.</summary>
    public static double Row(int index) => HeaderHeight + (index + 0.5) * RowHeight;

    public static Point OutputPort(NodeInstance node, int index) =>
        new(node.X + Width, node.Y + Row(index));

    public static Point InputPort(NodeInstance node, NodeDef def, int index) =>
        new(node.X, node.Y + Row(Compact ? index : def.Outputs.Count + index));

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
        HeaderHeight + Math.Max(Rows(sockets.Inputs.Count, sockets.Outputs.Count), 1) * RowHeight + FooterPadding;

    /// <summary>
    /// Where the box sits: the top left of the modules it stands for.
    /// </summary>
    /// <remarks>
    /// Derived rather than stored, which keeps collapsing and expanding exactly
    /// reversible — there is no second position to drift out of step. The corner
    /// rather than the middle of their bounding box, because a coordinate names a
    /// corner everywhere else here, and a box that grew downward as sockets appeared
    /// would move when it was not dragged.
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
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (x == double.MaxValue) return default;

        return new Rect(x, y, Width, GroupHeight(sockets));
    }

    public static Point GroupOutputPort(Rect bounds, int index) => new(bounds.Right, bounds.Y + Row(index));

    public static Point GroupInputPort(Rect bounds, GroupSockets sockets, int index) =>
        new(bounds.X, bounds.Y + Row(Compact ? index : sockets.Outputs.Count + index));

    // --- an open group -------------------------------------------------------
    //
    // Nothing of the module's shape here: an open group draws no header and no
    // sockets, only a ring round the modules standing in it and a strip above
    // that to take hold of.

    /// <summary>The ring drawn round a group that is open, on all four sides.</summary>
    public const double GroupPadding = 24;

    /// <summary>The strip above that ring, which the group's name is written on.</summary>
    public const double GroupHandleHeight = 20;

    /// <summary>
    /// These same numbers, in the shape the layout wants them — plus how much room to
    /// leave between the nodes, which is the only part the editor decides rather than
    /// draws.
    /// </summary>
    /// <remarks>
    /// The layout lives in the engine because the assistant's workbench wants it and
    /// has no canvas to ask, so the sizes travel to it. Wide enough between columns
    /// for the wires to be followed, and about a row's worth between nodes.
    /// </remarks>
    public static PatchLayout.Metrics Metrics => new(
        Width,
        HeaderHeight,
        RowHeight,
        FooterPadding,
        ColumnGap: 108,
        RowGap: 40,
        GroupPadding,
        GroupHandleHeight) { SharedRows = Compact };
}
