using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
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

            DrawBusLinks(context);

            DrawPendingWire(context);
        }

        // Outside the transform, so a hairline stays a hairline and the dashes
        // keep their spacing however far out the view is zoomed. These are drawn
        // on the canvas rather than in it — neither is part of the patch.
        DrawEdge(context);
        DrawMarquee(context);

        KeepMoving();
    }

    /// <summary>
    /// Asks for another frame where a module drawn this pass was animating, and
    /// otherwise lets the canvas go still.
    /// </summary>
    /// <remarks>
    /// Driven by what was drawn rather than by what is in the patch, so the clock
    /// starts when a moving module appears and stops when the last one is
    /// deleted, switched to a still or scrolled behind a shut group. One pending
    /// tick at a time, since a pass that draws forty animations still wants one
    /// repaint.
    /// </remarks>
    private void KeepMoving()
    {
        if (!moving || ticking) return;

        moving = false;
        ticking = true;

        DispatcherTimer.RunOnce(
            () =>
            {
                ticking = false;
                InvalidateVisual();
            },
            TimeSpan.FromMilliseconds(Tick));
    }

    private bool moving, ticking;

    /// <summary>
    /// How often the canvas repaints while a module on it is animating. Under
    /// the shortest delay a GIF is usually written at, and well over what it
    /// costs to redraw a patch.
    /// </summary>
    private const double Tick = 40;

    /// <summary>
    /// What every animation on the canvas is read against, so two modules showing
    /// the same picture show the same frame of it.
    /// </summary>
    private static readonly Stopwatch clock = Stopwatch.StartNew();

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
                var run = WirePath.ReturnRun(
                    NodeGeometry.Bounds(source, sourceDef),
                    NodeGeometry.Bounds(target, targetDef));

                WirePath.DrawReturn(context, from, to, run, pen);
                continue;
            }

            WirePath.Draw(context, from, to, pen);
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
            WirePath.Draw(context, from, to, pen);
            return;
        }

        // Routed by the same rule as a wire already drawn, so it does not change
        // shape the moment it is dropped. The pointer is a module of no size.
        var held = patch.Find(wireNode) is { } node && NodeCatalog.Get(node.TypeId) is { } def
            ? NodeGeometry.Bounds(node, def)
            : new Rect(anchor, anchor);

        WirePath.DrawReturn(context, from, to, WirePath.ReturnRun(held, new Rect(wireEnd, wireEnd)), pen);
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

    /// <summary>
    /// Sets a plugin's picture behind its module, scaled to cover the body and
    /// clipped to it.
    /// </summary>
    /// <remarks>
    /// Cover rather than stretch, so nothing anybody drew comes out the wrong
    /// shape; what falls outside the block is cut off. Drawing it asks for the
    /// next frame, which is the whole of what makes an animation run — a canvas
    /// with no moving module on it is asked for nothing and repaints when the
    /// patch changes, as it always has.
    /// <para>
    /// Stopped here rather than at the decode, so switching the setting is the
    /// next frame rather than every picture on the canvas being read again.
    /// </para>
    /// </remarks>
    private void DrawArtwork(DrawingContext context, ModuleArtwork picture, RoundedRect body)
    {
        var running = ModuleSkins.Animated && picture.Runs > 0;
        var image = running ? picture.At(clock.Elapsed.TotalMilliseconds) : picture.Frames[0];
        var size = image.Size;

        if (size.Width <= 0 || size.Height <= 0) return;

        if (running) moving = true;

        var bounds = body.Rect;
        var scale = Math.Max(bounds.Width / size.Width, bounds.Height / size.Height);

        using (context.PushClip(body))
        {
            context.DrawImage(
                image,
                new Rect(size),
                bounds.CenterRect(new Rect(0, 0, size.Width * scale, size.Height * scale)));
        }
    }

    /// <summary>
    /// The band of background a line of text takes its color from: the header for
    /// the title, the row for everything else.
    /// </summary>
    private const double TitleInk = 16, RowInk = 14;

    /// <summary>
    /// How far the quieter columns are pulled back into the background — the step
    /// from a name to a number and from a number to a normal, at the spacing
    /// <see cref="CanvasText.LabelBrush"/>, <see cref="CanvasText.ValueBrush"/> and
    /// <see cref="NormalBrush"/> already stand at over the node grey.
    /// </summary>
    private const double ValueFade = 0.35, NormalFade = 0.55;

    private void DrawModule(DrawingContext context, NodeInstance node, NodeDef def)
    {
        var bounds = NodeGeometry.Bounds(node, def);
        var isSelected = selection.Contains(node.Id);
        var (accent, floor) = Colors.Palette(def);
        var backdrop = ModuleBackdrop.Of(def, isSelected);

        // A module given a background of its own was given one the shell cannot
        // judge white against, so where its author asked, every line of text on
        // it is colored from the background that line covers instead.
        var follow = ModuleSkins.Of(def) is { ContrastText: true };

        IBrush Ink(double centre, double height, double fade, IBrush plain) => follow
            ? NodeSkin.Ink(
                backdrop.At(bounds, centre - height / 2),
                backdrop.At(bounds, centre + height / 2),
                backdrop.Lift(bounds, centre),
                fade)
            : plain;

        var body = new RoundedRect(bounds, NodeGeometry.CornerRadius);
        var border = !isSelected ? NodeSkin.Edge : focus == node.Id ? SelectionPen : SelectionPenSecondary;

        if (backdrop.Picture is { } picture)
        {
            DrawArtwork(context, picture, body);
            context.DrawRectangle(null, border, body);
        }
        else
        {
            context.DrawRectangle(NodeSkin.Body(accent, floor, isSelected), border, body);

            // Over the wash and under the mark, because it is the surface the
            // mark is set into rather than a second thing on the body.
            if (ModuleSkins.Of(def) is ModuleSkin.Grain { Cut: var cut })
                using (context.PushClip(body))
                    context.FillRectangle(
                        NodeSkin.Cut(cut, NodeSkin.BodyTop(accent, isSelected)),
                        bounds);

            NodeSkin.DrawMark(context, body, ModuleGlyphs.For(def), NodeSkin.Mark(accent));
        }

        // Header band, square at the bottom so it reads as a title bar. A picture
        // runs under it instead: it is the background, and the band is the one
        // part of the block that is not.
        var header = new Rect(bounds.X, bounds.Y, bounds.Width, NodeGeometry.HeaderHeight);

        if (backdrop.Picture is null)
            context.DrawRectangle(
                NodeSkin.Header(accent, floor, isSelected),
                null,
                new RoundedRect(header, NodeGeometry.CornerRadius, NodeGeometry.CornerRadius, 0, 0));

        NodeSkin.Relief(context, header);

        var titleAt = new Point(bounds.X + 9, bounds.Y + 5);

        var titleBrush = Ink(titleAt.Y + TitleInk / 2, TitleInk, fade: 0, HeaderTextBrush);

        var title = CanvasText.Text(Heading(node, def), HeaderSize, titleBrush, HeaderWidth(bounds, def), true);

        context.DrawText(title, titleAt);

        if (node.Off)
            context.DrawLine(
                follow ? new Pen(titleBrush, 1.5) : OffStrike,
                new Point(titleAt.X, titleAt.Y + title.Height / 2),
                new Point(titleAt.X + title.Width, titleAt.Y + title.Height / 2));

        if (Tagged(def)) DrawTag(context, bounds);

        var formula = NodeCatalog.FormulaOf(node);

        for (var i = 0; i < def.Outputs.Count; i++)
        {
            var port = def.Outputs[i];
            var centre = NodeGeometry.OutputPort(node, i);
            var label = CanvasText.Text(port.Name, 11.5, Ink(centre.Y, RowInk, 0, CanvasText.LabelBrush), bounds.Width - 24, true);

            context.DrawText(label, new Point(bounds.Right - 14 - label.Width, centre.Y - label.Height / 2));
            NodeSkin.DrawPort(context, centre, port.Kind);
        }

        for (var i = 0; i < def.Inputs.Count; i++)
        {
            var port = def.Inputs[i];
            var centre = NodeGeometry.InputPort(node, def, i);
            var connected = patch.IncomingTo(node.Id, i) is not null;

            var linked = DrawLinkedRow(context, node, port, i, bounds, centre, connected);

            var label = CanvasText.Text(port.Name, 11.5, Ink(centre.Y, RowInk, 0, CanvasText.LabelBrush), bounds.Width * 0.55, true);
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
                var name = CanvasText.Text(source, 11.5, Ink(centre.Y, RowInk, NormalFade, NormalBrush), bounds.Width * 0.5, true);
                context.DrawText(name, new Point(bounds.Right - 12 - name.Width, centre.Y - name.Height / 2));
            }
            else if (!linked && !connected && i < node.InputValues.Length && (formula is null || FormulaLayout.Reads(formula, i)))
            {
                // A socket its formula never reads has a knob that turns nothing,
                // so an Expression shows the values of the ones it does and no more.
                var value = CanvasText.Text(
                    port.Format(node.InputValues[i]),
                    11.5,
                    Ink(centre.Y, RowInk, ValueFade, CanvasText.ValueBrush),
                    bounds.Width * 0.4,
                    true);
                context.DrawText(value, new Point(bounds.Right - 12 - value.Width, centre.Y - value.Height / 2));
            }

            NodeSkin.DrawPort(context, centre, port.Kind);
        }

        if (FormulaLayout.FormulaBlock(patch, node, def, bounds) is var (text, at, _, _)) context.DrawText(text, at);
    }
}
