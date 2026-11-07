using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Flyback.Core.Graph;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Canvas;

/// <summary>
/// A module on the canvas: its body, artwork and title, and each socket's row with
/// its name, value, well and link.
/// </summary>
internal sealed class ModulePainter(
    CanvasHistory history,
    CanvasSelection selection,
    Viewport view,
    SocketFocus focus,
    KnobLinking linking,
    UndescribedTags tags,
    Repaint repaint,
    NodeGeometry geometry,
    CanvasLift lift,
    CanvasGlass glass,
    PaintFrame frame)
{
    private static readonly IBrush NormalBrush = new ImmutableSolidColorBrush(Colors.Normalled);
    private static readonly IBrush LinkedBrush = new ImmutableSolidColorBrush(Colors.Attention, 0.85);
    private static readonly IBrush LinkableWash = new ImmutableSolidColorBrush(Colors.Attention, 0.07);
    private static readonly IBrush LinkedWash = new ImmutableSolidColorBrush(Colors.Attention, 0.24);
    private static readonly IImmutableBrush FlagBrush = new ImmutableSolidColorBrush(Colors.Attention);
    private static readonly IPen FlagPen = new ImmutablePen(FlagBrush);

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

    private const double IdleOpacity = 0.35;

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

    private bool moving, ticking;

    private Patch Patch => history.Patch;

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
    internal void KeepMoving()
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

    /// <summary>
    /// Draws a module, faintly where it is switched off.
    /// </summary>
    /// <remarks>
    /// Faint rather than differently colored: what the module is has not changed,
    /// and its category's accent is how the canvas is read at a glance. The strike
    /// through the name is what says it outright, since a patch drawn small is
    /// faint everywhere.
    /// </remarks>
    internal void DrawNode(DrawingContext context, NodeInstance node, NodeDef def)
    {
        using (CanvasLift.Hold(context, geometry.Bounds(node, def), node.Off, lift.Raised(node.Id)))
            DrawModule(context, node, def);
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

    /// <summary>A module's body, artwork and title band: everything under its text.</summary>
    private void DrawFill(
        DrawingContext context, NodeDef def, RoundedRect body, Rect bounds, Rect header, Color accent, Color floor,
        bool isSelected, ModuleBackdrop backdrop, IPen border)
    {
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
            // The body stops where the band starts, so a module drawn faint shows
            // the canvas through its band rather than its body.
            var radius = NodeGeometry.CornerRadius;
            var under = new RoundedRect(
                new Rect(bounds.X, header.Bottom, bounds.Width, Math.Max(0, bounds.Bottom - header.Bottom)), 0, 0, radius, radius);

            context.DrawRectangle(NodeSkin.Body(accent, floor, isSelected), null, under);

            if (ModuleSkins.Of(def) is ModuleSkin.Grain { Cut: var cut })
                using (context.PushClip(under))
                    context.FillRectangle(
                        NodeSkin.Cut(cut, NodeSkin.BodyTop(accent, isSelected)),
                        bounds);

            context.DrawRectangle(NodeSkin.Header(accent, floor, isSelected), null, ModuleFace.HeaderShape(header));
            context.DrawRectangle(null, border, body);
        }

        NodeSkin.Relief(context, header, accent);
    }

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
        var border = ModuleFace.BorderFor(isSelected, selection.Focus == node.Id);
        var header = ModuleFace.HeaderOf(bounds);

        using (context.PushOpacity(glass.FillOpacity(node.Id)))
            DrawFill(context, def, body, bounds, header, accent, floor, isSelected, backdrop, border);

        glass.DrawEdge(context, bounds);

        if (ModuleOverview.At(view.Zoom))
        {
            ModuleOverview.DrawName(
                context, bounds, ModuleFace.Heading(node, def), view.Zoom, ModuleFace.OverviewTextBrush, node.Off,
                ModuleFace.GlyphOf(def), NodeSkin.Glyph(accent), ModuleFace.HiddenType(node, def));

            for (var i = 0; i < def.Outputs.Count; i++)
                NodeSkin.DrawPort(context, NodeGeometry.OutputPort(node, i), def.Outputs[i].Kind);

            for (var i = 0; i < def.Inputs.Count; i++)
                NodeSkin.DrawPort(context, geometry.InputPort(node, def, i), def.Inputs[i].Kind);

            return;
        }

        var titleBrush = Ink(bounds.Y + (3 + NodeGeometry.HeaderHeight) / 2, TitleInk, fade: 0, ModuleFace.HeaderTextBrush);

        var glyph = ModuleFace.GlyphOf(def);

        var headerWidth = ModuleFace.HeaderWidth(bounds, tags.Tagged(def), glyph is not null);
        var title = CanvasText.Text(ModuleFace.Heading(node, def), ModuleFace.HeaderSize, titleBrush, headerWidth, true);
        var titleAt = ModuleFace.TitleAt(bounds, title, glyph is not null);

        if (glyph is not null) ModuleFace.DrawGlyph(context, bounds, glyph, NodeSkin.Glyph(accent));

        ModuleFace.DrawTitle(context, title, titleAt, node.Off ? follow ? new Pen(titleBrush, 1.5) : ModuleFace.OffStrike : null);

        if (ModuleFace.HiddenType(node, def) is { } type && ModuleFace.LayType(type, bounds, title, titleAt, headerWidth, titleBrush) is { } laid)
            ModuleFace.DrawType(context, laid);

        if (tags.Tagged(def)) UndescribedTags.Draw(context, bounds, follow ? titleBrush : null);

        var formula = NodeCatalog.FormulaOf(node);
        var spans = def.TypeId == NodeCatalog.AutoRemapTypeId ? AutoRemap.Of(Patch, node) : null;

        for (var i = 0; i < def.Outputs.Count; i++)
        {
            var port = def.Outputs[i];
            var center = NodeGeometry.OutputPort(node, i);
            var room = geometry.Compact ? ModuleFace.HalfRow(bounds) : bounds.Width - 24;
            var label = CanvasText.Text(OutputNames.Of(node, def, i), CanvasText.RowSize, Ink(center.Y, RowInk, 0, CanvasText.LabelBrush), room, true);

            CanvasText.Draw(context, label, ModuleFace.LabelAt(bounds, center, label, output: true));
            NodeSkin.DrawPort(context, center, port.Kind);
        }

        for (var i = 0; i < def.Inputs.Count; i++)
            DrawInputRow(context, node, def, def.Inputs[i], i, def.Inputs[i].Name, bounds, geometry.InputPort(node, def, i), follow, formula, spans, Ink);

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
    /// <param name="well">How the value's well is drawn, or null for an input that has none.</param>
    private bool DrawResting(
        DrawingContext context, NodeInstance node, PortSpec port, int i, Rect bounds, Point center,
        string? formula, RemapSpans? spans, WellState? well, Func<double, double, double, IBrush, IBrush> ink)
    {
        if (NodeCatalog.Normalled(port) is { } source)
        {
            // Wider than the column a number gets: "Coordinates x" does not fit
            // where "0.25" does, and trimmed it would stop telling x from y.
            var name = CanvasText.Text(source, CanvasText.RowSize, ink(center.Y, RowInk, NormalFade, NormalBrush), bounds.Width * 0.5, true);
            CanvasText.Draw(context, name, new Point(bounds.Right - 12 - name.Width, center.Y - name.Height / 2));
            return true;
        }

        if (i >= node.InputValues.Length || (formula is not null && !FormulaLayout.Reads(formula, i))) return false;

        var (said, flagged) = SocketTips.RemapValue(spans, i, node.InputValues[i]) ?? (port.Format(node.InputValues[i]), false);

        if (well is { } state && !flagged)
        {
            ValueWell.Draw(
                context,
                ValueWell.At(bounds, center),
                said,
                Share(port, node.InputValues[i]),
                port.Stepped ? (int)(port.Max - port.Min) + 1 : 0,
                state,
                ink(center.Y, RowInk, 0, glass.SeeThrough ? ValueWell.GlassInk : ValueWell.Ink),
                glass.SeeThrough);
            return true;
        }

        var value = CanvasText.Text(
            said,
            CanvasText.RowSize,
            flagged ? FlagBrush : ink(center.Y, RowInk, ValueFade, CanvasText.ValueBrush),
            bounds.Width * 0.4,
            true);
        var spot = new Point(bounds.Right - 12 - value.Width, center.Y - value.Height / 2);

        CanvasText.Draw(context, value, spot);

        if (flagged)
            context.DrawRectangle(null, FlagPen, new Rect(spot.X - 3, spot.Y - 1, value.Width + 6, value.Height + 2), 3, 3);

        return true;
    }

    /// <summary>Where a value sits along its socket's range, which a value outside it widens.</summary>
    private static double Share(PortSpec port, float value)
    {
        var min = Math.Min(port.Min, value);
        var max = Math.Max(port.Max, value);

        return max > min ? port.Travel(value, min, max) : 0;
    }

    /// <summary>
    /// An input's row, on its module or on a shut box standing for it: the well, the
    /// link wash, the name, the value and the socket's mark. Both draw it through here,
    /// so a mark added for one is on the other too.
    /// </summary>
    internal void DrawInputRow(
        DrawingContext context, NodeInstance node, NodeDef def, PortSpec port, int index, string name, Rect bounds, Point center,
        bool follow, string? formula, RemapSpans? spans, Func<double, double, double, IBrush, IBrush> ink)
    {
        var connected = (frame.For(Patch) is { } memo ? memo.IncomingTo(node.Id, index) : Patch.IncomingTo(node.Id, index)) is not null;

        using var idle = ModuleFace.Idle(formula, index) ? context.PushOpacity(IdleOpacity) : (IDisposable?)null;

        if (geometry.Compact)
        {
            DrawCompactInput(context, node, port, index, name, bounds, center, connected, follow, spans, ink);
            return;
        }

        var well = !connected && ValueWell.Has(Patch, node, def, index) && history.Turnable(node.Id, index) ? focus.StateOf(node.Id, index) : (WellState?)null;

        if (well is { } lit) ValueWell.DrawRow(context, bounds, center, lit);

        DrawLinkWash(context, node, port, index, bounds, center, connected);

        var valued = !connected && DrawInputValue(context, node, port, index, bounds, center, follow, formula, spans, well, ink);
        var width = ModuleFace.BoxLabelRoom(bounds, valued, compact: false);
        var label = CanvasText.Text(CanvasText.Fit(name, width), CanvasText.RowSize, ink(center.Y, RowInk, 0, CanvasText.LabelBrush), width, true);

        CanvasText.Draw(context, label, ModuleFace.LabelAt(bounds, center, label, output: false));

        if (well is { } state) ValueWell.DrawSocket(context, center, port.Kind, state);
        else if (KnobChip.Of(Patch, node, index) is not null) SocketDiamond.DrawLinked(context, center, port.Kind);
        else if (!connected && !KnobLinking.Linkable(port)) SocketDiamond.DrawWireOnly(context, center, port.Kind);
        else NodeSkin.DrawPort(context, center, port.Kind);
    }

    /// <summary>
    /// A compact input row: its name, a dot where it follows a panel knob, and a
    /// flag where an Auto remap's range has nothing at the far end to take from.
    /// </summary>
    private void DrawCompactInput(
        DrawingContext context, NodeInstance node, PortSpec port, int i, string name, Rect bounds, Point center,
        bool connected, bool follow, RemapSpans? spans, Func<double, double, double, IBrush, IBrush> ink)
    {
        DrawLinkWash(context, node, port, i, bounds, center, connected);

        var brush = ink(center.Y, RowInk, 0, CanvasText.LabelBrush);
        var room = ModuleFace.HalfRow(bounds);
        var label = CanvasText.Text(CanvasText.Fit(name, room), CanvasText.RowSize, brush, room, true);
        var at = ModuleFace.LabelAt(bounds, center, label, output: false);

        CanvasText.Draw(context, label, at);

        if (!connected && ControlMap.Of(node, i) is { } found && Patch.Control(found.Control) is not null)
            context.DrawEllipse(follow ? brush : LinkedBrush, null, new Point(at.X + label.Width + 6, center.Y), 2.5, 2.5);
        else if (!connected && i < node.InputValues.Length && SocketTips.RemapValue(spans, i, node.InputValues[i]) is (_, true))
            context.DrawRectangle(null, FlagPen, new Rect(at.X - 3, at.Y - 1, label.Width + 6, label.Height + 2), 3, 3);

        if (KnobChip.Of(Patch, node, i) is not null) SocketDiamond.DrawLinked(context, center, port.Kind);
        else if (!connected && !KnobLinking.Linkable(port)) SocketDiamond.DrawWireOnly(context, center, port.Kind);
        else NodeSkin.DrawPort(context, center, port.Kind);
    }

    /// <summary>
    /// What an unwired input is set to, at the right of its row: the value of the knob it
    /// follows, and otherwise what it rests at. Whether anything was drawn.
    /// </summary>
    /// <param name="follow">Whether the module is asking for its text colored from its background.</param>
    /// <param name="ink">
    /// The same helper the rest of a row's text is colored through — see
    /// <see cref="NodeSkin.Ink"/>. The Attention hue is a shell color the module's own
    /// background may not read against, so it gives way to the row's ink when the
    /// module asks for one.
    /// </param>
    private bool DrawInputValue(
        DrawingContext context, NodeInstance node, PortSpec port, int index, Rect bounds, Point center,
        bool follow, string? formula, RemapSpans? spans, WellState? well, Func<double, double, double, IBrush, IBrush> ink) =>
        DrawLinkedValue(context, node, port, index, bounds, center, follow, ink)
        || DrawResting(context, node, port, index, bounds, center, formula, spans, well, ink);

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
        if (KnobChip.Of(Patch, node, index) is not var (place, control, found)) return false;

        var brush = follow
            ? ink(center.Y, RowInk, ValueFade, CanvasText.ValueBrush)
            : ink(center.Y, RowInk, 0, glass.SeeThrough ? ValueWell.GlassInk : ValueWell.Ink);

        KnobChip.Draw(context, bounds, center, port.Format(found.At(control.Value)), KnobChoice.Tag(place), brush);
        return true;
    }
}
