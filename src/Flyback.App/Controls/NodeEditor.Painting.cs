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
    /// The rectangle between two corners, whichever way round they are. Built by hand
    /// rather than from <c>new Rect(a, b)</c>, which takes the first point as the top
    /// left: started from any other corner that gives a negative width, and a
    /// rectangle like that draws nothing and intersects nothing.
    /// </summary>
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
    /// The modules being dragged, empty while none are. A set rather than one id
    /// because a drag may carry a whole selection.
    /// </param>
    /// <param name="theirs">
    /// Which half of the wires this pass draws: those modules' own, or all the rest.
    /// One loop serves both, so the two passes partition the same set rather than each
    /// deciding what belongs in it.
    /// </param>
    private void DrawConnections(DrawingContext context, IReadOnlySet<Guid> lifted, bool theirs)
    {
        // What a loop is made of, and the one thing about a wire the canvas
        // cannot read off its two ends — see Cycles.Backwards, which the compiler
        // asks the same question of.
        var backwards = Cycles.Backwards(patch);

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

            // A wire onto or off a module that is switched off is drawn as faintly
            // as the module is: what it shows is where the patch runs again once
            // that module comes back.
            var strength = source.Off || target.Off ? OffOpacity : 1;

            // Dashed where the wire runs backwards, which is the whole of how a
            // loop shows itself: what this one carries is the evaluation before,
            // and a solid wire would say it carried this one.
            var dashes = backwards.Contains(connection) ? DashStyle.Dash : null;

            // Heavier and at full strength, which is the same signal the pending
            // wire gives: this one is in play.
            var pen = theirs
                ? new Pen(new SolidColorBrush(color, strength), LiftedWireThickness, dashes)
                : new Pen(new SolidColorBrush(color, RestingWireOpacity * strength), WireThickness, dashes);

            // How a wire is routed is a question of where its ends are, not of
            // what it carries: one that has to travel leftwards goes round, and a
            // loop whose modules are laid out left to right is drawn like any
            // other chain. The dashes are what say which wire is the cut.
            if (from.X > to.X)
            {
                var run = ReturnRun(
                    NodeGeometry.Bounds(source, sourceDef),
                    NodeGeometry.Bounds(target, targetDef));

                DrawReturnWire(context, from, to, run, pen);
                continue;
            }

            DrawWire(context, from, to, pen);
        }
    }

    /// <summary>
    /// Where the wire being dragged is anchored, and null when none is.
    /// </summary>
    /// <remarks>
    /// What <see cref="DrawPendingWire"/> draws from rather than a second reading of
    /// the same question: the anchor is a port that may be behind a box, so it has to
    /// come through the anchors like every other wire — one of the two going back to
    /// <see cref="NodeGeometry"/> directly is exactly the bug this had.
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
        // the two are handed over in whichever order makes the curve leave an
        // output and arrive at an input.
        var (from, to) = wireFromOutput ? (anchor, wireEnd) : (wireEnd, anchor);

        if (from.X <= to.X)
        {
            DrawWire(context, from, to, pen);
            return;
        }

        // Routed by the same rule as a wire already drawn, so it does not change
        // shape the moment it is dropped. The pointer is a module of no size.
        var held = patch.Find(wireNode) is { } node && NodeCatalog.Get(node.TypeId) is { } def
            ? NodeGeometry.Bounds(node, def)
            : new Rect(anchor, anchor);

        DrawReturnWire(context, from, to, ReturnRun(held, new Rect(wireEnd, wireEnd)), pen);
    }

    /// <summary>
    /// The height a wire travelling leftwards runs back at, between the two
    /// modules it joins.
    /// </summary>
    /// <remarks>
    /// Through the gap between them where one sits clear above the other, which
    /// is the short way round and crosses neither. Where they overlap on the
    /// vertical — side by side, or the same module twice — there is no gap to
    /// use, and it passes under both instead.
    /// </remarks>
    private static double ReturnRun(Rect source, Rect target)
    {
        if (target.Top - source.Bottom >= ReturnWireDrop) return (source.Bottom + target.Top) / 2;
        if (source.Top - target.Bottom >= ReturnWireDrop) return (target.Bottom + source.Top) / 2;

        return Math.Max(source.Bottom, target.Bottom) + ReturnWireDrop;
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

    /// <summary>
    /// A wire whose input is left of its output: out to the right of the socket it
    /// leaves, round in a U-bend to <paramref name="run"/>, back along it, and
    /// round again into the socket it arrives at from the left. Every piece meets
    /// the next on a shared tangent, so there is no corner anywhere on it — the
    /// same soft line as <see cref="DrawWire"/>, bent twice.
    /// </summary>
    /// <remarks>
    /// <see cref="DrawWire"/>'s single bezier is wrong for these. It spreads its
    /// control points by half the span, which turns a long leftward wire into a
    /// diagonal across the patch and a module wired to itself into an ellipse
    /// wider than the module. Here each bend is sized by how far it has to turn
    /// rather than by how far the wire travels, so the wire reads the same whether
    /// it goes round one module or across the canvas.
    /// </remarks>
    /// <param name="run">The height the flat run back sits at — see <see cref="ReturnRun"/>.</param>
    private static void DrawReturnWire(DrawingContext context, Point from, Point to, double run, IPen pen)
    {
        var geometry = new StreamGeometry();
        using (var sink = geometry.Open())
        {
            sink.BeginFigure(from, false);

            // Out of the output heading right, and round until it is heading left
            // along the run, directly beneath or above where it started.
            var leaving = Bend(from.Y, run);

            sink.CubicBezierTo(
                new Point(from.X + leaving, from.Y),
                new Point(from.X + leaving, run),
                new Point(from.X, run));

            sink.LineTo(new Point(to.X, run));

            // And round the other way, into the input heading right.
            var arriving = Bend(run, to.Y);

            sink.CubicBezierTo(
                new Point(to.X - arriving, run),
                new Point(to.X - arriving, to.Y),
                to);

            sink.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    /// <summary>
    /// How far a U-bend's handles reach out sideways, for a bend that turns across
    /// the given heights.
    /// </summary>
    /// <remarks>
    /// Three quarters of the height is what makes a cubic with both handles level
    /// with its ends close to a half circle — it bulges out by about half the
    /// height. Bounded both ways: a short turn still wants a curve rather than a
    /// kink, and a long one through a wide gap would otherwise swing further out
    /// than any other wire on the canvas.
    /// </remarks>
    private static double Bend(double from, double to) =>
        Math.Clamp(Math.Abs(to - from) * 0.75, ReturnWireReach / 3, ReturnWireReach);

    /// <summary>
    /// Sets a module's mark in the body, right of the labels and under the
    /// header, which is drawn after it.
    /// </summary>
    /// <remarks>
    /// Sized to the body rather than fixed, so a module of one row gets a small
    /// whole mark instead of the bottom third of a large one, and capped so a
    /// tall module's does not become the module. Drawn under the text on purpose
    /// and held faint enough that nothing has to be read past it.
    /// </remarks>
    private static void DrawMark(DrawingContext context, RoundedRect body, Geometry? glyph, IPen pen)
    {
        if (glyph is null) return;

        var bounds = body.Rect;
        var room = bounds.Height - NodeGeometry.HeaderHeight;

        // Too little room for a mark to be anything but a smudge.
        if (room - MarkInset * 2 < MarkLeast) return;

        var size = Math.Min(room - MarkInset * 2, MarkMost);
        var scale = size / ModuleGlyphs.Box;

        var at = new Point(
            bounds.Right - MarkInset - size,
            bounds.Y + NodeGeometry.HeaderHeight + (room - size) / 2);

        using (context.PushTransform(
            Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(at.X, at.Y)))
        {
            context.DrawGeometry(null, pen, glyph);
        }
    }

    /// <summary>How far a mark keeps off the sides of the body it is set in.</summary>
    private const double MarkInset = 4;

    /// <summary>The sizes a mark is held between — see <see cref="DrawMark"/>.</summary>
    private const double MarkLeast = 18, MarkMost = 52;

    /// <summary>
    /// The two lines that give a header band a face: light along its top edge,
    /// and a seam where it meets the body.
    /// </summary>
    /// <remarks>
    /// The light is held off the corners, where a straight line across a rounded
    /// one reads as an overhang rather than as an edge catching the light.
    /// </remarks>
    private static void DrawHeaderRelief(DrawingContext context, Rect header)
    {
        var inset = NodeGeometry.CornerRadius;

        context.DrawLine(
            HeaderGloss,
            new Point(header.X + inset, header.Y + 0.75),
            new Point(header.Right - inset, header.Y + 0.75));

        context.DrawLine(
            HeaderSeam,
            new Point(header.X, header.Bottom - 0.5),
            new Point(header.Right, header.Bottom - 0.5));
    }

    /// <summary>
    /// Draws a module, faintly where it is switched off.
    /// </summary>
    /// <remarks>
    /// Faint rather than differently colored: what the module is has not changed,
    /// and its category's accent is how the canvas is read at a glance. The strike
    /// through the name is what says it outright, since a patch drawn small is
    /// faint everywhere.
    /// </remarks>
    private void DrawNode(DrawingContext context, NodeInstance node, NodeDef def)
    {
        if (!node.Off)
        {
            DrawModule(context, node, def);
            return;
        }

        using (context.PushOpacity(OffOpacity)) DrawModule(context, node, def);
    }

    private void DrawModule(DrawingContext context, NodeInstance node, NodeDef def)
    {
        var bounds = NodeGeometry.Bounds(node, def);
        var isSelected = selection.Contains(node.Id);

        var body = new RoundedRect(bounds, NodeGeometry.CornerRadius);

        context.DrawRectangle(
            NodeSkin.Body(def.Category, isSelected),
            !isSelected ? NodeBorder : focus == node.Id ? SelectionPen : SelectionPenSecondary,
            body);

        DrawMark(context, body, ModuleGlyphs.For(def), NodeSkin.Mark(def.Category));

        // Header band, square at the bottom so it reads as a title bar.
        var header = new Rect(bounds.X, bounds.Y, bounds.Width, NodeGeometry.HeaderHeight);
        context.DrawRectangle(
            NodeSkin.Header(def.Category),
            null,
            new RoundedRect(header, NodeGeometry.CornerRadius, NodeGeometry.CornerRadius, 0, 0));

        DrawHeaderRelief(context, header);

        var title = Text(node.Title(def), HeaderSize, HeaderTextBrush, HeaderWidth(bounds, def), true);
        var titleAt = new Point(bounds.X + 9, bounds.Y + 5);

        context.DrawText(title, titleAt);

        if (node.Off)
            context.DrawLine(
                OffStrike,
                new Point(titleAt.X, titleAt.Y + title.Height / 2),
                new Point(titleAt.X + title.Width, titleAt.Y + title.Height / 2));

        if (Tagged(def)) DrawTag(context, bounds);

        var formula = NodeCatalog.FormulaOf(node);

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

            var linked = DrawLinkedRow(context, node, port, i, bounds, centre, connected);

            var label = Text(port.Name, 11.5, LabelBrush, bounds.Width * 0.55, true);
            context.DrawText(label, new Point(bounds.X + 14, centre.Y - label.Height / 2));

            // An unconnected input shows what it will compile to: the module
            // normalled to it where there is one — no wire is drawn for a wire
            // that is not in the patch — and otherwise the knob value.
            if (!linked && !connected && NodeCatalog.Normalled(port) is { } source)
            {
                // Wider than the column a number gets, because this is a module
                // name and a qualified one at that — "Coordinates x" does not
                // fit where "0.25" does, and trimmed to "Coordinates…" it would
                // stop telling x from y.
                var name = Text(source, 11.5, NormalBrush, bounds.Width * 0.5, true);
                context.DrawText(name, new Point(bounds.Right - 12 - name.Width, centre.Y - name.Height / 2));
            }
            else if (!linked && !connected && i < node.InputValues.Length && (formula is null || Reads(formula, i)))
            {
                // A socket its formula never reads has a knob that turns nothing,
                // so an Expression shows the values of the ones it does and no more.
                var value = Text(port.Format(node.InputValues[i]), 11.5, ValueBrush, bounds.Width * 0.4, true);
                context.DrawText(value, new Point(bounds.Right - 12 - value.Width, centre.Y - value.Height / 2));
            }

            DrawPort(context, centre, port.Kind);
        }

        if (FormulaBlock(node, def, bounds) is var (text, at, _, _)) context.DrawText(text, at);
    }
}
