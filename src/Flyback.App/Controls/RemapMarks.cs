using Avalonia;
using Avalonia.Media;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// A mark in the middle of a wire whose two ends have different ranges, which puts
/// an Auto remap into the wire when clicked (ADR-0130).
/// </summary>
internal sealed class RemapMarks(CanvasHistory history, CanvasSelection selection, Repaint repaint, NodeGeometry geometry)
{
    /// <summary>How far from a mark's center a click or a hover still lands on it, in graph units.</summary>
    private const double MarkReach = 9;

    private const double MarkRadius = 7.5;

    /// <summary>Where along a wire its mark may sit, nearest the middle first.</summary>
    private static readonly double[] MarkStops = [0.5, 0.4, 0.6, 0.3, 0.7, 0.2, 0.8, 0.1, 0.9];

    private static readonly IBrush MarkFill = new SolidColorBrush(Colors.Canvas);
    private static readonly IBrush MarkInk = new SolidColorBrush(Colors.Attention);
    private static readonly IBrush MarkFaint = new SolidColorBrush(Colors.Attention, 0.45);

    /// <summary>The wire whose mark the pointer is over, null while it is over none.</summary>
    private Connection? hovered;

    private Patch Patch => history.Patch;

    /// <summary>The wire whose mark is under <paramref name="graph"/>, where no module covers it.</summary>
    public (Connection Wire, Point At)? At(Point graph)
    {
        var scene = selection.Scene;

        if (history.Locked || scene.Covered(graph) || scene.HitNode(graph) is not null || scene.HitBox(graph) is not null)
            return null;

        foreach (var mark in Marks(scene))
        {
            var (dx, dy) = (mark.At.X - graph.X, mark.At.Y - graph.Y);

            if (dx * dx + dy * dy <= MarkReach * MarkReach) return mark;
        }

        return null;
    }

    /// <summary>Faint on every wire that has one, and bright under the pointer or on a wire that overflows.</summary>
    public void Draw(DrawingContext context)
    {
        if (history.Locked) return;

        foreach (var (wire, at) in Marks(selection.Scene))
        {
            var ink = wire == hovered || AutoRemap.Overflow(Patch, wire) is not null ? MarkInk : MarkFaint;

            context.DrawEllipse(MarkFill, new Pen(ink, 1.2), at, MarkRadius, MarkRadius);

            var arrows = CanvasText.Text("⇄", 11, ink, 20, false);
            context.DrawText(arrows, new Point(at.X - arrows.Width / 2, at.Y - arrows.Height / 2));
        }
    }

    /// <summary>Brightens the mark under the pointer, and lets the last one go.</summary>
    public void Hover(Point graph)
    {
        var over = At(graph)?.Wire;

        if (over == hovered) return;

        hovered = over;
        repaint.Request();
    }

    /// <summary>Puts an Auto remap into <paramref name="wire"/>, centered on its mark and selected, as one edit.</summary>
    public NodeInstance Splice(Connection wire, Point at)
    {
        var def = NodeCatalog.Require(NodeCatalog.AutoRemapTypeId);
        var node = NodeInstance.Create(def, at.X - NodeGeometry.Width / 2, at.Y - geometry.Height(def) / 2);

        Patch.Nodes.Add(node);

        // An input takes one wire, so connecting the remap's output to it replaces
        // the wire that was there.
        Patch.Connect(node.Id, 0, wire.TargetNode, wire.TargetPort);
        Patch.Connect(wire.SourceNode, wire.SourcePort, node.Id, AutoRemap.In);

        hovered = null;
        selection.Select(node.Id);
        history.Record();
        return node;
    }

    /// <summary>Every wire on the canvas that has a mark, and where its middle is.</summary>
    private IEnumerable<(Connection Wire, Point At)> Marks(CanvasScene scene)
    {
        foreach (var wire in Patch.Connections)
        {
            if (scene.Hidden(wire) || !AutoRemap.Offered(Patch, wire)) continue;

            var source = Patch.Find(wire.SourceNode)!;
            var target = Patch.Find(wire.TargetNode)!;
            var sourceDef = NodeCatalog.Require(source.TypeId);
            var targetDef = NodeCatalog.Require(target.TypeId);

            var from = scene.OutputAnchor(source, wire.SourcePort);
            var to = scene.InputAnchor(target, targetDef, wire.TargetPort);

            // A return wire's middle is on its flat run back. A forward one's is
            // halfway along its bezier, or the nearest point to it no module covers.
            if (from.X > to.X)
            {
                yield return (wire, new Point(
                    (from.X + to.X) / 2,
                    WirePath.ReturnRun(geometry.Bounds(source, sourceDef), geometry.Bounds(target, targetDef))));
                continue;
            }

            var along = MarkStops.Select(t => WirePath.At(from, to, t)).ToList();

            yield return (wire, along.FirstOrDefault(p => Clear(scene, p), along[0]));
        }
    }

    /// <summary>Whether a mark at <paramref name="at"/> would be drawn clear of every module and box.</summary>
    private static bool Clear(CanvasScene scene, Point at)
    {
        const double room = MarkRadius + 2;

        return ((Point[])[at, at + new Point(room, 0), at - new Point(room, 0), at + new Point(0, room), at - new Point(0, room)])
            .All(p => scene.HitNode(p) is null && scene.HitBox(p) is null);
    }
}
