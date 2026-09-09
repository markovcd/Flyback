using Avalonia;
using Avalonia.Media;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// Turning graph coordinates into screen ones, and everything drawn with them.
/// </summary>
/// <remarks>
/// Pan and zoom are one matrix pushed around the whole render, and the socket
/// positions drawn here come from the same <see cref="NodeGeometry"/> the hit
/// testing measures with — which is what stops a wire's end and the dot it is
/// drawn on from ever disagreeing (ADR-0017).
/// </remarks>
public sealed partial class NodeEditor
{
    // --- coordinate transforms ----------------------------------------------

    /// <summary>
    /// Internal rather than private because the UI tests need it: painting works
    /// inside this transform and so never asks where a socket ended up on the
    /// control, which is precisely the question a test about hit-testing asks.
    /// </summary>
    internal Matrix GraphToScreen =>
        Matrix.CreateScale(zoom, zoom) * Matrix.CreateTranslation(pan.X, pan.Y);

    private Point ToGraph(Point screen) =>
        new((screen.X - pan.X) / zoom, (screen.Y - pan.Y) / zoom);

    // --- painting ------------------------------------------------------------

    public override void Render(DrawingContext context)
    {
        // Two grounds rather than one. The canvas is a sheet of finite size and
        // the rest of the control is whatever lies past it, which the bounded
        // pan lets you see a little of — that strip is the whole of what makes
        // the edge a thing to look at rather than a stop to walk into.
        context.FillRectangle(Beyond, new Rect(Bounds.Size));
        context.FillRectangle(Background, OnScreen(CanvasBounds));

        using (context.PushTransform(GraphToScreen))
        {
            DrawGrid(context);

            // A module being dragged is the only thing on the canvas that is
            // moving, and its wires are what one is watching while it moves. So
            // they are drawn after the modules rather than before: nothing the
            // block is pulled across can hide where it is still patched, which
            // is the whole question being asked by the drag.
            var lifted = drag == Drag.Node ? selection : [];

            // Under the modules and under the wires both, because it is the
            // ground a group stands on rather than anything in the patch.
            DrawOpenGroups(context);

            DrawConnections(context, lifted, theirs: false);

            foreach (var node in patch.Nodes)
                if (!Shut(node.Id) && NodeCatalog.Get(node.TypeId) is { } def)
                    DrawNode(context, node, def);

            foreach (var (group, sockets, bounds) in Boxes())
                DrawBox(context, group, sockets, bounds);

            DrawConnections(context, lifted, theirs: true);

            DrawPendingWire(context);
        }

        // Outside the transform, so a hairline stays a hairline and the dashes
        // keep their spacing however far out the view is zoomed. These are drawn
        // on the canvas rather than in it — neither is part of the patch.
        DrawEdge(context);
        DrawMarquee(context);
    }

    /// <summary>
    /// Rules the edge of the canvas.
    /// </summary>
    /// <remarks>
    /// Over the modules rather than under them, so that the one standing hard
    /// against an edge has the line across it and the rest of its body out on
    /// the far side. That is what the module's own bound looks like: a
    /// coordinate names a corner, and the body hangs off it.
    /// </remarks>
    private void DrawEdge(DrawingContext context) =>
        context.DrawRectangle(null, EdgePen, OnScreen(CanvasBounds));

    /// <summary>A rectangle of graph units, in the control's own coordinates.</summary>
    private Rect OnScreen(Rect graph) => graph.TransformToAABB(GraphToScreen);

    private void DrawMarquee(DrawingContext context)
    {
        if (drag != Drag.Marquee) return;

        var band = Band(
            GraphToScreen.Transform(marqueeFrom),
            GraphToScreen.Transform(marqueeTo));

        // A band with no width or height is a click that has not moved yet, and
        // a line of dashes across the canvas is not what that looks like.
        if (band.Width < 1 || band.Height < 1) return;

        context.DrawRectangle(MarqueeFill, MarqueePen, band);
    }

    /// <summary>
    /// The rectangle between two corners, whichever way round they are.
    /// </summary>
    /// <remarks>
    /// Built by hand rather than from <c>new Rect(a, b)</c>, which takes the
    /// first point as the top left and subtracts. Started from any corner but
    /// the top left that gives a negative width or height — a rectangle which
    /// draws nothing and intersects nothing, so the band would appear to do
    /// nothing at all.
    /// </remarks>
    private static Rect Band(Point a, Point b) => new(
        Math.Min(a.X, b.X),
        Math.Min(a.Y, b.Y),
        Math.Abs(b.X - a.X),
        Math.Abs(b.Y - a.Y));

    private void DrawGrid(DrawingContext context)
    {
        // What is being looked at, and never more of it than there is canvas.
        // The grid is what says where a module would land, so ruling ground no
        // module may stand on would be a lie told in the one part of the view
        // that has nothing else in it to read.
        var visible = new Rect(
                ToGraph(new Point(0, 0)),
                ToGraph(new Point(Bounds.Width, Bounds.Height)))
            .Intersect(CanvasBounds);

        // Zoomed in hard against an edge, the canvas can be off the view
        // entirely bar the line around it.
        if (visible.Width <= 0 || visible.Height <= 0) return;

        const double spacing = 48;

        // Zoomed far out the grid stops being useful and only costs draw calls.
        if (visible.Width / spacing > 400) return;

        // Rounded up rather than down: a line started just short of the canvas
        // would land off the control and invisible, rather than ruling the
        // ground past the edge.
        var firstX = Math.Ceiling(visible.X / spacing) * spacing;
        var firstY = Math.Ceiling(visible.Y / spacing) * spacing;

        for (var x = firstX; x <= visible.Right; x += spacing)
        {
            var pen = Math.Abs(x % (spacing * 5)) < 0.5 ? GridPenMajor : GridPen;
            context.DrawLine(pen, new Point(x, visible.Y), new Point(x, visible.Bottom));
        }

        for (var y = firstY; y <= visible.Bottom; y += spacing)
        {
            var pen = Math.Abs(y % (spacing * 5)) < 0.5 ? GridPenMajor : GridPen;
            context.DrawLine(pen, new Point(visible.X, y), new Point(visible.Right, y));
        }
    }

    /// <param name="context">Where the canvas is drawing.</param>
    /// <param name="lifted">
    /// The modules being dragged, empty while none are. A set rather than one
    /// id because a drag may carry a whole selection, and a wire between two of
    /// its members is as much in play as one leaving it.
    /// </param>
    /// <param name="theirs">
    /// Which half of the wires this pass draws: those modules' own, or all the
    /// rest. One loop serves both, so a wire cannot be drawn twice or missed
    /// entirely — the two passes partition the same set rather than each
    /// deciding for themselves what belongs in it.
    /// </param>
    private void DrawConnections(DrawingContext context, IReadOnlySet<Guid> lifted, bool theirs)
    {
        foreach (var connection in patch.Connections)
        {
            var mine = lifted.Contains(connection.SourceNode)
                || lifted.Contains(connection.TargetNode);

            if (mine != theirs) continue;

            // A wire with both ends inside one collapsed box is a wire the box
            // is standing in front of. Not drawn faintly or routed around — it
            // is simply not on the canvas while the box is shut.
            if (Hidden(connection)) continue;

            var source = patch.Find(connection.SourceNode);
            var target = patch.Find(connection.TargetNode);
            if (source is null || target is null) continue;

            var sourceDef = NodeCatalog.Get(source.TypeId);
            var targetDef = NodeCatalog.Get(target.TypeId);
            if (sourceDef is null || targetDef is null) continue;
            if (connection.SourcePort >= sourceDef.Outputs.Count) continue;
            if (connection.TargetPort >= targetDef.Inputs.Count) continue;

            var from = OutputAnchor(source, connection.SourcePort);
            var to = InputAnchor(target, targetDef, connection.TargetPort);
            var color = Colors.PortColor(sourceDef.Outputs[connection.SourcePort].Kind);

            // Heavier and at full strength, which is the same signal the pending
            // wire gives: this one is in play.
            var pen = theirs
                ? new Pen(new SolidColorBrush(color), LiftedWireThickness)
                : new Pen(new SolidColorBrush(color, RestingWireOpacity), WireThickness);

            DrawWire(context, from, to, pen);
        }
    }

    /// <summary>
    /// Where the wire being dragged is anchored, and null when none is.
    /// </summary>
    /// <remarks>
    /// What <see cref="DrawPendingWire"/> draws from, rather than a second
    /// reading of the same question: the anchor is a port that may be behind a
    /// box, so it has to come through the anchors like every other wire, and one
    /// of the two going back to <see cref="NodeGeometry"/> directly is exactly
    /// the bug this had. One of them can be tested and the other cannot, so they
    /// are the same one.
    /// </remarks>
    public Point? PendingWireFrom
    {
        get
        {
            if (drag != Drag.Wire) return null;
            if (patch.Find(wireNode) is not { } node) return null;
            if (NodeCatalog.Get(node.TypeId) is not { } def) return null;

            return wireFromOutput ? OutputAnchor(node, wirePort) : InputAnchor(node, def, wirePort);
        }
    }

    private void DrawPendingWire(DrawingContext context)
    {
        if (PendingWireFrom is not { } anchor) return;

        var pen = new Pen(new SolidColorBrush(Colors.Attention, 0.9), 2.2, DashStyle.Dash);

        // The anchor decides where it starts and the pointer where it ends, and
        // the two are handed to DrawWire in whichever order makes the curve leave
        // an output and arrive at an input.
        if (wireFromOutput) DrawWire(context, anchor, wireEnd, pen);
        else DrawWire(context, wireEnd, anchor, pen);
    }

    /// <summary>A horizontal-tangent bezier, so wires leave and enter sockets cleanly.</summary>
    private static void DrawWire(DrawingContext context, Point from, Point to, IPen pen)
    {
        var reach = Math.Max(45, Math.Abs(to.X - from.X) * 0.5);

        var geometry = new StreamGeometry();
        using (var sink = geometry.Open())
        {
            sink.BeginFigure(from, false);
            sink.CubicBezierTo(from.WithX(from.X + reach), to.WithX(to.X - reach), to);
            sink.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private void DrawNode(DrawingContext context, NodeInstance node, NodeDef def)
    {
        var bounds = NodeGeometry.Bounds(node, def);
        var isSelected = selection.Contains(node.Id);
        var accent = Colors.Accent(def.Category);

        context.DrawRectangle(
            isSelected ? NodeFillSelected : NodeFill,
            !isSelected ? NodeBorder : focus == node.Id ? SelectionPen : SelectionPenSecondary,
            new RoundedRect(bounds, NodeGeometry.CornerRadius));

        // Header band, square at the bottom so it reads as a title bar.
        var header = new Rect(bounds.X, bounds.Y, bounds.Width, NodeGeometry.HeaderHeight);
        context.DrawRectangle(
            new SolidColorBrush(accent, 0.85),
            null,
            new RoundedRect(header, NodeGeometry.CornerRadius, NodeGeometry.CornerRadius, 0, 0));

        context.DrawText(
            Text(node.Title(def), 12.5, HeaderTextBrush, bounds.Width - 16, true),
            new Point(bounds.X + 9, bounds.Y + 5));

        for (var i = 0; i < def.Outputs.Count; i++)
        {
            var port = def.Outputs[i];
            var centre = NodeGeometry.OutputPort(node, i);
            var label = Text(port.Name, 11.5, LabelBrush, bounds.Width - 24, true);

            context.DrawText(label, new Point(bounds.Right - 14 - label.Width, centre.Y - label.Height / 2));
            DrawPort(context, centre, port.Kind);
        }

        for (var i = 0; i < def.Inputs.Count; i++)
        {
            var port = def.Inputs[i];
            var centre = NodeGeometry.InputPort(node, def, i);
            var connected = patch.IncomingTo(node.Id, i) is not null;

            var label = Text(port.Name, 11.5, LabelBrush, bounds.Width * 0.55, true);
            context.DrawText(label, new Point(bounds.X + 14, centre.Y - label.Height / 2));

            // An unconnected input shows what it will compile to: the module
            // normalled to it where there is one — no wire is drawn for a wire
            // that is not in the patch — and otherwise the knob value.
            if (!connected && NodeCatalog.Normalled(port) is { } source)
            {
                // Wider than the column a number gets, because this is a module
                // name and a qualified one at that — "Coordinates x" does not
                // fit where "0.25" does, and trimmed to "Coordinates…" it would
                // stop telling x from y.
                var name = Text(source, 11.5, NormalBrush, bounds.Width * 0.5, true);
                context.DrawText(name, new Point(bounds.Right - 12 - name.Width, centre.Y - name.Height / 2));
            }
            else if (!connected && i < node.InputValues.Length)
            {
                var value = Text(port.Format(node.InputValues[i]), 11.5, ValueBrush, bounds.Width * 0.4, true);
                context.DrawText(value, new Point(bounds.Right - 12 - value.Width, centre.Y - value.Height / 2));
            }

            DrawPort(context, centre, port.Kind);
        }
    }
}
