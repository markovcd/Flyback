using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Flyback.Editor.Notices;
using Flyback.Core.Graph;

namespace Flyback.Editor.Canvas;

/// <summary>
/// The hand on the canvas: what a press, a drag, a release and a wheel turn do. One
/// <see cref="Drag"/> state says which gesture is under way, and hands the pointer to
/// <see cref="ModuleDrag"/>, <see cref="WireDrag"/> or <see cref="RubberBand"/>; a pan
/// stays here, since it can put any of them on hold.
/// </summary>
/// <remarks>
/// Each handler takes the canvas it is for, which is what the pointer is captured to
/// and whose cursor and tooltip change.
/// </remarks>
internal sealed class CanvasGestures
{
    private enum Drag
    {
        None,
        Pan,
        Node,
        Wire,
        Marquee,
    }

    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor PortCursor = new(StandardCursorType.Cross);
    private static readonly Cursor NodeCursor = new(StandardCursorType.SizeAll);

    /// <summary>
    /// While a button is dragging the view. A hand rather than the four-way
    /// arrow a module gets, because what is moving is not in the patch.
    /// </summary>
    private static readonly Cursor PanCursor = new(StandardCursorType.Hand);

    /// <summary>How far, in screen pixels, a press may wander and still be a click.</summary>
    private const double ClickSlop = 4;

    private readonly CanvasHistory history;
    private readonly CanvasSelection selection;
    private readonly Viewport view;
    private readonly CanvasEdits edits;
    private readonly SocketDial dial;
    private readonly HeldModules held;
    private readonly KnobLinking linking;
    private readonly RemapMarks marks;
    private readonly CanvasTips tips;
    private readonly Repaint repaint;
    private readonly ModuleDrag module;
    private readonly WireDrag wire;
    private readonly RubberBand band;

    private Drag drag;

    /// <summary>
    /// What the middle button put on hold to pan, and where the pan started.
    /// <see cref="Drag.None"/> when the pan has nothing under it.
    /// </summary>
    private Drag panSuspended = Drag.None;
    private Point panOrigin;

    /// <summary>Where a press that is still a click went down, in screen space; null once it has moved off.</summary>
    private Point? stillAt;

    private readonly Reactions reactions;

    public CanvasGestures(
        CanvasHistory history,
        CanvasSelection selection,
        Viewport view,
        CanvasEdits edits,
        SocketDial dial,
        HeldModules held,
        KnobLinking linking,
        RemapMarks marks,
        CanvasTips tips,
        Repaint repaint,
        ModuleDrag module,
        WireDrag wire,
        RubberBand band,
        Reactions reactions)
    {
        this.history = history;
        this.reactions = reactions;
        this.selection = selection;
        this.view = view;
        this.edits = edits;
        this.dial = dial;
        this.held = held;
        this.linking = linking;
        this.marks = marks;
        this.tips = tips;
        this.repaint = repaint;
        this.module = module;
        this.wire = wire;
        this.band = band;

        history.Replaced += (_, _) => End();
        wire.Dropped += (_, drop) => WireDropped?.Invoke(this, drop);
    }

    /// <summary>
    /// Raised by a right-click on empty canvas, or Space, carrying the point in graph
    /// space: what is picked from the palette belongs there.
    /// </summary>
    public event EventHandler<Point>? MenuRequested;

    /// <summary>
    /// A wire was let go over empty canvas. What the shell puts there is the palette,
    /// narrowed to what could take the wire.
    /// </summary>
    public event EventHandler<WireDrop>? WireDropped;

    /// <summary>
    /// Whether a module is being moved, a wire drawn, the view panned or a band drawn
    /// out. The shell holds back moving the canvas while one is.
    /// </summary>
    public bool Gesturing => drag != Drag.None;

    /// <summary>Whether the selection is being carried, so its wires are drawn over everything else.</summary>
    public bool Carrying => drag == Drag.Node;

    /// <inheritdoc cref="ModuleDrag.Regrouping"/>
    public bool Regrouping => module.Regrouping;

    /// <inheritdoc cref="ModuleDrag.Landing"/>
    public NodeGroup? Landing => module.Landing;

    /// <inheritdoc cref="ModuleDrag.Regrouped"/>
    public IReadOnlySet<Guid> Regrouped => module.Regrouped;

    /// <summary>
    /// Whether a key may change the patch: not on a locked canvas, and not while the
    /// pointer holds a piece of it, since Delete mid-wire would complete a wire from a
    /// module that has gone.
    /// </summary>
    public bool Editable => !history.Locked && drag == Drag.None && !dial.Turning;

    /// <summary>Where the pointer was last seen, in graph space, for a gesture with no position of its own.</summary>
    public Point? LastPointer { get; private set; }

    /// <summary>
    /// Where the wire being dragged is anchored, null when none is. Through the scene's
    /// anchors like every other wire, since the port may be behind a box.
    /// </summary>
    public Point? PendingWireFrom => drag == Drag.Wire ? wire.Anchor : null;

    /// <summary>Whether the wire being drawn plugs into an output rather than an input; null when none is being drawn.</summary>
    public bool? PendingWireTakesOutput => PendingWireFrom is null ? null : !wire.FromOutput;

    /// <summary>
    /// Whether a mouse drags empty canvas with the left button to pan, and with the right
    /// to draw the rubber band, a right-click there opening the list (ADR-0182). Off, the
    /// middle button pans and the left draws the band.
    /// </summary>
    public bool DragToPan { get; set; }

    /// <summary>Opens the palette where the pointer last was, or in the middle of the view.</summary>
    public void RequestMenu() => MenuRequested?.Invoke(this, LastPointer ?? view.Middle);

    /// <summary>Opens the palette in the middle of the view, for a button with no point of its own.</summary>
    public void RequestMenuInMiddle() => MenuRequested?.Invoke(this, view.Middle);

    public void Pressed(Control canvas, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(canvas).Properties;

        var button = properties.IsMiddleButtonPressed ? MouseButton.Middle
            : properties.IsRightButtonPressed ? MouseButton.Right
            : properties.IsLeftButtonPressed ? MouseButton.Left
            : MouseButton.None;

        if (Pressed(canvas, e.Pointer, e.GetPosition(canvas), button, e.KeyModifiers, e.ClickCount)) e.Handled = true;
    }

    /// <summary>
    /// A press of <paramref name="button"/> at <paramref name="screen"/>, from a mouse
    /// or from <see cref="Fingers"/>, and whether it was answered in full.
    /// </summary>
    /// <param name="pointer">What is captured for the length of the gesture, if anything is.</param>
    public bool Pressed(Control canvas, IPointer? pointer, Point screen, MouseButton button, KeyModifiers modifiers, int clickCount)
    {
        var graph = view.ToGraph(screen);
        var scene = selection.Scene;

        // The middle button pans whatever the setting (ADR-0046, ADR-0182). It pans
        // mid-gesture too: whatever was under way is put on hold and picks back up
        // once the button comes up.
        if (button == MouseButton.Middle)
        {
            if (drag != Drag.Pan) panSuspended = drag;
            drag = Drag.Pan;
            panOrigin = screen;
            canvas.Cursor = PanCursor;
            pointer?.Capture(canvas);
            return false;
        }

        // After the pan, which leaves the grip of a carry it puts on hold where it was.
        module.Grip(graph);

        // Over a module Ctrl adds to the selection; over an output it lifts a wire off.
        var ctrl = (modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;

        var dragToPan = DragToPan && pointer?.Type != PointerType.Touch;

        if (button == MouseButton.Right)
        {
            // Over an unpatched input, the button held down is a knob for its value.
            if (drag == Drag.None && dial.Start(canvas, graph, screen, anchored: pointer?.Type != PointerType.Touch))
            {
                tips.Down(canvas);
                pointer?.Capture(canvas);
                return true;
            }

            var bare = !scene.HitPort(graph, out _, out _, out _)
                && scene.HitBox(graph) is null
                && scene.HitNode(graph) is null;

            // Dragged, it is a rubber band; let go where it went down, the list opens.
            if (dragToPan && bare && drag == Drag.None)
            {
                StartMarquee(graph, ctrl, MouseButton.Right);
                stillAt = screen;
                pointer?.Capture(canvas);
                return false;
            }

            // Not over a module, where a right-click is about that module, and not on
            // a locked canvas, where the next evaluation would take it straight off.
            if (!history.Locked && bare) MenuRequested?.Invoke(this, graph);

            // Over a module or a shut box, the button is held to flip it.
            if (!scene.HitPort(graph, out _, out _, out _))
            {
                if (scene.HitBox(graph) is { } shut) held.Hold(shut.Members);
                else if (scene.HitNode(graph) is { } under)
                    held.Hold(selection.Contains(under.Id) ? selection.Nodes.Select(n => n.Id) : [under.Id]);

                if (held.Holding) pointer?.Capture(canvas);
            }

            return false;
        }

        if (button != MouseButton.Left) return false;

        var liftedLast = wire.Pressed();

        // A press outside the box being looked into puts it back, and goes on to be
        // whatever press it was. A socket keeps it, so a wire can be drawn in.
        if (selection.Peeked is not null && !scene.Covered(graph) && !scene.HitPort(graph, out _, out _, out _))
        {
            selection.EndPeek();
            scene = selection.Scene;
        }

        if (linking.Pick(graph))
        {
            return true;
        }

        // A socket on a locked canvas is not a handle, so the press falls through to
        // the module under it.
        if (!history.Locked && scene.HitPort(graph, out var portNode, out var portIndex, out var isOutput))
        {
            // Before a wire is lifted off, so whatever hears of that change already sees a
            // gesture under way.
            drag = Drag.Wire;
            wire.Start(portNode, portIndex, isOutput, lifting: ctrl, liftedLast, graph);
            pointer?.Capture(canvas);
            repaint.Request();
            return false;
        }

        if (marks.At(graph) is var (marked, at))
        {
            marks.Splice(marked, at);
            return true;
        }

        // A box before a module, because that is the order they are painted in.
        if (scene.HitBox(graph) is null && scene.HitNode(graph) is { } node)
        {
            // Shift on one member of a group selected whole means that one: it is about
            // to be carried out, and the whole group would ride along as itself.
            if ((modifiers & KeyModifiers.Shift) != 0 && selection.Group is { } whole && whole.Members.Contains(node.Id))
                selection.Select(node.Id);

            module.PressModule(node, ctrl);
            drag = Drag.Node;
            if (!history.Locked) module.Aim(graph, modifiers);

            pointer?.Capture(canvas);
            repaint.Request();
            return false;
        }

        // A double-click on a box looks into it; a single click selects what is inside,
        // which the ordinary drag then moves.
        if (scene.HitBox(graph) is { } box)
        {
            if (clickCount == 2) selection.Peek(box);
            else PressGroup(box, ctrl);

            pointer?.Capture(canvas);
            repaint.Request();
            return false;
        }

        if (scene.HitOpenGroupHandle(graph) is { } opened)
        {
            if (clickCount == 2 && opened == selection.Peeked) selection.EndPeek();
            // Shutting a group is an edit, so a locked canvas selects it instead.
            else if (clickCount == 2 && !history.Locked) edits.ToggleBox(opened);
            else PressGroup(opened, ctrl);

            pointer?.Capture(canvas);
            repaint.Request();
            return false;
        }

        if (dragToPan)
        {
            drag = Drag.Pan;
            panOrigin = screen;
            stillAt = screen;
            canvas.Cursor = PanCursor;
            pointer?.Capture(canvas);
            return false;
        }

        // Left on empty canvas draws a rubber band. A band that sweeps nothing selects
        // nothing, which is what a click on empty canvas does.
        StartMarquee(graph, ctrl, MouseButton.Left);
        band.Sweep();

        pointer?.Capture(canvas);
        repaint.Request();
        return false;
    }

    private void StartMarquee(Point graph, bool adding, MouseButton button)
    {
        band.Start(graph, adding, button);
        drag = Drag.Marquee;
    }

    /// <summary>A press on a box, or on the strip above an open group, which carries what is inside.</summary>
    private void PressGroup(NodeGroup group, bool adding)
    {
        module.PressGroup(group, adding);
        drag = Drag.Node;
    }

    public void Moved(Control canvas, PointerEventArgs e) =>
        Moved(canvas, e.GetPosition(canvas), e.KeyModifiers, e.GetCurrentPoint(canvas).Properties.IsMiddleButtonPressed);

    /// <param name="middleDown">
    /// The middle button's own state: a second button going down while the first is
    /// captured does not reliably raise a press of its own.
    /// </param>
    public void Moved(Control canvas, Point screen, KeyModifiers modifiers, bool middleDown)
    {
        var graph = view.ToGraph(screen);

        LastPointer = graph;

        if (stillAt is { } at && Math.Abs(screen.X - at.X) + Math.Abs(screen.Y - at.Y) > ClickSlop) stillAt = null;

        if (dial.Turning)
        {
            dial.Move(canvas, screen, modifiers);
            return;
        }

        if (middleDown && drag != Drag.Pan)
        {
            panSuspended = drag;
            drag = Drag.Pan;
            panOrigin = screen;
            canvas.Cursor = PanCursor;
        }
        else if (!middleDown && drag == Drag.Pan && panSuspended != Drag.None)
        {
            // Every gesture a pan can suspend is held in the canvas's coordinates,
            // which a pan does not move.
            drag = panSuspended;
            panSuspended = Drag.None;

            canvas.Cursor = drag == Drag.Wire ? PortCursor : CursorOver(graph);
        }

        switch (drag)
        {
            case Drag.Pan:
                view.PanBy(screen - panOrigin);

                // From where the pointer is rather than where the view ended up, so a
                // drag pushing at an edge builds up no debt to pay back.
                panOrigin = screen;
                return;

            // A locked canvas still carries a module under the pointer, since nothing
            // here is written back into the text; it just cannot regroup, which is.
            case Drag.Node when module.Carrying:
                module.Carry(graph, modifiers);
                repaint.Request();
                return;

            // A right-click must not take the selection away on its way to the list.
            case Drag.Marquee when stillAt is null:
                band.Stretch(graph);
                repaint.Request();
                return;

            case Drag.Wire:
                wire.Stretch(graph);
                repaint.Request();
                return;

            case Drag.Marquee:
                return;

            default:
                canvas.Cursor = CursorOver(graph);
                tips.Over(canvas, graph);
                marks.Hover(graph);
                return;
        }
    }

    /// <summary>A lifted finger hovers over nothing, so what it brought up comes down.</summary>
    public void Lifted(Control canvas) => tips.Down(canvas);

    public void Released(Control canvas, PointerReleasedEventArgs e) =>
        Released(canvas, e.Pointer, e.GetPosition(canvas), e.InitialPressMouseButton, e.KeyModifiers);

    /// <param name="button">The button whose press began what is being let go of.</param>
    public void Released(Control canvas, IPointer? pointer, Point screen, MouseButton button, KeyModifiers modifiers)
    {
        var graph = view.ToGraph(screen);

        if (button == MouseButton.Right)
        {
            if (EndTurn(canvas)) pointer?.Capture(null);

            held.Release();

            if (drag == Drag.Marquee && band.Button == MouseButton.Right)
            {
                var click = stillAt is not null;
                var at = band.From;

                End();
                canvas.Cursor = CursorOver(graph);
                pointer?.Capture(null);
                repaint.Request();

                if (click && !history.Locked) MenuRequested?.Invoke(this, at);
            }

            return;
        }

        // The middle button's own release is answered here whatever drag is by now: the
        // gesture it interrupted must not be finished by a button that never held it.
        if (button == MouseButton.Middle)
        {
            if (panSuspended != Drag.None)
            {
                drag = panSuspended;
                panSuspended = Drag.None;

                canvas.Cursor = drag == Drag.Wire ? PortCursor : CursorOver(graph);
                repaint.Request();
                return;
            }

            if (drag == Drag.Pan)
            {
                End();
                canvas.Cursor = CursorOver(graph);
                pointer?.Capture(null);
                repaint.Request();
            }

            return;
        }

        // The button that began the gesture came up while a pan held it: a module stays
        // where it was carried to, and a wire is dropped, since its end was let go of
        // over a view that was moving.
        if (drag == Drag.Pan && panSuspended == Drag.Node) module.RecordMove();

        // A left pan that never moved was a click on empty canvas, which selects nothing.
        var ctrl = (modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        if (drag == Drag.Pan && panSuspended == Drag.None && stillAt is not null && !ctrl) selection.Select(null);

        if (drag == Drag.Wire) wire.Complete(graph);
        if (drag == Drag.Node) module.Drop(graph, modifiers);

        End();

        // Taken from where the pointer is, which after a pan is rarely where it was.
        canvas.Cursor = CursorOver(graph);

        pointer?.Capture(null);
        repaint.Request();
    }

    /// <summary>
    /// The pointer was taken away mid-gesture, and the button will come up somewhere
    /// this never hears about. A module stays where it was dragged to, as a step; a
    /// wire is dropped, since nobody chose where it ended.
    /// </summary>
    public void CaptureLost(Control canvas)
    {
        EndTurn(canvas);
        held.Release();

        if (drag == Drag.None) return;

        if (drag == Drag.Node || panSuspended == Drag.Node) module.RecordMove();

        End();
        canvas.Cursor = ArrowCursor;
        repaint.Request();
    }

    public void Wheel(Control canvas, PointerWheelEventArgs e)
    {
        view.ZoomAt(e.GetPosition(canvas), e.Delta.Y);
        e.Handled = true;
    }

    /// <summary>
    /// Puts back whatever the gesture under way has already changed, and says whether
    /// there was one to back out of.
    /// </summary>
    /// <remarks>
    /// A wire lifted off an input is off it from the press, a rubber band has replaced
    /// the selection on its first move, and a module drag has carried modules. A pan is
    /// not backed out of, so with one under way this ends what the pan suspended.
    /// </remarks>
    public bool Abort(Control canvas)
    {
        if (EndTurn(canvas, restore: true)) return true;

        var panning = drag == Drag.Pan;
        var aborting = panning ? panSuspended : drag;

        if (aborting == Drag.None) return false;

        switch (aborting)
        {
            case Drag.Wire:
                wire.Abort();
                break;

            case Drag.Node:
                module.Abort();
                break;

            case Drag.Marquee:
                band.Abort();
                break;
        }

        // The button is still down, and the release that follows finds nothing under
        // way, which is what makes this an abort rather than a pause.
        if (panning)
        {
            panSuspended = Drag.None;
        }
        else
        {
            End();
            if (LastPointer is { } over) canvas.Cursor = CursorOver(over);
        }

        repaint.Request();
        return true;
    }

    /// <summary>Ends whatever gesture was under way, and forgets what it was holding.</summary>
    public void End()
    {
        var ended = drag != Drag.None;

        drag = Drag.None;
        panSuspended = Drag.None;
        stillAt = null;

        module.Clear();
        band.Clear();

        if (ended) reactions.Raise(new GestureFinished());
    }

    /// <summary>The rubber band, over the canvas rather than in it, so its hairline and dashes hold at any zoom.</summary>
    public void DrawMarquee(DrawingContext context)
    {
        if (drag == Drag.Marquee) band.Draw(context);
    }

    /// <summary>The wire being drawn, from its anchor to the pointer, routed as it will be once dropped.</summary>
    public void DrawPendingWire(DrawingContext context)
    {
        if (PendingWireFrom is { } anchor) wire.Draw(context, anchor);
    }

    /// <summary>Shift went down or came up: a carry mid-way shows at once what letting go would do.</summary>
    public void ModifiersChanged(KeyModifiers modifiers)
    {
        if (drag != Drag.Node || history.Locked || LastPointer is not { } over) return;

        module.Aim(over, modifiers);
        repaint.Request();
    }

    /// <summary>Stops a turn of a socket, and puts the cursor back to what the pointer is over.</summary>
    private bool EndTurn(Control canvas, bool restore = false)
    {
        if (!dial.Turning) return false;

        dial.End(canvas, restore);
        canvas.Cursor = LastPointer is { } over ? CursorOver(over) : ArrowCursor;

        return true;
    }

    /// <summary>
    /// What the pointer should look like over <paramref name="graph"/>. A box and the
    /// strip above an open group answer as a module does, being taken hold of as one.
    /// </summary>
    private Cursor CursorOver(Point graph)
    {
        var scene = selection.Scene;

        if (scene.HitPort(graph, out _, out _, out _)) return PortCursor;

        var draggable = scene.HitNode(graph) is not null
            || scene.HitBox(graph) is not null
            || scene.HitOpenGroupHandle(graph) is not null;

        return draggable ? NodeCursor : ArrowCursor;
    }
}
