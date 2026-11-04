using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Core.Graph;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Canvas;

/// <summary>
/// Groups on the canvas: the ring round an open one, a shut one's box, and the box
/// being looked into over everything else.
/// </summary>
internal sealed class GroupPainter(
    CanvasHistory history,
    CanvasSelection selection,
    Viewport view,
    CanvasGestures gestures,
    MeasureLabels measured,
    NodeGeometry geometry,
    CanvasLift lift,
    CanvasGlass glass,
    GroundPainter ground,
    WirePainter wires,
    ModulePainter modules)
{
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
    private static readonly IPen OpenGroupPen = new ImmutablePen(
        new ImmutableSolidColorBrush(Colors.Separator, 0.5),
        1.5,
        new ImmutableDashStyle([6, 4], 0));

    /// <summary>
    /// The same ring while everything inside it is selected.
    /// </summary>
    /// <remarks>
    /// A shut box turns its border the selected color, so without this the one
    /// picture saying which modules a gesture is about goes missing exactly when
    /// the group is opened to work on. Held well under
    /// <see cref="ModuleFace.SelectionPen"/>: the modules inside are already ringed one by
    /// one, and this is the line round the lot of them.
    /// </remarks>
    private static readonly IPen OpenGroupPenSelected = new ImmutablePen(
        new ImmutableSolidColorBrush(Colors.Attention, 0.55),
        1.5,
        new ImmutableDashStyle([6, 4], 0));

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
    /// A tab is what a named region wears everywhere, and it joins the name to the
    /// region it names. It is still not a header, because it is only as wide as
    /// the name.
    /// </remarks>
    private static readonly IBrush OpenGroupTab = new ImmutableSolidColorBrush(Colors.Separator, 0.22);

    private static readonly IBrush OpenGroupTabSelected = new ImmutableSolidColorBrush(Colors.Attention, 0.22);

    /// <summary>The header of the group a Shift-drag would drop into.</summary>
    private static readonly IBrush DropHeader = new ImmutableSolidColorBrush(Colors.Attention, 0.55);

    /// <summary>The ring round a box being looked into: solid, because it is over everything.</summary>
    private static readonly IPen PeekPen = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Separator), 1.5);

    /// <summary>The ground inside a box being looked into: slightly transparent, so the canvas under it still shows.</summary>
    private static readonly IBrush PeekGround = new ImmutableSolidColorBrush(Colors.Canvas, 0.70);

    /// <summary>What the canvas outside a box being looked into is dimmed under.</summary>
    private static readonly IBrush PeekScrim = new ImmutableSolidColorBrush(Colors.Edge, 0.78);

    /// <summary>How much wider than its title a tab is drawn, and how far in the title sits.</summary>
    private const double TabPadding = 9;

    /// <summary>
    /// How round a region's corners are. Softer than a module's, because it is a
    /// region and not a thing.
    /// </summary>
    private const double GroupCornerRadius = 3;

    private Patch Patch => history.Patch;

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
    internal void DrawOpenGroups(DrawingContext context, CanvasScene scene)
    {
        if (Patch.Groups is null) return;

        foreach (var group in Patch.Groups)
            if (group != scene.Peek && scene.OpenGroup(group, Leaving) is var (outline, handle))
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
    internal void DrawPeek(DrawingContext context, CanvasScene scene, IReadOnlySet<Guid> lifted)
    {
        if (scene.Peek is not { } group || scene.OpenGroup(group, Leaving) is not var (outline, handle)) return;

        context.FillRectangle(PeekScrim, new Rect(view.ToGraph(default), view.ToGraph(new Point(view.Size.Width, view.Size.Height))));

        DrawRing(context, group, outline, handle, lifted: true);

        wires.DrawConnections(context, lifted, theirs: false, peeked: true);

        foreach (var node in Patch.Nodes)
            if (scene.InPeek(node.Id) && NodeCatalog.Get(node.TypeId) is { } def)
                modules.DrawNode(context, node, def);

        wires.DrawConnections(context, lifted, theirs: true, peeked: true);

        if (!gestures.Gesturing) measured.Draw(context, peeked: true);
    }

    /// <summary>What a Shift-drag is taking out of its group, which the ring is drawn without.</summary>
    private IReadOnlySet<Guid>? Leaving => gestures.Regrouping ? gestures.Regrouped : null;

    /// <summary>Whether letting go of a Shift-drag now leaves modules in this group.</summary>
    private bool DropsInto(NodeGroup group) => ReferenceEquals(gestures.Landing, group);

    private void DrawRing(DrawingContext context, NodeGroup group, Rect outline, Rect handle, bool lifted)
    {
        // Selected when its modules are, which is the rule a shut box uses —
        // and it is the same gesture that selects them, since pressing the
        // strip takes the group.
        var isSelected = selection.Holds(group);

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

            using (context.PushClip(ring)) ground.DrawGrid(context);
        }

        context.DrawRectangle(
            OpenGroupFill,
            lifted ? PeekPen : isSelected ? OpenGroupPenSelected : OpenGroupPen,
            ring);

        if (lifted) context.DrawRectangle(GroundPainter.Background, null, tabShape);

        context.DrawRectangle(
            DropsInto(group) ? DropHeader : isSelected ? OpenGroupTabSelected : OpenGroupTab, null, tabShape);

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
    internal void DrawBox(DrawingContext context, NodeGroup group, GroupSockets sockets, Rect bounds)
    {
        var off = selection.Scene.SwitchedOff(group);

        using (CanvasLift.Hold(context, bounds, off, lift.Raised(group)))
            DrawBoxFace(context, group, sockets, bounds, off);
    }

    private void DrawBoxFace(
        DrawingContext context, NodeGroup group, GroupSockets sockets, Rect bounds, bool off)
    {
        // Selected when its modules are, because pressing the box is what selects
        // them — there is nothing else it could mean for a box to be picked.
        var isSelected = selection.Holds(group);

        // A box has no focus of its own — a module does — but the inspector's
        // subject can be one of a shut box's hidden members, the same way
        // pressing the box leaves focus on one of them. That is the rule a
        // module's border follows, so a box follows it too.
        var isFocused = selection.Focus is { } f && group.Members.Contains(f);

        var body = new RoundedRect(bounds, NodeGeometry.CornerRadius);

        context.DrawRectangle(NodeSkin.Box(isSelected), ModuleFace.BorderFor(isSelected, isFocused), body);

        glass.DrawEdge(context, bounds);

        var header = ModuleFace.HeaderOf(bounds);

        context.DrawRectangle(NodeSkin.BoxHeaderOf(isSelected), null, ModuleFace.HeaderShape(header));

        if (DropsInto(group)) context.DrawRectangle(DropHeader, null, ModuleFace.HeaderShape(header));

        NodeSkin.Relief(context, header, Colors.Separator);

        if (ModuleOverview.At(view.Zoom))
        {
            ModuleOverview.DrawName(context, bounds, group.Title(), view.Zoom, ModuleFace.OverviewTextBrush, off);

            for (var i = 0; i < sockets.Outputs.Count; i++)
                if (selection.Scene.Named(sockets.Outputs[i]) is var (_, spec))
                    NodeSkin.DrawPort(context, NodeGeometry.GroupOutputPort(bounds, i), spec.Kind);

            for (var i = 0; i < sockets.Inputs.Count; i++)
                if (selection.Scene.Named(sockets.Inputs[i]) is var (_, spec))
                    NodeSkin.DrawPort(context, geometry.GroupInputPort(bounds, sockets, i), spec.Kind);

            return;
        }

        var title = CanvasText.Text(group.Title(), ModuleFace.HeaderSize, ModuleFace.HeaderTextBrush, bounds.Width - 16, true);

        ModuleFace.DrawTitle(context, title, ModuleFace.TitleAt(bounds, title), off ? ModuleFace.OffStrike : null);

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

        if (!socket.IsOutput && Patch.Find(socket.Node) is { } node && NodeCatalog.Get(node.TypeId) is { } def)
        {
            static IBrush Plain(double center, double height, double fade, IBrush plain) => plain;

            var spans = node.TypeId == NodeCatalog.AutoRemapTypeId ? AutoRemap.Of(Patch, node) : null;

            modules.DrawInputRow(context, node, def, spec, socket.Port, label, bounds, center, follow: false, NodeCatalog.FormulaOf(node), spans, Plain);
            return;
        }

        var width = ModuleFace.BoxLabelRoom(bounds, resting: false, geometry.Compact);
        var text = CanvasText.Text(CanvasText.Fit(label, width), CanvasText.RowSize, CanvasText.LabelBrush, width, true);

        CanvasText.Draw(context, text, ModuleFace.LabelAt(bounds, center, text, socket.IsOutput));
        NodeSkin.DrawPort(context, center, spec.Kind);
    }
}
