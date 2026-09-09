using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// The hand and the keyboard: what a press, a drag, a release, a wheel turn and
/// a key press do.
/// </summary>
/// <remarks>
/// Five gestures over one <c>Drag</c> state, and the fields behind it are only
/// ever read while their own gesture is the one under way — each is cleared by
/// the press that starts it rather than trusted to have been left empty.
/// </remarks>
public sealed partial class NodeEditor
{
    // --- interaction ---------------------------------------------------------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var properties = e.GetCurrentPoint(this).Properties;
        var screen = e.GetPosition(this);
        var graph = ToGraph(screen);
        dragOrigin = screen;

        // Panning is the middle button and nothing else: the right one opens
        // the module list instead — ADR-0046 — because a button cannot both
        // open something on a click and stay silent for one.
        if (properties.IsMiddleButtonPressed)
        {
            drag = Drag.Pan;
            Cursor = PanCursor;
            e.Pointer.Capture(this);
            return;
        }

        if (properties.IsRightButtonPressed)
        {
            // Not over a module: a right-click there is about that module rather
            // than about adding another one beside it. And nowhere at all on a
            // locked canvas, where the list would offer to place something the
            // next evaluation would take straight back off.
            if (!Locked && !HitPort(graph, out _, out _, out _) && HitNode(graph) is null)
                MenuRequested?.Invoke(this, graph);

            return;
        }

        if (!properties.IsLeftButtonPressed) return;

        // Ctrl means two things, and which one depends entirely on what is under
        // the pointer: over a module it adds to the selection, over an output it
        // lifts the wire off. They never meet — a press is over one or the other
        // — so one modifier serves both without either having to know.
        var ctrl = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;

        // A socket on a locked canvas is not a handle. Falls through to the
        // module under it, so a press on a port still selects the module — which
        // is what somebody reading a patch was reaching for anyway.
        if (!Locked && HitPort(graph, out var portNode, out var portIndex, out var isOutput))
        {
            StartWire(portNode, portIndex, isOutput, lifting: ctrl, graph);
            e.Pointer.Capture(this);
            InvalidateVisual();
            return;
        }

        if (HitNode(graph) is { } node)
        {
            PressNode(node, ctrl);
            e.Pointer.Capture(this);
            InvalidateVisual();
            return;
        }

        // A box, or the strip above an open group. Both answer a double-click by
        // changing which of the two the group is; a single click on a box selects
        // what is inside it, which is what makes dragging one work without a drag
        // of its own — the modules are selected, so the ordinary group drag moves
        // them and the box follows because it is drawn from where they are.
        if (HitBox(graph) is { } box)
        {
            if (e.ClickCount == 2) ToggleBox(box);
            else PressGroup(box, ctrl);

            e.Pointer.Capture(this);
            InvalidateVisual();
            return;
        }

        if (HitOpenGroupHandle(graph) is { } opened)
        {
            if (e.ClickCount == 2) ToggleBox(opened);
            else PressGroup(opened, ctrl);

            e.Pointer.Capture(this);
            InvalidateVisual();
            return;
        }

        // Left on empty canvas draws a rubber band. Panning is the middle and
        // right buttons, which is where it was already and where it stays.
        //
        // Nothing is deselected here: a band that sweeps nothing ends by
        // selecting nothing, which is the same thing a click on empty canvas
        // always did, and it arrives through the one path rather than two.
        marqueeFrom = marqueeTo = graph;

        marqueeBase.Clear();
        if (ctrl) marqueeBase.UnionWith(selection);

        drag = Drag.Marquee;
        Sweep();

        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    /// <summary>
    /// Selects what the rubber band is currently over, together with whatever it
    /// was told to keep.
    /// </summary>
    /// <remarks>
    /// A module counts as swept when the band touches it rather than when it
    /// swallows it whole. Touching is the more forgiving of the two and it is
    /// what the gesture looks like it should do — dragging across a row of
    /// modules takes the row, without having to reach past the ends of it.
    /// </remarks>
    private void Sweep()
    {
        var band = Band(marqueeFrom, marqueeTo);
        var wanted = new HashSet<Guid>(marqueeBase);

        foreach (var node in patch.Nodes)
            if (!Shut(node.Id)
                && NodeCatalog.Get(node.TypeId) is { } def
                && NodeGeometry.Bounds(node, def).Intersects(band))
                wanted.Add(node.Id);

        // A box is swept as the modules it stands for, all of them together and
        // none of them without the rest. Sweeping half a box would select a
        // module the band never touched — the one under it — which is the same
        // reason a module under a box does not answer a click.
        foreach (var (group, _, bounds) in Boxes())
            if (bounds.Intersects(band))
                wanted.UnionWith(group.Members);

        // Only when it actually changed. This runs on every pointer move, and
        // the inspector is rebuilt from scratch whenever a selection is
        // announced — saying so sixty times a second for a band moving across
        // empty canvas would be sixty rebuilds of the same panel.
        if (wanted.Count == selection.Count && wanted.All(selection.Contains)) return;

        selection.Clear();
        selection.UnionWith(wanted);

        // The last in the patch's own order, which is the module drawn on top —
        // the same rule SelectAll and Toggle use.
        focus = patch.Nodes.LastOrDefault(node => selection.Contains(node.Id))?.Id;

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// What a press on a module does to the selection, and the start of a drag
    /// of whatever that leaves selected.
    /// </summary>
    /// <remarks>
    /// The awkward case is a plain press on a module already in a larger
    /// selection, and it cannot be answered here: collapsing to it would make a
    /// group impossible to drag by one of its own members, and not collapsing
    /// would make one impossible to pick apart. So it is deferred to the release
    /// — see <see cref="pendingNarrow"/> — which is the same answer every
    /// editor that has this problem arrives at.
    /// </remarks>
    /// <summary>
    /// Ends whatever gesture was under way, and forgets what it was holding.
    /// </summary>
    /// <remarks>
    /// Each field behind <c>drag</c> belongs to one gesture and is cleared by the
    /// press that starts that gesture, so what is forgotten here is already out of
    /// reach — every one of them is read only while its own gesture is the one under
    /// way. It is written down all the same, so that ending a gesture is one thing
    /// with one name rather than a list each caller is trusted to have remembered.
    /// </remarks>
    private void EndGesture()
    {
        drag = Drag.None;

        pendingNarrow = null;
        dragOrigins.Clear();
        marqueeBase.Clear();
    }

    private void PressNode(NodeInstance node, bool adding)
    {
        pendingNarrow = null;

        if (adding) Toggle(node.Id);
        else if (!selection.Contains(node.Id)) Select(node.Id);
        else
        {
            pendingNarrow = node.Id;

            // The panel follows the pointer even when the set does not, so
            // pressing one module of a group is how its values are reached.
            if (focus != node.Id)
            {
                focus = node.Id;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        // Everything selected comes to the front together, so a group being
        // dragged does not pass under members of itself.
        foreach (var moving in SelectedNodes) BringToFront(moving);

        drag = Drag.Node;

        dragOrigins.Clear();
        foreach (var moving in SelectedNodes)
            dragOrigins[moving.Id] = new Point(moving.X, moving.Y);
    }

    /// <summary>
    /// As much of a drag as keeps every module in it inside the canvas.
    /// </summary>
    /// <remarks>
    /// The whole gesture is cut back to what the nearest module to an edge can
    /// take, rather than each module being clamped where it lands. Clamping
    /// them one at a time would hold the group together right up until it met
    /// the edge and then flatten it against it — the ones already there stopped
    /// while the rest kept coming — and letting go would leave a selection
    /// nothing puts back. Cut as one vector, the group slides up to the wall
    /// whole and stays in the shape it was picked up in.
    /// <para>
    /// Each axis is narrowed by every module in turn. All of the ranges hold
    /// zero, because a module is inside the canvas before it is dragged, so
    /// there is always some part of the gesture left to allow — standing still
    /// at worst.
    /// </para>
    /// </remarks>
    private Vector Held(Vector delta, Dictionary<Guid, Point> origins)
    {
        var (x, y) = (delta.X, delta.Y);

        foreach (var (id, from) in origins)
        {
            if (patch.Find(id) is not { } node) continue;
            if (NodeCatalog.Get(node.TypeId) is not { } def) continue;

            var room = Room(def);

            x = Math.Clamp(x, room.X - from.X, room.Right - from.X);
            y = Math.Clamp(y, room.Y - from.Y, room.Bottom - from.Y);
        }

        return new Vector(x, y);
    }

    /// <summary>
    /// Where a module's corner may be put, so that the whole of the module is on
    /// the canvas: the canvas less the room the module itself takes up.
    /// </summary>
    /// <remarks>
    /// A coordinate names the top left of a module and the body hangs below and
    /// to the right of it, so holding the coordinate inside the canvas leaves the
    /// body outside — a module dragged to the edge stood entirely on the far side
    /// of the line, which is what the line is drawn to say cannot happen.
    /// </remarks>
    private static Rect Room(NodeDef def) => new(
        CanvasBounds.X,
        CanvasBounds.Y,
        Math.Max(0, CanvasBounds.Width - NodeGeometry.Width),
        Math.Max(0, CanvasBounds.Height - NodeGeometry.Height(def)));

    /// <summary>
    /// Puts every module wholly inside the canvas.
    /// </summary>
    /// <remarks>
    /// The coordinate holds itself to <see cref="NodeInstance.Extent"/> on its
    /// own, and that is the backstop against a module being lost altogether. It
    /// cannot do this part: what it holds is a corner, and how far the body
    /// reaches past that corner depends on how many sockets the module has —
    /// which is the view's arithmetic and not the engine's. So a paste, a layout
    /// or a file may still leave a module standing half off the canvas, and this
    /// is where it is known enough to be put right.
    /// <para>
    /// A module the catalogue does not have is left exactly where it is, the same
    /// as the layout leaves it: nothing here can measure one, and a guessed size
    /// would move it for no reason anybody could see.
    /// </para>
    /// </remarks>
    private void HoldInside()
    {
        foreach (var node in patch.Nodes)
        {
            if (NodeCatalog.Get(node.TypeId) is not { } def) continue;

            var room = Room(def);

            node.X = Math.Clamp(node.X, room.X, room.Right);
            node.Y = Math.Clamp(node.Y, room.Y, room.Bottom);
        }
    }

    /// <summary>
    /// Grabbing a connected socket picks the existing wire up by the end that
    /// was not grabbed, so re-patching works the way it does on a real rig: the
    /// far end stays plugged in and the end in your hand goes somewhere else.
    /// </summary>
    /// <remarks>
    /// Which end that leaves free is the whole of the difference between the two
    /// gestures. Grabbing an input takes the plug out of it, so what is being
    /// chosen is a new input for a signal that keeps its source. Grabbing an
    /// output takes the plug out of <em>that</em>, so what is being chosen is a
    /// new source for a socket that keeps being fed — the question "where should
    /// this come from instead", which nothing here could ask before.
    /// </remarks>
    /// <param name="isOutput"></param>
    /// <param name="lifting">
    /// Whether Ctrl was held, which only matters on an output. An input is
    /// unplugged by being dragged and needs no modifier: it holds one wire, so
    /// grabbing it can only mean that one. An output holds any number, and
    /// dragging from one has always meant "start another" — which is the common
    /// thing to want and cannot be given up. So reaching for the wire that is
    /// already there asks for the modifier, and asks for it only where the answer
    /// is not a guess: exactly one wire leaves the socket.
    /// <para>
    /// With none, or with several, this falls back to starting a new wire —
    /// silently, because a modifier that does nothing is better than a gesture
    /// that picks one of four wires for you.
    /// </para>
    /// </param>
    /// <param name="nodeId"></param>
    /// <param name="portIndex"></param>
    /// <param name="graph"></param>
    private void StartWire(Guid nodeId, int portIndex, bool isOutput, bool lifting, Point graph)
    {
        wireGesture++;

        if (!isOutput && patch.IncomingTo(nodeId, portIndex) is { } existing)
        {
            patch.Disconnect(nodeId, portIndex);
            wireNode = existing.SourceNode;
            wirePort = existing.SourcePort;
            wireFromOutput = true;
            NotifyPatchChanged(WireGesture);
        }
        else if (isOutput && lifting && patch.SoleOutgoingFrom(nodeId, portIndex) is { } sole)
        {
            // The mirror of the case above, and mirrored in every part: the wire
            // comes off the socket that was grabbed and stays in the one at its
            // far end. Grabbing an input keeps the source and looks for a new
            // target; grabbing an output keeps the target and looks for a new
            // source, so what is being changed is where the signal comes from
            // while what it feeds stays put.
            patch.Disconnect(sole.TargetNode, sole.TargetPort);
            wireNode = sole.TargetNode;
            wirePort = sole.TargetPort;
            wireFromOutput = false;
            NotifyPatchChanged(WireGesture);
        }
        else
        {
            wireNode = nodeId;
            wirePort = portIndex;
            wireFromOutput = isOutput;
        }

        drag = Drag.Wire;
        wireEnd = graph;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var screen = e.GetPosition(this);
        var graph = ToGraph(screen);

        lastPointer = graph;

        switch (drag)
        {
            case Drag.Pan:
                PanTo(pan + (screen - dragOrigin));

                // Taken from where the pointer is rather than from where the
                // view ended up, so that a drag pushing at an edge does not
                // build up a debt of movement to be paid back before the view
                // will come away from it again.
                dragOrigin = screen;
                InvalidateVisual();
                return;

            // Pressed to select it, which a locked canvas still does, and then
            // held — which it does not. Caught here rather than at the press so
            // that the selection a press makes is unaffected.
            case Drag.Node when Locked:
                return;

            case Drag.Node when dragOrigins.Count > 0:
                var delta = Held((screen - dragOrigin) / zoom, dragOrigins);

                foreach (var moving in SelectedNodes)
                {
                    if (!dragOrigins.TryGetValue(moving.Id, out var from)) continue;

                    moving.X = from.X + delta.X;
                    moving.Y = from.Y + delta.Y;
                }

                InvalidateVisual();
                return;

            case Drag.Marquee:
                marqueeTo = graph;
                Sweep();
                InvalidateVisual();
                return;

            case Drag.Wire:
                wireEnd = graph;
                InvalidateVisual();
                return;

            default:
                Cursor = CursorOver(graph);
                return;
        }
    }

    /// <summary>
    /// What the pointer should look like over <paramref name="graph"/>.
    /// </summary>
    /// <remarks>
    /// A box, and the strip above an open group, answer here exactly as a module
    /// does — because they are taken hold of exactly as one is: pressing either
    /// selects what is inside and the drag that follows is the ordinary module
    /// drag, moving the modules with the box drawn from where they are. A cursor
    /// that went on saying "nothing here" over the one part of a group meant to
    /// be grabbed was the picture disagreeing with the gesture.
    /// </remarks>
    private Cursor CursorOver(Point graph)
    {
        if (HitPort(graph, out _, out _, out _)) return PortCursor;

        var draggable = HitNode(graph) is not null
            || HitBox(graph) is not null
            || HitOpenGroupHandle(graph) is not null;

        return draggable ? NodeCursor : ArrowCursor;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (drag == Drag.Wire)
            CompleteWire(ToGraph(e.GetPosition(this)));

        // Where a module sits is worth being able to take back and is nothing
        // the program can hear, so it goes into the history without asking
        // anything to recompile — a picture and a sound rebuilt because a block
        // was nudged would be work done for a change neither of them has in it.
        if (drag == Drag.Node)
        {
            var moved = SelectedNodes.Any(node =>
                dragOrigins.TryGetValue(node.Id, out var from)
                && (node.X != from.X || node.Y != from.Y));

            if (moved && history.Record(patch, mark: Mark))
            {
                HistoryChanged?.Invoke(this, EventArgs.Empty);
                Recorded?.Invoke(this, EventArgs.Empty);
            }

            // A press on one module of a group that turned out not to be a drag
            // was a click, and a click picks that module out of the group.
            else if (pendingNarrow is { } one) Select(one);
        }

        EndGesture();

        // The hand a pan put on goes back to whatever the pointer is standing
        // over now, which after a pan is rarely what it was standing over when
        // the pan began. Taken from the position rather than simply reset,
        // because the pointer may well have come to rest on a module and the
        // next move is not guaranteed — a hand left on a socket is a cursor
        // lying about what a click would do.
        Cursor = CursorOver(ToGraph(e.GetPosition(this)));

        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    private void CompleteWire(Point graph)
    {
        if (!HitPort(graph, out var node, out var port, out var isOutput))
        {
            // Let go over nothing at all. Dropped on a module's body it is a
            // miss — the sockets are where a wire means something — but dropped
            // on bare canvas it is a request for something to plug into.
            if (HitNode(graph) is null) OfferSomethingToPlugInto(graph);

            return;
        }

        // A wire only means something between opposite kinds of socket.
        if (isOutput == wireFromOutput) return;

        var (sourceNode, sourcePort, targetNode, targetPort) = wireFromOutput
            ? (wireNode, wirePort, node, port)
            : (node, port, wireNode, wirePort);

        // A wire that runs backwards has the delay its loop needs put on it, so
        // that drawing the cycle is the whole gesture. Placed as a module rather
        // than implied by the compiler: what carries a loop round is worth being
        // able to see, move and take back, and it is the one thing on the canvas
        // that can say the loop is heard rather than seen.
        if (patch.WouldCycle(sourceNode, targetNode)
            && InsertUnitDelay(sourceNode, sourcePort, targetNode, targetPort))
        {
            return;
        }

        patch.Connect(sourceNode, sourcePort, targetNode, targetPort);

        NotifyPatchChanged(WireGesture);
    }

    /// <summary>
    /// Puts a Unit Delay between two sockets and runs the loop through it, as one
    /// step of the history: the wire and the module that makes it legal arrived in
    /// a single gesture and come back the same way.
    /// </summary>
    /// <returns>
    /// Whether it went in. Answering false leaves the caller to draw the wire as
    /// asked and lets the compiler complain about it, which is what a build
    /// without the module wants — a refusal that can be read beats a gesture that
    /// quietly does nothing.
    /// </returns>
    private bool InsertUnitDelay(Guid sourceNode, int sourcePort, Guid targetNode, int targetPort)
    {
        if (NodeCatalog.Get(NodeCatalog.UnitDelayTypeId) is not { } def) return false;
        if (patch.Find(sourceNode) is not { } from) return false;
        if (patch.Find(targetNode) is not { } to) return false;

        // Between the two it joins, and a node's height below them. A back-edge
        // runs right to left, so the space between its ends is where the rest of
        // the loop already is — dropping under it keeps the new module clear of
        // what it was threaded into.
        var unit = NodeInstance.Create(
            def,
            (from.X + to.X) / 2,
            (from.Y + to.Y) / 2 + NodeGeometry.Height(def));

        patch.Nodes.Add(unit);
        patch.Connect(sourceNode, sourcePort, unit.Id, 0);
        patch.Connect(unit.Id, 0, targetNode, targetPort);

        // Selected because it is what just appeared, and because its panel is
        // where the one evaluation of delay is explained.
        Select(unit.Id);
        NotifyPatchChanged(WireGesture);

        return true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var screen = e.GetPosition(this);
        var anchor = ToGraph(screen);

        zoom = Math.Clamp(zoom * Math.Pow(1.12, e.Delta.Y), 0.2, 3.0);

        // Keep whatever was under the cursor pinned there — as far as the edge
        // of the canvas allows, since zooming out in a corner walks the view
        // outwards as surely as dragging it does.
        PanTo(new Point(screen.X - anchor.X * zoom, screen.Y - anchor.Y * zoom));

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Copy and paste are handled here rather than on the window, unlike undo
        // and redo. Ctrl+C in a text box means the text in it, and a window-wide
        // handler would have to know which of the two was meant; the canvas only
        // sees these while the canvas has the focus, which is the same question
        // answered by not asking it.
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
        {
            switch (e.Key)
            {
                case Key.C:
                    Clipboard(CopySelectionAsync);
                    e.Handled = true;
                    return;

                // Cut and paste change the patch, so a locked canvas has
                // neither. Copy above does not, and stays: reading a patch and
                // taking a piece of it elsewhere is exactly what a view is for.
                case Key.X when !Locked:
                    Clipboard(CutSelectionAsync);
                    e.Handled = true;
                    return;

                case Key.V when !Locked:
                    Clipboard(PasteAsync);
                    e.Handled = true;
                    return;

                case Key.A:
                    SelectAll();
                    e.Handled = true;
                    return;

                // Group and ungroup, on the letter every editor with a canvas
                // uses for it. Shift tells them apart rather than a second key,
                // which is the same pairing undo and redo already use.
                case Key.G when !Locked:
                    if ((e.KeyModifiers & KeyModifiers.Shift) != 0) UngroupSelected();
                    else GroupSelected();

                    e.Handled = true;
                    return;

                // Under Control with the rest of them, rather than on a bare
                // letter of its own. Every bare letter belongs to the instrument
                // now — see MainWindow's key handling — and a gesture that
                // depended on no MIDI In being in the patch would be one that
                // worked until somebody wanted to play.
                case Key.F:
                    FrameAll();
                    e.Handled = true;
                    return;
            }

            // Anything else with a modifier on it is somebody else's — undo and
            // redo are the window's, and marking them handled here would take
            // them off it.
            return;
        }

        switch (e.Key)
        {
            case Key.Delete or Key.Back when !Locked:
                DeleteSelected();
                e.Handled = true;
                break;

            // The module list, from the keyboard. Opened where the pointer last
            // was, so it lands under the hand the way the right-click does —
            // and in the middle of the view when the pointer has never been
            // over the canvas at all.
            case Key.Space when !Locked:
                MenuRequested?.Invoke(
                    this,
                    lastPointer ?? ToGraph(new Point(Bounds.Width / 2, Bounds.Height / 2)));
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Runs one of the clipboard gestures and passes on whatever it has to say.
    /// </summary>
    /// <remarks>
    /// Void and asynchronous, which is what a key press is: there is nobody to
    /// hand a task back to. So the catch is not optional — an exception escaping
    /// here would have no caller to reach and would take the program with it.
    /// </remarks>
    private async void Clipboard(Func<Task<string?>> gesture)
    {
        try
        {
            if (await gesture() is { } trouble) Reported?.Invoke(this, trouble);
        }
        catch (Exception ex)
        {
            Reported?.Invoke(this, $"Clipboard unavailable: {ex.Message}");
        }
    }
}
