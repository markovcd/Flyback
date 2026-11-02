using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Flyback.Core.Graph;
using Flyback.Editor.Notices;

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
    private static readonly Cursor WellCursor = new(StandardCursorType.SizeNorthSouth);

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
    private readonly SocketFocus focus;
    private readonly KnobChoice choice;
    private readonly StepBar bar;
    private readonly HeldModules held;
    private readonly KnobLinking linking;
    private readonly RemapMarks marks;
    private readonly CanvasTips tips;
    private readonly Repaint repaint;
    private readonly ModuleDrag module;
    private readonly WireDrag wire;
    private readonly RubberBand band;
    private readonly LevelShelves shelves;

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
        SocketFocus focus,
        KnobChoice choice,
        StepBar bar,
        HeldModules held,
        KnobLinking linking,
        RemapMarks marks,
        CanvasTips tips,
        Repaint repaint,
        ModuleDrag module,
        WireDrag wire,
        RubberBand band,
        LevelShelves shelves,
        Reactions reactions)
    {
        this.shelves = shelves;
        this.history = history;
        this.reactions = reactions;
        this.selection = selection;
        this.view = view;
        this.edits = edits;
        this.dial = dial;
        this.focus = focus;
        this.choice = choice;
        this.bar = bar;
        this.held = held;
        this.linking = linking;
        this.marks = marks;
        this.tips = tips;
        this.repaint = repaint;
        this.module = module;
        this.wire = wire;
        this.band = band;

        history.Replaced += (_, _) =>
        {
            End();
            focus.Clear();
            choice.Close();
        };
        wire.Dropped += (_, drop) => WireDropped?.Invoke(this, drop);
    }

    /// <summary>
    /// A finger's tap on a value's well, which gives it the keys and shows the step bar;
    /// a second tap opens it for typing. Whether the tap was the well's.
    /// </summary>
    public bool Tapped(Point screen, int taps)
    {
        if (drag != Drag.None || choice.For is not null || bar.ButtonAt(screen) is not null) return false;
        if (view.Overview || !selection.Scene.HitWell(view.ToGraph(screen), Math.Min(Fingers.Reach / view.Zoom, Fingers.MostReach), out var node, out var port)) return false;

        if (!selection.Contains(node)) selection.Select(node);

        focus.Pick(new SocketPick(node, port), tapped: true);
        if (taps == 2) focus.BeginTyping();

        return true;
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

    /// <summary>Whether a value is being turned.</summary>
    public bool Turning => dial.Turning;

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

        // The list of knobs takes a click on one of its lines, and shuts at any other press.
        if (choice.For is not null)
        {
            if (button == MouseButton.Left && choice.RowAt(screen) is { } line)
            {
                choice.Choose(line);
                return true;
            }

            choice.Close();
        }

        if (button == MouseButton.Left && bar.ButtonAt(screen) is { } stepped)
        {
            bar.Press(stepped);
            return true;
        }

        // A press anywhere takes the keys back from a well; one on a well gives them again.
        focus.Clear();

        var press = new Press(canvas, pointer, screen, graph, modifiers, clickCount);

        return button switch
        {
            MouseButton.Right => PressRight(press, scene),
            MouseButton.Left => PressLeft(press, scene),
            _ => false,
        };
    }

    /// <summary>
    /// One press as the handlers below take it: where it landed, on screen and on the
    /// canvas, and with which keys held and how many clicks.
    /// </summary>
    private readonly record struct Press(
        Control Canvas, IPointer? Pointer, Point Screen, Point Graph, KeyModifiers Modifiers, int ClickCount)
    {
        /// <summary>Over a module Ctrl adds to the selection; over an output it lifts a wire off.</summary>
        public bool Ctrl => (Modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;

        public bool Touch => Pointer?.Type == PointerType.Touch;
    }

    /// <summary>The right button: a value's knob, a rubber band, the list, or a module held to flip it.</summary>
    private bool PressRight(Press press, CanvasScene scene)
    {
        var (canvas, pointer, screen, graph) = (press.Canvas, press.Pointer, press.Screen, press.Graph);
        var onPort = scene.HitPort(graph, out _, out _, out _);

        // Over an unpatched input, the button held down is a knob for its value. A finger
        // held anywhere on a module turns the input on the row nearest it, and zoomed out
        // anything does, for its first input; Tab or a second finger moves on.
        var anchored = !press.Touch;

        if (drag == Drag.None
            && (dial.Start(canvas, graph, screen, anchored)
                || ((view.Overview || press.Touch) && !onPort
                    && dial.StartOnBody(canvas, graph, screen, anchored, nearest: press.Touch && !view.Overview))))
        {
            tips.Down(canvas);
            pointer?.Capture(canvas);
            return true;
        }

        var bare = !onPort
            && scene.HitBox(graph) is null
            && scene.HitNode(graph) is null;

        // Dragged, it is a rubber band; let go where it went down, the list opens.
        if (DragToPan && !press.Touch && bare && drag == Drag.None)
        {
            StartMarquee(graph, press.Ctrl, MouseButton.Right);
            stillAt = screen;
            pointer?.Capture(canvas);
            return false;
        }

        // Not over a module, where a right-click is about that module, and not on
        // a locked canvas, where the next evaluation would take it straight off.
        if (!history.Locked && bare) MenuRequested?.Invoke(this, graph);

        // Over a module or a shut box, the button is held to flip it. Not a finger's,
        // whose hold on a module turns a value; the inspector switches it off.
        if (!onPort && !press.Touch)
        {
            if (scene.HitBox(graph) is { } shut) held.Hold(shut.Members);
            else if (scene.HitNode(graph) is { } under)
                held.Hold(selection.Contains(under.Id) ? selection.Nodes.Select(n => n.Id) : [under.Id]);

            if (held.Holding) pointer?.Capture(canvas);
        }

        return false;
    }

    /// <summary>
    /// The left button on what a value or a wire is handled by: a well, a knob chip, a
    /// socket or a remap mark. Past those, the module, box or bare canvas under it.
    /// </summary>
    private bool PressLeft(Press press, CanvasScene scene)
    {
        var (canvas, pointer, graph) = (press.Canvas, press.Pointer, press.Graph);
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

        // A value's well turns it with the left button, as the right does on the socket.
        // A finger holds first, through Fingers, so a swipe across a module still moves it.
        if (!press.Touch && drag == Drag.None && !view.Overview
            && scene.HitWell(graph, out var wellNode, out var wellPort))
        {
            if (!selection.Contains(wellNode)) selection.Select(wellNode);

            if ((press.Modifiers & KeyModifiers.Alt) != 0) return dial.Reset(wellNode, wellPort);

            // A double-click opens it for typing, the first click having given it the keys.
            if (press.ClickCount == 2)
            {
                focus.Pick(new SocketPick(wellNode, wellPort));
                return focus.BeginTyping();
            }

            if (!dial.Start(canvas, wellNode, wellPort, press.Screen)) return false;

            tips.Down(canvas);
            pointer?.Capture(canvas);
            return true;
        }

        // A linked socket's chip drops the list of knobs, to pick another or unlink.
        if (!history.Locked && drag == Drag.None && !view.Overview && scene.HitChip(graph, out var chipNode, out var chipPort))
        {
            if (!selection.Contains(chipNode)) selection.Select(chipNode);

            choice.Open(new SocketPick(chipNode, chipPort));
            return true;
        }

        // A socket on a locked canvas is not a handle, so the press falls through to
        // the module under it.
        if (!history.Locked && scene.HitPort(graph, out var portNode, out var portIndex, out var isOutput))
        {
            // Before a wire is lifted off, so whatever hears of that change already sees a
            // gesture under way.
            drag = Drag.Wire;
            wire.Start(portNode, portIndex, isOutput, lifting: press.Ctrl, liftedLast, graph);
            pointer?.Capture(canvas);
            repaint.Request();
            return false;
        }

        if (marks.At(graph) is var (marked, at))
        {
            marks.Splice(marked, at);
            return true;
        }

        return PressBody(press, scene) ?? PressBare(press);
    }

    /// <summary>
    /// The left button on a module, a shelf's chip, a shut box or an open group's strip,
    /// and whether it was answered in full; null over none of them.
    /// </summary>
    private bool? PressBody(Press press, CanvasScene scene)
    {
        var (canvas, pointer, graph, clickCount) = (press.Canvas, press.Pointer, press.Graph, press.ClickCount);

        // A box before a module, because that is the order they are painted in.
        if (scene.HitBox(graph) is null && scene.HitNode(graph) is { } node)
        {
            // Zoomed out past reading, a double-click brings the module up to a size that reads.
            if (clickCount == 2 && view.Overview)
            {
                view.FrameOn(scene.Footprint(node));
                return true;
            }

            // Shift on one member of a group selected whole means that one: it is about
            // to be carried out, and the whole group would ride along as itself.
            if ((press.Modifiers & KeyModifiers.Shift) != 0 && selection.Group is { } whole && whole.Members.Contains(node.Id))
                selection.Select(node.Id);

            module.PressModule(node, press.Ctrl);
            drag = Drag.Node;
            if (!history.Locked) module.Aim(graph, press.Modifiers);

            pointer?.Capture(canvas);
            repaint.Request();
            return false;
        }

        // A shelf's chip selects the level it names, as the inspector's path does.
        if (shelves.Hit(history.Patch, scene, graph) is { } shelf)
        {
            selection.Take(shelf.Members);
            selection.Announce();
            return false;
        }

        // A double-click on a box looks into it; a single click selects what is inside,
        // which the ordinary drag then moves.
        if (scene.HitBox(graph) is { } box)
        {
            if (clickCount == 2) selection.Peek(box);
            else PressGroup(box, press.Ctrl);

            pointer?.Capture(canvas);
            repaint.Request();
            return false;
        }

        if (scene.HitOpenGroupHandle(graph) is { } opened)
        {
            if (clickCount == 2 && opened == selection.Peeked) selection.EndPeek();
            // Shutting a group is an edit, so a locked canvas selects it instead.
            else if (clickCount == 2 && !history.Locked) edits.ToggleBox(opened);
            else PressGroup(opened, press.Ctrl);

            pointer?.Capture(canvas);
            repaint.Request();
            return false;
        }

        return null;
    }

    /// <summary>The left button on empty canvas: a pan where a drag pans, otherwise a rubber band.</summary>
    private bool PressBare(Press press)
    {
        var (canvas, pointer, screen, graph) = (press.Canvas, press.Pointer, press.Screen, press.Graph);

        // A double-click on empty canvas looks at the whole patch again, and is still a click there.
        if (press.ClickCount == 2) view.FrameAll();

        if (DragToPan && !press.Touch)
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
        StartMarquee(graph, press.Ctrl, MouseButton.Left);
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
                dial.Hover(graph);
                return;
        }
    }

    /// <summary>A lifted finger hovers over nothing, so what it brought up comes down.</summary>
    public void Lifted(Control canvas) => tips.Down(canvas);

    /// <summary>The pointer went off the canvas, taking its hover with it.</summary>
    public void Exited(Control canvas)
    {
        tips.Down(canvas);
        dial.Unhover();
    }

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

        // A press on a well that let go without turning it was a click, which gives it the keys.
        if (button == MouseButton.Left && dial.Dialed is { } dialed)
        {
            var clicked = !dial.Moved;

            EndTurn(canvas);
            pointer?.Capture(null);

            if (clicked) focus.Pick(dialed);
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

    /// <summary>
    /// Whether a scroll with neither Ctrl nor Cmd held pans the view rather than zooming it:
    /// a Mac's convention, where the wheel is usually two fingers on a trackpad and a pinch zooms.
    /// </summary>
    public bool ScrollPans { get; set; }

    /// <summary>How many screen pixels a notch of the wheel pans, the step a scroll viewer takes.</summary>
    internal const double PanPerNotch = 50;

    /// <summary>
    /// How long, in milliseconds, the wheel rests before a scroll that was moving the view is over,
    /// which a trackpad's momentum runs on well inside.
    /// </summary>
    internal const ulong ScrollRest = 250;

    /// <summary>When the scroll moving the view last turned the wheel; null while none is.</summary>
    private ulong? viewScrolledAt;

    public void Wheel(Control canvas, PointerWheelEventArgs e)
    {
        e.Handled = true;

        // A scroll that is moving the view goes on moving it until it rests: a well that slides
        // under the pointer is not nudged by it, nor is a turn begun under its momentum switched.
        if (viewScrolledAt is { } last && e.Timestamp >= last && e.Timestamp - last <= ScrollRest)
        {
            viewScrolledAt = e.Timestamp;
            if (!dial.Turning) MoveView(canvas, e);
            return;
        }

        viewScrolledAt = null;

        // While a value turns the wheel picks which; over the well with the keys it nudges
        // it; over anything else it pans or zooms.
        if (dial.SwitchBy(e.Delta.Y)) return;

        var over = selection.Scene.HitWell(view.ToGraph(e.GetPosition(canvas)), out var node, out var port);

        if (over && focus.Focused == new SocketPick(node, port) && focus.Wheel(e.Delta.Y)) return;

        viewScrolledAt = e.Timestamp;
        MoveView(canvas, e);
    }

    /// <summary>Pans the view by a scroll, or zooms it about the pointer.</summary>
    private void MoveView(Control canvas, PointerWheelEventArgs e)
    {
        if (ScrollPans && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0)
            view.PanBy(e.Delta * PanPerNotch);
        else
            view.ZoomAt(e.GetPosition(canvas), e.Delta.Y);
    }

    /// <summary>A pinch on a touchpad, which zooms about the pointer by as much as the fingers spread.</summary>
    public void Magnify(Control canvas, PointerDeltaEventArgs e)
    {
        e.Handled = true;

        // A magnification of -1 or less would be a zoom to nothing.
        if (e.Delta.X > -0.9) view.ZoomBy(e.GetPosition(canvas), 1 + e.Delta.X);
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

    /// <summary>Moves the turn under way to the next input on its module that turns, or the one before for a negative <paramref name="step"/>.</summary>
    public bool SwitchTurn(int step) => dial.Switch(step);

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

        if (scene.HitWell(graph, out _, out _)) return WellCursor;
        if (!history.Locked && scene.HitChip(graph, out _, out _)) return PanCursor;

        var draggable = scene.HitNode(graph) is not null
            || scene.HitBox(graph) is not null
            || scene.HitOpenGroupHandle(graph) is not null;

        return draggable ? NodeCursor : ArrowCursor;
    }
}
