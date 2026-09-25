using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Flyback.Core.Graph;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App.Canvas;

/// <summary>
/// The hand on the canvas: what a press, a drag, a release and a wheel turn do. Five
/// gestures over one <see cref="Drag"/> state, whose fields are only read while their
/// own gesture is the one under way.
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
    /// While the middle button is dragging the view. A hand rather than the four-way
    /// arrow a module gets, because what is moving is not in the patch.
    /// </summary>
    private static readonly Cursor PanCursor = new(StandardCursorType.Hand);

    /// <summary>
    /// The rubber band. Dashed, because it is a gesture in progress rather than anything
    /// in the patch, and drawn over the canvas so it keeps its size at any zoom.
    /// </summary>
    private static readonly IPen MarqueePen = new Pen(
        new SolidColorBrush(Colors.Attention),
        1,
        new DashStyle([4, 3], 0));

    private static readonly IBrush MarqueeFill = new SolidColorBrush(Colors.Attention, 0.08);

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
    private readonly NodeGeometry geometry;

    private Drag drag;

    /// <summary>
    /// Where on the canvas a module drag took hold, in the patch's own coordinates, so a
    /// module stays in the hand through a pan and a zoom mid-drag.
    /// </summary>
    private Point dragOrigin;

    /// <summary>
    /// What the middle button put on hold to pan, and where the pan started.
    /// <see cref="Drag.None"/> when the pan has nothing under it.
    /// </summary>
    private Drag panSuspended = Drag.None;
    private Point panOrigin;

    /// <summary>
    /// Where each module of the selection was when the drag began, so a drag ending
    /// where it started can be told from one that moved.
    /// </summary>
    private readonly Dictionary<Guid, Point> dragOrigins = [];

    /// <summary>
    /// A module pressed while already part of a larger selection, which cannot be
    /// resolved until the button comes up: pressing must not narrow the selection, or a
    /// set could never be dragged by one of its members; releasing without a drag must.
    /// </summary>
    private Guid? pendingNarrow;

    /// <summary>The two corners of the rubber band, in graph space, so it stays over the same modules at any zoom.</summary>
    private Point marqueeFrom;
    private Point marqueeTo;

    /// <summary>
    /// What the rubber band adds to: what was selected when it began with the modifier
    /// held, and nothing otherwise. Held apart, so sweeping back off a module takes it
    /// out again.
    /// </summary>
    private readonly HashSet<Guid> marqueeBase = [];

    /// <summary>What was selected when the rubber band began, which backing out of one puts back.</summary>
    private readonly HashSet<Guid> marqueeWas = [];

    private Guid wireNode;
    private int wirePort;
    private bool wireFromOutput;

    /// <summary>
    /// Which re-patch this is. Unplugging an input and plugging it in elsewhere is two
    /// edits and one gesture, so both carry this and fold into one step.
    /// </summary>
    private int wireGesture;

    private Point wireEnd;

    /// <summary>The wire this re-patch picked up and where in the patch's list it was, or null for a new wire.</summary>
    private (Connection Wire, int At)? lifted;

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
        NodeGeometry geometry)
    {
        this.history = history;
        this.geometry = geometry;
        this.selection = selection;
        this.view = view;
        this.edits = edits;
        this.dial = dial;
        this.held = held;
        this.linking = linking;
        this.marks = marks;
        this.tips = tips;
        this.repaint = repaint;

        history.Replaced += (_, _) => End();
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

    /// <summary>A gesture has ended, and <see cref="Gesturing"/> is false again.</summary>
    public event EventHandler? GestureFinished;

    /// <summary>
    /// Whether a module is being moved, a wire drawn, the view panned or a band drawn
    /// out. The shell holds back moving the canvas while one is.
    /// </summary>
    public bool Gesturing => drag != Drag.None;

    /// <summary>Whether the selection is being carried, so its wires are drawn over everything else.</summary>
    public bool Carrying => drag == Drag.Node;

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
    public Point? PendingWireFrom
    {
        get
        {
            if (drag != Drag.Wire) return null;
            if (history.Patch.Find(wireNode) is not { } node) return null;
            if (NodeCatalog.Get(node.TypeId) is not { } def) return null;

            var scene = selection.Scene;

            return wireFromOutput ? scene.OutputAnchor(node, wirePort) : scene.InputAnchor(node, def, wirePort);
        }
    }

    private string WireGesture => $"wire {wireGesture}";

    /// <summary>Opens the palette where the pointer last was, or in the middle of the view.</summary>
    public void RequestMenu() => MenuRequested?.Invoke(this, LastPointer ?? view.Middle);

    public void Pressed(Control canvas, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(canvas).Properties;
        var screen = e.GetPosition(canvas);
        var graph = view.ToGraph(screen);
        var scene = selection.Scene;

        dragOrigin = graph;

        // Panning is the middle button and nothing else (ADR-0046). It pans
        // mid-gesture too: whatever was under way is put on hold and picks back up
        // once the button comes up.
        if (properties.IsMiddleButtonPressed)
        {
            if (drag != Drag.Pan) panSuspended = drag;
            drag = Drag.Pan;
            panOrigin = screen;
            canvas.Cursor = PanCursor;
            e.Pointer.Capture(canvas);
            return;
        }

        if (properties.IsRightButtonPressed)
        {
            // Over an unpatched input, the button held down is a knob for its value.
            if (drag == Drag.None && dial.Start(canvas, graph, screen))
            {
                tips.Down(canvas);
                e.Pointer.Capture(canvas);
                e.Handled = true;
                return;
            }

            // Not over a module, where a right-click is about that module, and not on
            // a locked canvas, where the next evaluation would take it straight off.
            if (!history.Locked
                && !scene.HitPort(graph, out _, out _, out _)
                && scene.HitBox(graph) is null
                && scene.HitNode(graph) is null)
                MenuRequested?.Invoke(this, graph);

            // Over a module or a shut box, the button is held to flip it.
            if (!scene.HitPort(graph, out _, out _, out _))
            {
                if (scene.HitBox(graph) is { } shut) held.Hold(shut.Members);
                else if (scene.HitNode(graph) is { } under) held.Hold([under.Id]);

                if (held.Holding) e.Pointer.Capture(canvas);
            }

            return;
        }

        if (!properties.IsLeftButtonPressed) return;

        // Over a module Ctrl adds to the selection; over an output it lifts the wire off.
        var ctrl = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;

        // A press outside the box being looked into puts it back, and goes on to be
        // whatever press it was. A socket keeps it, so a wire can be drawn in.
        if (selection.Peeked is not null && !scene.Covered(graph) && !scene.HitPort(graph, out _, out _, out _))
        {
            selection.EndPeek();
            scene = selection.Scene;
        }

        if (linking.Pick(graph))
        {
            e.Handled = true;
            return;
        }

        // A socket on a locked canvas is not a handle, so the press falls through to
        // the module under it.
        if (!history.Locked && scene.HitPort(graph, out var portNode, out var portIndex, out var isOutput))
        {
            StartWire(portNode, portIndex, isOutput, lifting: ctrl, graph);
            e.Pointer.Capture(canvas);
            repaint.Request();
            return;
        }

        if (marks.At(graph) is var (wire, at))
        {
            marks.Splice(wire, at);
            e.Handled = true;
            return;
        }

        // A box before a module, because that is the order they are painted in.
        if (scene.HitBox(graph) is null && scene.HitNode(graph) is { } node)
        {
            PressNode(node, ctrl);
            e.Pointer.Capture(canvas);
            repaint.Request();
            return;
        }

        // A double-click on a box looks into it; a single click selects what is inside,
        // which the ordinary drag then moves.
        if (scene.HitBox(graph) is { } box)
        {
            if (e.ClickCount == 2) selection.Peek(box);
            else PressGroup(box, ctrl);

            e.Pointer.Capture(canvas);
            repaint.Request();
            return;
        }

        if (scene.HitOpenGroupHandle(graph) is { } opened)
        {
            if (e.ClickCount == 2 && opened == selection.Peeked) selection.EndPeek();
            // Shutting a group is an edit, so a locked canvas selects it instead.
            else if (e.ClickCount == 2 && !history.Locked) edits.ToggleBox(opened);
            else PressGroup(opened, ctrl);

            e.Pointer.Capture(canvas);
            repaint.Request();
            return;
        }

        // Left on empty canvas draws a rubber band. A band that sweeps nothing selects
        // nothing, which is what a click on empty canvas does.
        marqueeFrom = marqueeTo = graph;

        marqueeBase.Clear();
        if (ctrl) marqueeBase.UnionWith(selection.Ids);

        marqueeWas.Clear();
        marqueeWas.UnionWith(selection.Ids);

        drag = Drag.Marquee;
        Sweep();

        e.Pointer.Capture(canvas);
        repaint.Request();
    }

    public void Moved(Control canvas, PointerEventArgs e)
    {
        var screen = e.GetPosition(canvas);
        var graph = view.ToGraph(screen);

        LastPointer = graph;

        if (dial.Turning)
        {
            dial.Move(canvas, screen, e.KeyModifiers);
            return;
        }

        // The middle button's own state, sampled here: a second button going down while
        // the first is captured does not reliably raise a press of its own.
        var middleDown = e.GetCurrentPoint(canvas).Properties.IsMiddleButtonPressed;

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

            // Pressed to select, which a locked canvas does, and then held, which it does not.
            case Drag.Node when history.Locked:
                return;

            case Drag.Node when dragOrigins.Count > 0:
                var delta = selection.Scene.Held(graph - dragOrigin, dragOrigins);

                foreach (var moving in selection.Nodes)
                {
                    if (!dragOrigins.TryGetValue(moving.Id, out var from)) continue;

                    moving.X = from.X + delta.X;
                    moving.Y = from.Y + delta.Y;
                }

                repaint.Request();
                return;

            case Drag.Marquee:
                marqueeTo = graph;
                Sweep();
                repaint.Request();
                return;

            case Drag.Wire:
                wireEnd = graph;
                repaint.Request();
                return;

            default:
                canvas.Cursor = CursorOver(graph);
                tips.Over(canvas, graph);
                marks.Hover(graph);
                return;
        }
    }

    public void Released(Control canvas, PointerReleasedEventArgs e)
    {
        var graph = view.ToGraph(e.GetPosition(canvas));

        if (e.InitialPressMouseButton == MouseButton.Right)
        {
            if (EndTurn(canvas)) e.Pointer.Capture(null);

            held.Release();
            return;
        }

        // The middle button's own release is answered here whatever drag is by now: the
        // gesture it interrupted must not be finished by a button that never held it.
        if (e.InitialPressMouseButton == MouseButton.Middle)
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
                e.Pointer.Capture(null);
                repaint.Request();
            }

            return;
        }

        // The button that began the gesture came up while a pan held it: a module stays
        // where it was carried to, and a wire is dropped, since its end was let go of
        // over a view that was moving.
        if (drag == Drag.Pan && panSuspended == Drag.Node) RecordMove();

        if (drag == Drag.Wire) CompleteWire(graph);

        // A move is a step and nothing the program can hear. A press on one module of a
        // group that turned out not to be a drag was a click, which picks it out.
        if (drag == Drag.Node && !RecordMove() && pendingNarrow is { } one) selection.Select(one);

        End();

        // Taken from where the pointer is, which after a pan is rarely where it was.
        canvas.Cursor = CursorOver(graph);

        e.Pointer.Capture(null);
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

        if (drag == Drag.Node || panSuspended == Drag.Node) RecordMove();

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
            // Back into its place in the list, under the name the lifting was recorded
            // under, so the history sees the patch it began with and drops the step.
            case Drag.Wire when lifted is { } was:
                lifted = null;
                history.Patch.Connections.Insert(Math.Min(was.At, history.Patch.Connections.Count), was.Wire);
                history.Record(WireGesture);
                break;

            case Drag.Node:
                foreach (var moving in selection.Nodes)
                {
                    if (!dragOrigins.TryGetValue(moving.Id, out var from)) continue;

                    moving.X = from.X;
                    moving.Y = from.Y;
                }

                break;

            case Drag.Marquee:
                selection.Replace(marqueeWas);
                selection.Refocus();
                selection.Announce();
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

        pendingNarrow = null;
        dragOrigins.Clear();
        marqueeBase.Clear();
        marqueeWas.Clear();

        if (ended) GestureFinished?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The rubber band, over the canvas rather than in it, so its hairline and dashes hold at any zoom.</summary>
    public void DrawMarquee(DrawingContext context)
    {
        if (drag != Drag.Marquee) return;

        var band = CanvasScene.Band(
            view.GraphToScreen.Transform(marqueeFrom),
            view.GraphToScreen.Transform(marqueeTo));

        // A band with no width or height is a click that has not moved yet.
        if (band.Width < 1 || band.Height < 1) return;

        context.DrawRectangle(MarqueeFill, MarqueePen, band);
    }

    /// <summary>The wire being drawn, from its anchor to the pointer, routed as it will be once dropped.</summary>
    public void DrawPendingWire(DrawingContext context)
    {
        if (PendingWireFrom is not { } anchor) return;

        var pen = new Pen(new SolidColorBrush(Colors.Attention, 0.9), 2.2, DashStyle.Dash);

        // Handed over in whichever order makes the curve leave an output and arrive at an input.
        var (from, to) = wireFromOutput ? (anchor, wireEnd) : (wireEnd, anchor);

        if (from.X <= to.X)
        {
            WirePath.Draw(context, from, to, pen);
            return;
        }

        // The pointer is a module of no size.
        var holding = history.Patch.Find(wireNode) is { } node && NodeCatalog.Get(node.TypeId) is { } def
            ? geometry.Bounds(node, def)
            : new Rect(anchor, anchor);

        WirePath.DrawReturn(context, from, to, WirePath.ReturnRun(holding, new Rect(wireEnd, wireEnd)), pen);
    }

    /// <summary>Stops a turn of a socket, and puts the cursor back to what the pointer is over.</summary>
    private bool EndTurn(Control canvas, bool restore = false)
    {
        if (!dial.Turning) return false;

        dial.End(canvas, restore);
        canvas.Cursor = LastPointer is { } over ? CursorOver(over) : ArrowCursor;

        return true;
    }

    /// <summary>Selects what the rubber band is over, together with whatever it was told to keep.</summary>
    private void Sweep()
    {
        var wanted = new HashSet<Guid>(marqueeBase);

        wanted.UnionWith(selection.Scene.Swept(CanvasScene.Band(marqueeFrom, marqueeTo)));

        // Only when it changed: this runs on every move, and the inspector is rebuilt
        // whenever a selection is announced.
        if (wanted.Count == selection.Count && wanted.All(selection.Contains)) return;

        selection.Replace(wanted);
        selection.FocusTop();
        selection.Announce();
    }

    /// <summary>What a press on a module does to the selection, and the start of a drag of what that leaves selected.</summary>
    private void PressNode(NodeInstance node, bool adding)
    {
        pendingNarrow = null;

        if (adding) selection.Toggle(node.Id);
        else if (!selection.Contains(node.Id)) selection.Select(node.Id);
        else
        {
            pendingNarrow = node.Id;

            // The panel follows the pointer even when the set does not.
            selection.FocusOn(node.Id);
        }

        StartCarrying();
    }

    /// <summary>A press on a box, or on the strip above an open group: what is inside is what gets selected.</summary>
    private void PressGroup(NodeGroup group, bool adding)
    {
        pendingNarrow = null;

        if (adding) selection.Take([.. selection.Ids, .. group.Members], group.Members[^1]);
        else selection.Take(group.Members);

        selection.Announce();

        StartCarrying();
    }

    /// <summary>Brings the selection to the front together and notes where each module started.</summary>
    private void StartCarrying()
    {
        // Nodes paint in list order, so a group being dragged does not pass under
        // members of itself.
        foreach (var moving in selection.Nodes)
        {
            history.Patch.Nodes.Remove(moving);
            history.Patch.Nodes.Add(moving);
        }

        drag = Drag.Node;

        dragOrigins.Clear();
        foreach (var moving in selection.Nodes)
            dragOrigins[moving.Id] = new Point(moving.X, moving.Y);
    }

    /// <summary>
    /// Grabbing a connected socket picks the existing wire up by the end that was not
    /// grabbed, so re-patching works the way it does on a real rig.
    /// </summary>
    /// <param name="lifting">
    /// Whether Ctrl was held, which only matters on an output: an output holds any
    /// number of wires, so reaching for the one already there asks for the modifier,
    /// and only where exactly one leaves the socket.
    /// </param>
    private void StartWire(Guid nodeId, int portIndex, bool isOutput, bool lifting, Point graph)
    {
        var patch = history.Patch;

        wireGesture++;
        lifted = null;

        // Before a wire is lifted off, so whatever hears of that change already sees a
        // gesture under way.
        drag = Drag.Wire;
        wireEnd = graph;

        if (!isOutput && patch.IncomingTo(nodeId, portIndex) is { } existing)
        {
            lifted = (existing, patch.Connections.IndexOf(existing));
            patch.Disconnect(nodeId, portIndex);
            wireNode = existing.SourceNode;
            wirePort = existing.SourcePort;
            wireFromOutput = true;
            history.Record(WireGesture);
        }
        else if (isOutput && lifting && patch.SoleOutgoingFrom(nodeId, portIndex) is { } sole)
        {
            // The mirror of the case above: the wire comes off the socket grabbed and
            // stays in the one at its far end, so what changes is where the signal
            // comes from while what it feeds stays put.
            lifted = (sole, patch.Connections.IndexOf(sole));
            patch.Disconnect(sole.TargetNode, sole.TargetPort);
            wireNode = sole.TargetNode;
            wirePort = sole.TargetPort;
            wireFromOutput = false;
            history.Record(WireGesture);
        }
        else
        {
            wireNode = nodeId;
            wirePort = portIndex;
            wireFromOutput = isOutput;
        }
    }

    private void CompleteWire(Point graph)
    {
        var patch = history.Patch;
        var scene = selection.Scene;

        if (!scene.HitPort(graph, out var node, out var port, out var isOutput))
        {
            // Dropped on a module's body it is a miss; on bare canvas it asks for
            // something to plug into.
            if (scene.HitNode(graph) is null) OfferSomethingToPlugInto(graph);

            return;
        }

        // A wire only means something between opposite kinds of socket.
        if (isOutput == wireFromOutput) return;

        var (sourceNode, sourcePort, targetNode, targetPort) = wireFromOutput
            ? (wireNode, wirePort, node, port)
            : (node, port, wireNode, wirePort);

        // A wire that closes a loop is drawn like any other and carries the previous
        // evaluation (ADR-0075), so there is nothing to put on it.
        patch.Connect(sourceNode, sourcePort, targetNode, targetPort);

        // Put straight back where it was lifted from, into its old place in the list,
        // so the history sees the patch the gesture began with and drops the step.
        if (lifted is { } was
            && was.Wire == new Connection(sourceNode, sourcePort, targetNode, targetPort)
            && patch.Connections.Remove(was.Wire))
        {
            patch.Connections.Insert(Math.Min(was.At, patch.Connections.Count), was.Wire);
        }

        history.Record(WireGesture);
    }

    /// <summary>A wire let go over bare canvas, handed to whoever can offer something to plug it into.</summary>
    private void OfferSomethingToPlugInto(Point graph)
    {
        if (history.Patch.Find(wireNode) is not { } holding) return;
        if (NodeCatalog.Get(holding.TypeId) is not { } def) return;

        var sockets = wireFromOutput ? def.Outputs : def.Inputs;
        if (wirePort < 0 || wirePort >= sockets.Count) return;

        WireDropped?.Invoke(this, new WireDrop(graph, wireNode, wirePort, wireFromOutput, sockets[wirePort].Kind));
    }

    /// <summary>Puts a drag that moved something into the history, and says whether it did.</summary>
    private bool RecordMove()
    {
        var moved = selection.Nodes.Any(node =>
            dragOrigins.TryGetValue(node.Id, out var from)
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            && (node.X != from.X || node.Y != from.Y));

        return moved && history.RecordMove();
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
