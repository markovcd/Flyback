using Avalonia;
using Avalonia.Media;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// A Send and its Receives, which have no wire between them: the bus is written
/// in the header, and a selected end is joined to the others by a dotted line.
/// </summary>
/// <remarks>
/// Straight and dotted, and only while one end is selected, so it cannot be
/// mistaken for a wire, which is curved and always there.
/// </remarks>
public sealed partial class NodeEditor
{
    private static readonly IPen BusPen = new Pen(
        new SolidColorBrush(Colors.Attention, 0.7),
        1.5,
        new DashStyle([1, 3], 0));

    /// <summary>
    /// What a module's header says: its title, and the bus where it is a Send or a
    /// Receive. A module named after its own bus is called what it is instead.
    /// </summary>
    internal static string Heading(NodeInstance node, NodeDef def)
    {
        if (NodeCatalog.BusOf(node) is not { } bus) return node.Title(def);

        var title = string.Equals(node.Title(def), bus, StringComparison.OrdinalIgnoreCase) ? def.Name : node.Title(def);

        return $"{title} · {bus}";
    }

    private void DrawBusLinks(DrawingContext context)
    {
        foreach (var id in selection)
        {
            if (patch.Find(id) is not { } end || NodeCatalog.BusOf(end) is not { } bus) continue;
            if (Scene.Shut(end.Id) || NodeCatalog.Get(end.TypeId) is not { } def) continue;

            var partner = end.TypeId == NodeCatalog.SendTypeId ? NodeCatalog.ReceiveTypeId : NodeCatalog.SendTypeId;
            var from = NodeGeometry.Bounds(end, def);

            foreach (var other in patch.Nodes)
            {
                if (other.TypeId != partner || Scene.Shut(other.Id)) continue;
                if (!string.Equals(NodeCatalog.BusOf(other), bus, StringComparison.OrdinalIgnoreCase)) continue;
                if (NodeCatalog.Get(other.TypeId) is not { } otherDef) continue;

                var to = NodeGeometry.Bounds(other, otherDef);

                context.DrawLine(BusPen, Edge(from, to.Center), Edge(to, from.Center));
            }
        }
    }

    /// <summary>Where the line from the middle of <paramref name="box"/> towards <paramref name="toward"/> leaves it.</summary>
    private static Point Edge(Rect box, Point toward)
    {
        var d = toward - box.Center;

        if (d.X == 0 && d.Y == 0) return box.Center;

        var t = Math.Min(
            d.X == 0 ? double.MaxValue : box.Width / 2 / Math.Abs(d.X),
            d.Y == 0 ? double.MaxValue : box.Height / 2 / Math.Abs(d.Y));

        return box.Center + d * Math.Min(t, 1);
    }
}
