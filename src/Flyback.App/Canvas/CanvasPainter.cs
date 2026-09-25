using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App.Canvas;

/// <summary>
/// Everything the canvas draws: the ground and its grid, the wires, the modules, the
/// boxes and open groups, and what the gestures under way add over them.
/// </summary>
/// <remarks>
/// Drawn inside the view's one matrix, and every socket placed by the same
/// <see cref="NodeGeometry"/> the hit testing measures with, which is what stops a
/// wire's end and the dot it is drawn on from ever disagreeing (ADR-0017).
/// </remarks>
internal sealed class CanvasPainter(
    CanvasHistory history,
    CanvasSelection selection,
    Viewport view,
    CanvasGestures gestures,
    SocketDial dial,
    RemapMarks marks,
    KnobLinking linking,
    UndescribedTags tags,
    Repaint repaint,
    NodeGeometry geometry)
{
    /// <summary>How large a module's title is drawn.</summary>
    internal const double HeaderSize = 12.5;

    /// <summary>How wide a module's title may be drawn, leaving room for its tag.</summary>
    internal static double HeaderWidth(Rect bounds, bool tagged) => bounds.Width - 16 - (tagged ? UndescribedTags.TagRoom : 0);

    private Patch Patch => history.Patch;

    public void Render(DrawingContext context)
    {
        // Two grounds: the canvas is a sheet of finite size, and past it is whatever
        // the bounded pan lets you see a little of, which is what makes the edge an
        // edge rather than a stop to walk into.
        context.FillRectangle(Beyond, new Rect(view.Size));
        context.FillRectangle(Background, view.OnScreen(Viewport.CanvasBounds));

        using (context.PushTransform(view.GraphToScreen))
        {
            DrawGrid(context);

            // The wires of modules being dragged are drawn after the modules, so
            // nothing the block is pulled across hides where it is still patched.
            var lifted = gestures.Carrying ? selection.Ids : new HashSet<Guid>();
            var scene = selection.Scene;

            // Under the modules and the wires both: the ground a group stands on.
            DrawOpenGroups(context, scene);

            DrawConnections(context, lifted, theirs: false, peeked: false);

            if (!gestures.Gesturing) marks.Draw(context);

            foreach (var node in Patch.Nodes)
                if (!scene.Shut(node.Id) && !scene.InPeek(node.Id) && NodeCatalog.Get(node.TypeId) is { } def)
                    DrawNode(context, node, def);

            foreach (var (group, sockets, bounds) in scene.Boxes())
                DrawBox(context, group, sockets, bounds);

            DrawConnections(context, lifted, theirs: true, peeked: false);

            DrawPeek(context, scene, lifted);

            DrawBusLinks(context);

            gestures.DrawPendingWire(context);

            dial.Draw(context, scene);
        }

        // Outside the transform, so a hairline stays a hairline and the dashes keep
        // their spacing at any zoom. Neither is part of the patch.
        DrawEdge(context);
        gestures.DrawMarquee(context);

        KeepMoving();
    }

    private static readonly IBrush Background = new SolidColorBrush(Colors.Canvas);
    private static readonly IBrush NormalBrush = new SolidColorBrush(Colors.Normalled);
    private static readonly IBrush HeaderTextBrush = Brushes.White;
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Colors.Grid));
    private static readonly IPen GridPenMajor = new Pen(new SolidColorBrush(Colors.GridMajor));
    private static readonly IPen SelectionPen = new Pen(new SolidColorBrush(Colors.Attention), 2);

    /// <summary>
    /// Selected, but not the one the inspector is showing. The same color at
    /// half strength rather than a second color: what these modules are is
    /// selected, and the difference between them and the bright one is which of
    /// them the panel on the right is currently about.
    /// </summary>
    private static readonly IPen SelectionPenSecondary =
        new Pen(new SolidColorBrush(Colors.Attention, 0.5), 2);

    /// <summary>
    /// How strongly a module that is switched off is drawn, and its wires with
    /// it. Faint enough to read as out of the patch at any zoom, and not so faint
    /// that what it is wired to cannot be followed.
    /// </summary>
    private const double OffOpacity = 0.38;

    /// <summary>The line through the name of a module that is switched off.</summary>
    private static readonly IPen OffStrike = new Pen(HeaderTextBrush, 1.5);

    /// <summary>
    /// The dashed ring round a group that is open — see OpenGroup.
    /// </summary>
    /// <remarks>
    /// In the separator color at half strength rather than the outline one: a
    /// box's border is drawn on a module, where a gray darker than the canvas
    /// reads as an edge, and this is drawn on the canvas itself. Half strength and
    /// dashed because an open group is furniture marking a region, and furniture
    /// that shouts is furniture in the way.
    /// </remarks>
    private static readonly IPen OpenGroupPen = new Pen(
        new SolidColorBrush(Colors.Separator, 0.5),
        1.5,
        new DashStyle([6, 4], 0));

    /// <summary>
    /// The same ring while everything inside it is selected.
    /// </summary>
    /// <remarks>
    /// A shut box turns its border the selected color, so without this the one
    /// picture saying which modules a gesture is about goes missing exactly when
    /// the group is opened to work on. Held well under
    /// <see cref="SelectionPen"/>: the modules inside are already ringed one by
    /// one, and this is the line round the lot of them.
    /// </remarks>
    private static readonly IPen OpenGroupPenSelected = new Pen(
        new SolidColorBrush(Colors.Attention, 0.55),
        1.5,
        new DashStyle([6, 4], 0));

    /// <summary>
    /// The ground inside the ring. Faint to the edge of being nothing, on
    /// purpose: it is drawn under the wires and under the modules both, so it
    /// has to say "this area" at a glance without becoming a second background
    /// for everything standing on it.
    /// </summary>
    /// <remarks>
    /// Brightest where it meets the strip the title is on and falling away to
    /// nothing at the floor, so the region reads as light coming off the strip
    /// rather than as a panel laid over the canvas.
    /// </remarks>
    private static readonly IBrush OpenGroupFill = NodeSkin.Down(
        Colors.Faded(Colors.Separator, 0.12), Colors.Faded(Colors.Separator, 0.025));

    /// <summary>
    /// The tab the title sits on, above the ring.
    /// </summary>
    /// <remarks>
    /// The strip used to be bare, which left the name adrift over the canvas
    /// with nothing joining it to the region it names. A tab is what a named
    /// region wears everywhere; it is still not a header, because it is only as
    /// wide as the name.
    /// </remarks>
    private static readonly IBrush OpenGroupTab = new SolidColorBrush(Colors.Separator, 0.22);

    private static readonly IBrush OpenGroupTabSelected = new SolidColorBrush(Colors.Attention, 0.22);

    /// <summary>The ring round a box being looked into: solid, because it is over everything.</summary>
    private static readonly IPen PeekPen = new Pen(new SolidColorBrush(Colors.Separator), 1.5);

    /// <summary>The ground inside a box being looked into: slightly transparent, so the canvas under it still shows.</summary>
    private static readonly IBrush PeekGround = new SolidColorBrush(Colors.Canvas, 0.70);

    /// <summary>What the canvas outside a box being looked into is dimmed under.</summary>
    private static readonly IBrush PeekScrim = new SolidColorBrush(Colors.Edge, 0.78);

    /// <summary>
    /// How strongly a wire leaving a box being looked into is drawn at its far end,
    /// against full strength where it leaves the box. It reaches nothing, so no edge
    /// is left against the socket it runs into.
    /// </summary>
    private const double PeekWireFar = 0, PeekWireNear = 0.7;

    /// <summary>How much wider than its title a tab is drawn, and how far in the title sits.</summary>
    private const double TabPadding = 9;

    /// <summary>
    /// How round a region's corners are. Softer than a module's, because it is a
    /// region and not a thing.
    /// </summary>
    private const double GroupCornerRadius = 10;

    private static readonly IBrush Beyond = new SolidColorBrush(Colors.Edge);

    /// <summary>
    /// The edge of the canvas. Brighter than any grid line and a little heavier,
    /// because it is the one line out there that means something other than
    /// "this is where another forty-eight units went".
    /// </summary>
    private static readonly IPen EdgePen = new Pen(new SolidColorBrush(Colors.Separator), 2);

    /// <summary>How thick a wire is drawn, and how far under full strength.</summary>
    private const double WireThickness = 2.2;

    private const double RestingWireOpacity = 0.85;

    /// <summary>
    /// A wire on the module being dragged. Heavier rather than differently
    /// colored, because a wire's color already says what flows down it and
    /// that is not what has changed about this one.
    /// </summary>
    private const double LiftedWireThickness = 3.4;


    private static readonly IBrush LinkedBrush = new SolidColorBrush(Colors.Attention, 0.85);
    private static readonly IBrush LinkableWash = new SolidColorBrush(Colors.Attention, 0.07);
    private static readonly IBrush LinkedWash = new SolidColorBrush(Colors.Attention, 0.24);


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
                repaint.Request();
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
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

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
        context.DrawRectangle(null, EdgePen, view.OnScreen(Viewport.CanvasBounds));


    private void DrawGrid(DrawingContext context)
    {
        // What is being looked at, and never more of it than there is canvas.
        // The grid is what says where a module would land, so ruling ground no
        // module may stand on would be a lie told in the one part of the view
        // that has nothing else in it to read.
        var visible = new Rect(
                view.ToGraph(new Point(0, 0)),
                view.ToGraph(new Point(view.Size.Width, view.Size.Height)))
            .Intersect(Viewport.CanvasBounds);

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
    /// <param name="peeked">
    /// Whether this pass draws the wires of the box being looked into, which are
    /// drawn over everything, or the rest.
    /// </param>
    private void DrawConnections(DrawingContext context, IReadOnlySet<Guid> lifted, bool theirs, bool peeked)
    {
        // What a loop is made of, and the one thing about a wire the canvas
        // cannot read off its two ends — see Cycles.Backwards, which the compiler
        // asks the same question of.
        var backwards = Cycles.BackwardsThroughBuses(Patch);
        var scene = selection.Scene;

        foreach (var connection in Patch.Connections)
        {
            var mine = lifted.Contains(connection.SourceNode)
                || lifted.Contains(connection.TargetNode);

            if (mine != theirs) continue;
            if ((scene.InPeek(connection.SourceNode) || scene.InPeek(connection.TargetNode)) != peeked) continue;

            // A wire with both ends inside one collapsed box is a wire the box
            // is standing in front of. Not drawn faintly or routed around — it
            // is simply not on the canvas while the box is shut.
            if (scene.Hidden(connection)) continue;

            var source = Patch.Find(connection.SourceNode);
            var target = Patch.Find(connection.TargetNode);
            if (source is null || target is null) continue;

            var sourceDef = NodeCatalog.Get(source.TypeId);
            var targetDef = NodeCatalog.Get(target.TypeId);
            if (sourceDef is null || targetDef is null) continue;
            if (connection.SourcePort >= sourceDef.Outputs.Count) continue;
            if (connection.TargetPort >= targetDef.Inputs.Count) continue;

            var from = scene.OutputAnchor(source, connection.SourcePort);
            var to = scene.InputAnchor(target, targetDef, connection.TargetPort);
            // A wire swinging past the range its socket takes is drawn in the
            // accent, which is where the status bar's warning about it points.
            var color = AutoRemap.Overflow(Patch, connection) is null
                ? Colors.PortColor(sourceDef.Outputs[connection.SourcePort].Kind)
                : Colors.Attention;

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
            var opacity = theirs ? strength : RestingWireOpacity * strength;

            // A wire leaving a box being looked into fades toward the dimmed canvas
            // it runs off into, measured end to end rather than along the curve.
            var leaving = peeked && scene.InPeek(connection.SourceNode) != scene.InPeek(connection.TargetNode);

            IBrush ink = leaving && from != to
                ? Fading(color, opacity, scene.InPeek(connection.SourceNode) ? (from, to) : (to, from))
                : new SolidColorBrush(color, opacity);

            var pen = new Pen(ink, theirs ? LiftedWireThickness : WireThickness, dashes);

            // How a wire is routed is a question of where its ends are, not of
            // what it carries: one that has to travel leftwards goes round, and a
            // loop whose modules are laid out left to right is drawn like any
            // other chain. The dashes are what say which wire is the cut.
            if (from.X > to.X)
            {
                var run = WirePath.ReturnRun(
                    geometry.Bounds(source, sourceDef),
                    geometry.Bounds(target, targetDef));

                WirePath.DrawReturn(context, from, to, run, pen);
                continue;
            }

            WirePath.Draw(context, from, to, pen);
        }
    }

    /// <summary>A wire's ink, strongest at <paramref name="ends"/>'s first point and faint at its second.</summary>
    private static LinearGradientBrush Fading(Color color, double opacity, (Point Near, Point Far) ends) => new()
    {
        StartPoint = new RelativePoint(ends.Near, RelativeUnit.Absolute),
        EndPoint = new RelativePoint(ends.Far, RelativeUnit.Absolute),
        GradientStops =
        [
            new GradientStop(Colors.Faded(color, opacity * PeekWireNear), 0),
            new GradientStop(Colors.Faded(color, opacity * PeekWireFar), 1),
        ],
    };


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
        if (picture.Paint(context, body, Clock.Elapsed.TotalMilliseconds)) moving = true;
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
    /// <see cref="NormalBrush"/> already stand at over the node gray.
    /// </summary>
    private const double ValueFade = 0.35, NormalFade = 0.55;

    private void DrawModule(DrawingContext context, NodeInstance node, NodeDef def)
    {
        var bounds = geometry.Bounds(node, def);
        var isSelected = selection.Contains(node.Id);
        var (accent, floor) = Colors.Palette(def);
        var backdrop = ModuleBackdrop.Of(def, isSelected);

        // A module given a background of its own was given one the shell cannot
        // judge white against, so where its author asked, every line of text on
        // it is colored from the background that line covers instead.
        var follow = ModuleSkins.Of(def) is { ContrastText: true };

        IBrush Ink(double center, double height, double fade, IBrush plain) => follow
            ? NodeSkin.Ink(
                backdrop.At(bounds, center - height / 2),
                backdrop.At(bounds, center + height / 2),
                backdrop.Lift(bounds, center),
                fade)
            : plain;

        var body = new RoundedRect(bounds, NodeGeometry.CornerRadius);
        var border = !isSelected ? NodeSkin.Edge : selection.Focus == node.Id ? SelectionPen : SelectionPenSecondary;

        if (backdrop.Picture is { } picture)
        {
            // A picture rarely covers the body edge to edge, and ADR-0118 counts
            // what it leaves transparent as the node gray rather than the canvas
            // behind it.
            context.DrawRectangle(NodeSkin.GroundFill(isSelected), null, body);
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

        var title = CanvasText.Text(Heading(node, def), HeaderSize, titleBrush, HeaderWidth(bounds, tags.Tagged(def)), true);

        context.DrawText(title, titleAt);

        if (node.Off)
            context.DrawLine(
                follow ? new Pen(titleBrush, 1.5) : OffStrike,
                new Point(titleAt.X, titleAt.Y + title.Height / 2),
                new Point(titleAt.X + title.Width, titleAt.Y + title.Height / 2));

        if (tags.Tagged(def)) UndescribedTags.Draw(context, bounds, follow ? titleBrush : null);

        var formula = NodeCatalog.FormulaOf(node);
        var spans = def.TypeId == NodeCatalog.AutoRemapTypeId ? AutoRemap.Of(Patch, node) : null;

        for (var i = 0; i < def.Outputs.Count; i++)
        {
            var port = def.Outputs[i];
            var center = NodeGeometry.OutputPort(node, i);
            var room = geometry.Compact ? HalfRow(bounds) : bounds.Width - 24;
            var label = CanvasText.Text(port.Name, CanvasText.RowSize, Ink(center.Y, RowInk, 0, CanvasText.LabelBrush), room, true);

            context.DrawText(label, new Point(bounds.Right - 14 - label.Width, center.Y - label.Height / 2));
            NodeSkin.DrawPort(context, center, port.Kind);
        }

        for (var i = 0; i < def.Inputs.Count; i++)
        {
            var port = def.Inputs[i];
            var center = geometry.InputPort(node, def, i);
            var connected = Patch.IncomingTo(node.Id, i) is not null;

            if (geometry.Compact)
            {
                DrawCompactInput(context, node, port, i, bounds, center, connected, follow, spans, Ink);
                continue;
            }

            var linked = DrawLinkedRow(context, node, port, i, bounds, center, connected, follow, Ink);

            var label = CanvasText.Text(port.Name, CanvasText.RowSize, Ink(center.Y, RowInk, 0, CanvasText.LabelBrush), bounds.Width * 0.55, true);
            context.DrawText(label, new Point(bounds.X + 14, center.Y - label.Height / 2));

            if (!linked && !connected) DrawResting(context, node, port, i, bounds, center, formula, spans, Ink);

            NodeSkin.DrawPort(context, center, port.Kind);
        }

        if (FormulaLayout.FormulaBlock(Patch, node, def, bounds, geometry.Compact, y => Ink(y, RowInk, ValueFade, CanvasText.ValueBrush)) is var (text, at, _, _))
            context.DrawText(text, at);
    }

    /// <summary>
    /// What an unwired, unlinked input compiles to, at the right of its row: the
    /// module normalled to it where there is one, and otherwise its value. Whether
    /// anything was drawn.
    /// </summary>
    /// <remarks>
    /// No wire is drawn for a normal, since it is not in the patch. A socket its
    /// formula never reads has a knob that turns nothing, so an Expression shows
    /// the values of the ones it does and no more.
    /// </remarks>
    private static bool DrawResting(
        DrawingContext context, NodeInstance node, PortSpec port, int i, Rect bounds, Point center,
        string? formula, RemapSpans? spans, Func<double, double, double, IBrush, IBrush> ink)
    {
        if (NodeCatalog.Normalled(port) is { } source)
        {
            // Wider than the column a number gets: "Coordinates x" does not fit
            // where "0.25" does, and trimmed it would stop telling x from y.
            var name = CanvasText.Text(source, CanvasText.RowSize, ink(center.Y, RowInk, NormalFade, NormalBrush), bounds.Width * 0.5, true);
            context.DrawText(name, new Point(bounds.Right - 12 - name.Width, center.Y - name.Height / 2));
            return true;
        }

        if (i >= node.InputValues.Length || (formula is not null && !FormulaLayout.Reads(formula, i))) return false;

        var (said, flagged) = SocketTips.RemapValue(spans, i, node.InputValues[i]) ?? (port.Format(node.InputValues[i]), false);
        var value = CanvasText.Text(
            said,
            CanvasText.RowSize,
            flagged ? FlagBrush : ink(center.Y, RowInk, ValueFade, CanvasText.ValueBrush),
            bounds.Width * 0.4,
            true);
        var spot = new Point(bounds.Right - 12 - value.Width, center.Y - value.Height / 2);

        context.DrawText(value, spot);

        if (flagged)
            context.DrawRectangle(null, FlagPen, new Rect(spot.X - 3, spot.Y - 1, value.Width + 6, value.Height + 2), 3, 3);

        return true;
    }

    private static readonly IBrush FlagBrush = new SolidColorBrush(Colors.Attention);
    private static readonly Pen FlagPen = new(FlagBrush, 1);

    /// <summary>
    /// The ring, the ground inside it and the title above it, for every group that
    /// is open.
    /// </summary>
    /// <remarks>
    /// A wash as well as a line, because a line alone out here is nearly nothing —
    /// this is drawn under the wires and the modules. Both are held faint: a region
    /// that draws the eye harder than the modules standing in it is a region in the
    /// way of the work. The strip is left bare, because filling it makes a header,
    /// and a header is what a group wears when it is shut.
    /// </remarks>
    private void DrawOpenGroups(DrawingContext context, CanvasScene scene)
    {
        if (Patch.Groups is null) return;

        foreach (var group in Patch.Groups)
            if (group != scene.Peek && scene.OpenGroup(group) is var (outline, handle))
                DrawRing(context, group, outline, handle, lifted: false);
    }

    /// <summary>
    /// The box being looked into, and everything of it, over a canvas dimmed under it.
    /// </summary>
    /// <remarks>
    /// The ring is solid and its ground nearly opaque, so the canvas under it only
    /// just shows through. Its wires are drawn at full strength wherever they run, since what feeds the
    /// box and what it feeds are half of why it is being looked into.
    /// </remarks>
    private void DrawPeek(DrawingContext context, CanvasScene scene, IReadOnlySet<Guid> lifted)
    {
        if (scene.Peek is not { } group || scene.OpenGroup(group) is not var (outline, handle)) return;

        context.FillRectangle(PeekScrim, new Rect(view.ToGraph(default), view.ToGraph(new Point(view.Size.Width, view.Size.Height))));

        DrawRing(context, group, outline, handle, lifted: true);

        DrawConnections(context, lifted, theirs: false, peeked: true);

        foreach (var node in Patch.Nodes)
            if (scene.InPeek(node.Id) && NodeCatalog.Get(node.TypeId) is { } def)
                DrawNode(context, node, def);

        DrawConnections(context, lifted, theirs: true, peeked: true);
    }

    private void DrawRing(DrawingContext context, NodeGroup group, Rect outline, Rect handle, bool lifted)
    {
        // Selected when its modules are, which is the rule a shut box uses —
        // and it is the same gesture that selects them, since pressing the
        // strip takes the group.
        var isSelected = group.Members.Count > 0 && group.Members.All(selection.Contains);

        var label = CanvasText.Text(group.Title(), CanvasText.RowSize, CanvasText.LabelBrush, outline.Width - TabPadding * 2, true);

        // A tab only as wide as the name it carries, sitting on the ring: it
        // joins the name to the region without becoming the header a shut box
        // wears.
        var tab = new Rect(
            handle.X,
            handle.Y,
            Math.Min(label.Width + TabPadding * 2, outline.Width),
            handle.Height);

        var tabShape = new RoundedRect(tab, GroupCornerRadius, GroupCornerRadius, 0, 0);

        // Square where the tab sits, so its foot meets the ring instead of
        // straddling the curve of a corner.
        var flushRight = tab.Width >= outline.Width;
        var ring = new RoundedRect(
            outline, 0, flushRight ? 0 : GroupCornerRadius, GroupCornerRadius, GroupCornerRadius);

        if (lifted)
        {
            context.DrawRectangle(PeekGround, null, ring);

            using (context.PushClip(ring)) DrawGrid(context);
        }

        context.DrawRectangle(
            OpenGroupFill,
            lifted ? PeekPen : isSelected ? OpenGroupPenSelected : OpenGroupPen,
            ring);

        if (lifted) context.DrawRectangle(Background, null, tabShape);

        context.DrawRectangle(isSelected ? OpenGroupTabSelected : OpenGroupTab, null, tabShape);

        context.DrawText(
            label, new Point(tab.X + TabPadding, tab.Y + (tab.Height - label.Height) / 2));
    }

    /// <summary>
    /// Draws a box, faintly and struck through where every module in it is
    /// switched off.
    /// </summary>
    /// <remarks>
    /// The same marking a module that is off wears, for the same reason: a box is
    /// the one place the modules cannot say it themselves. An open group is left
    /// alone — the strike through each of its modules is right there.
    /// </remarks>
    private void DrawBox(DrawingContext context, NodeGroup group, GroupSockets sockets, Rect bounds)
    {
        if (!selection.Scene.SwitchedOff(group))
        {
            DrawBoxFace(context, group, sockets, bounds, off: false);
            return;
        }

        using (context.PushOpacity(OffOpacity))
            DrawBoxFace(context, group, sockets, bounds, off: true);
    }

    private void DrawBoxFace(
        DrawingContext context, NodeGroup group, GroupSockets sockets, Rect bounds, bool off)
    {
        // Selected when its modules are, because pressing the box is what selects
        // them — there is nothing else it could mean for a box to be picked.
        var isSelected = group.Members.Count > 0 && group.Members.All(selection.Contains);

        // A box has no focus of its own — a module does — but the inspector's
        // subject can be one of a shut box's hidden members, the same way
        // pressing the box leaves focus on one of them. That is the rule a
        // module's border follows, so a box follows it too.
        var isFocused = selection.Focus is { } f && group.Members.Contains(f);

        var body = new RoundedRect(bounds, NodeGeometry.CornerRadius);

        context.DrawRectangle(
            NodeSkin.Box(isSelected),
            !isSelected ? NodeSkin.Edge : isFocused ? SelectionPen : SelectionPenSecondary,
            body);

        NodeSkin.DrawMark(context, body, ModuleGlyphs.Group, NodeSkin.BoxMark);

        var header = new Rect(bounds.X, bounds.Y, bounds.Width, NodeGeometry.HeaderHeight);
        context.DrawRectangle(
            NodeSkin.BoxHeaderOf(isSelected),
            null,
            new RoundedRect(header, NodeGeometry.CornerRadius, NodeGeometry.CornerRadius, 0, 0));

        NodeSkin.Relief(context, header);

        var title = CanvasText.Text(group.Title(), HeaderSize, HeaderTextBrush, bounds.Width - 16, true);
        var titleAt = new Point(bounds.X + 9, bounds.Y + 5);

        context.DrawText(title, titleAt);

        if (off)
            context.DrawLine(
                OffStrike,
                new Point(titleAt.X, titleAt.Y + title.Height / 2),
                new Point(titleAt.X + title.Width, titleAt.Y + title.Height / 2));

        for (var i = 0; i < sockets.Outputs.Count; i++)
            DrawBoxSocket(context, sockets.Outputs[i], NodeGeometry.GroupOutputPort(bounds, i), bounds);

        for (var i = 0; i < sockets.Inputs.Count; i++)
            DrawBoxSocket(
                context, sockets.Inputs[i], geometry.GroupInputPort(bounds, sockets, i), bounds);
    }

    /// <summary>
    /// One socket of a box, named for the port inside that it stands for:
    /// "filter.cutoff" rather than a name of its own, so renaming a module inside
    /// relabels the box for nothing.
    /// </summary>
    private void DrawBoxSocket(DrawingContext context, GroupSocket socket, Point center, Rect bounds)
    {
        if (selection.Scene.Named(socket) is not var (label, spec)) return;

        // An unwired input shows what it rests at, as the module's own row does.
        var resting = !geometry.Compact
            && !socket.IsOutput
            && Patch.IncomingTo(socket.Node, socket.Port) is null
            && Patch.Find(socket.Node) is { } node
            && DrawRestingOf(context, node, spec, socket.Port, bounds, center);

        var width = BoxLabelRoom(bounds, resting, geometry.Compact);
        var text = CanvasText.Text(CanvasText.Fit(label, width), CanvasText.RowSize, CanvasText.LabelBrush, width, true);

        context.DrawText(
            text,
            socket.IsOutput
                ? new Point(bounds.Right - 14 - text.Width, center.Y - text.Height / 2)
                : new Point(bounds.X + 14, center.Y - text.Height / 2));

        NodeSkin.DrawPort(context, center, spec.Kind);
    }

    /// <summary>A box's unwired input, valued the way the module's row values it; whether anything was drawn.</summary>
    private bool DrawRestingOf(DrawingContext context, NodeInstance node, PortSpec spec, int port, Rect bounds, Point center)
    {
        static IBrush Plain(double center, double height, double fade, IBrush plain) => plain;

        if (DrawLinkedValue(context, node, spec, port, bounds, center, follow: false, Plain)) return true;

        var formula = NodeCatalog.FormulaOf(node);
        var spans = node.TypeId == NodeCatalog.AutoRemapTypeId ? AutoRemap.Of(Patch, node) : null;

        return DrawResting(context, node, spec, port, bounds, center, formula, spans, Plain);
    }

    /// <summary>How much of a box's width its sockets and their margins take from a label.</summary>
    private const double SocketLabelRoom = 26;

    /// <summary>How wide a box socket's label may be drawn: beside its value, on half a shared row, or across the box.</summary>
    internal static double BoxLabelRoom(Rect bounds, bool resting, bool compact) =>
        resting ? bounds.Width * 0.55 : compact ? HalfRow(bounds) : bounds.Width - SocketLabelRoom;

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
        foreach (var id in selection.Ids)
        {
            if (Patch.Find(id) is not { } end || NodeCatalog.BusOf(end) is not { } bus) continue;
            if (selection.Scene.Shut(end.Id) || NodeCatalog.Get(end.TypeId) is not { } def) continue;

            var partner = end.TypeId == NodeCatalog.SendTypeId ? NodeCatalog.ReceiveTypeId : NodeCatalog.SendTypeId;
            var from = geometry.Bounds(end, def);

            foreach (var other in Patch.Nodes)
            {
                if (other.TypeId != partner || selection.Scene.Shut(other.Id)) continue;
                if (!string.Equals(NodeCatalog.BusOf(other), bus, StringComparison.OrdinalIgnoreCase)) continue;
                if (NodeCatalog.Get(other.TypeId) is not { } otherDef) continue;

                var to = geometry.Bounds(other, otherDef);

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

    /// <summary>How wide a socket's name may be drawn on a shared row: half the module, less the socket and margins.</summary>
    internal static double HalfRow(Rect bounds) => bounds.Width / 2 - 18;

    /// <summary>
    /// A compact input row: its name, a dot where it follows a panel knob, and a
    /// flag where an Auto remap's range has nothing at the far end to take from.
    /// </summary>
    private void DrawCompactInput(
        DrawingContext context, NodeInstance node, PortSpec port, int i, Rect bounds, Point center,
        bool connected, bool follow, RemapSpans? spans, Func<double, double, double, IBrush, IBrush> ink)
    {
        DrawLinkWash(context, node, port, i, bounds, center, connected);

        var brush = ink(center.Y, RowInk, 0, CanvasText.LabelBrush);
        var label = CanvasText.Text(port.Name, CanvasText.RowSize, brush, HalfRow(bounds), true);
        var at = new Point(bounds.X + 14, center.Y - label.Height / 2);

        context.DrawText(label, at);

        if (!connected && ControlMap.Of(node, i) is { } found && Patch.Control(found.Control) is not null)
            context.DrawEllipse(follow ? brush : LinkedBrush, null, new Point(at.X + label.Width + 6, center.Y), 2.5, 2.5);
        else if (!connected && i < node.InputValues.Length && SocketTips.RemapValue(spans, i, node.InputValues[i]) is (_, true))
            context.DrawRectangle(null, FlagPen, new Rect(at.X - 3, at.Y - 1, label.Width + 6, label.Height + 2), 3, 3);

        NodeSkin.DrawPort(context, center, port.Kind);
    }


    /// <summary>
    /// Tints a row while linking, and draws a linked socket's value in the knob's
    /// color. Returns whether it drew the value, so the ordinary one is not drawn too.
    /// </summary>
    /// <param name="follow">Whether the module is asking for its text colored from its background.</param>
    /// <param name="ink">
    /// The same helper the rest of a row's text is colored through — see
    /// <see cref="NodeSkin.Ink"/>. The Attention hue is a shell color the module's own
    /// background may not read against, so it gives way to the row's ink when the
    /// module asks for one.
    /// </param>
    private bool DrawLinkedRow(
        DrawingContext context, NodeInstance node, PortSpec port, int index, Rect bounds, Point center,
        bool connected, bool follow, Func<double, double, double, IBrush, IBrush> ink)
    {
        DrawLinkWash(context, node, port, index, bounds, center, connected);

        return !connected && DrawLinkedValue(context, node, port, index, bounds, center, follow, ink);
    }

    /// <summary>Tints an input's row while linking: its half of a shared row on a compact module.</summary>
    private void DrawLinkWash(
        DrawingContext context, NodeInstance node, PortSpec port, int index, Rect bounds, Point center, bool connected)
    {
        if (linking.Control is not { } knob || connected || !KnobLinking.Linkable(port)) return;

        var width = geometry.Compact ? bounds.Width / 2 : bounds.Width;
        var row = new Rect(bounds.X, center.Y - NodeGeometry.RowHeight / 2, width, NodeGeometry.RowHeight);

        context.FillRectangle(ControlMap.Of(node, index)?.Control == knob ? LinkedWash : LinkableWash, row);
    }

    /// <summary>A linked socket's value in the knob's color, and whether there was one to draw.</summary>
    private bool DrawLinkedValue(
        DrawingContext context, NodeInstance node, PortSpec port, int index, Rect bounds, Point center,
        bool follow, Func<double, double, double, IBrush, IBrush> ink)
    {
        if (ControlMap.Of(node, index) is not { } found || Patch.Control(found.Control) is not { } control) return false;

        var brush = follow ? ink(center.Y, RowInk, ValueFade, CanvasText.ValueBrush) : LinkedBrush;

        var value = CanvasText.Text(port.Format(found.At(control.Value)), CanvasText.RowSize, brush, bounds.Width * 0.4, true);
        var right = bounds.Right - 12;

        context.DrawText(value, new Point(right - value.Width, center.Y - value.Height / 2));
        context.DrawEllipse(brush, null, new Point(right - value.Width - 6, center.Y), 2.5, 2.5);

        return true;
    }
}
