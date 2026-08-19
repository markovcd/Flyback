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
