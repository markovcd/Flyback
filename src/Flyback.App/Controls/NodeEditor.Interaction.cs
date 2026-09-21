using Avalonia;
using Avalonia.Input;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// The hand and the keyboard: what a press, a drag, a release, a wheel turn and a
/// key press do. Five gestures over one <c>Drag</c> state, and the fields behind
/// it are only ever read while their own gesture is the one under way.
/// </summary>
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
        dragOrigin = graph;

        // Panning is the middle button and nothing else: the right one opens
        // the module list instead — ADR-0046 — because a button cannot both
        // open something on a click and stay silent for one.
        //
        // It pans mid-gesture too, rather than stealing the gesture under way:
        // a wire (or a drag, or a marquee) is put on hold and picks back up
        // where it left off once the button comes back up, in
        // OnPointerReleased below.
        if (properties.IsMiddleButtonPressed)
        {
            if (drag != Drag.Pan) panSuspended = drag;
            drag = Drag.Pan;
            panOrigin = screen;
            Cursor = PanCursor;
            e.Pointer.Capture(this);
            return;
        }

        if (properties.IsRightButtonPressed)
        {
            // Over an unpatched input, the button held down is a knob for its value.
            if (drag == Drag.None && StartDial(graph, screen))
            {
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            // Not over a module: a right-click there is about that module rather
            // than about adding another one beside it. And nowhere at all on a
            // locked canvas, where the list would offer to place something the
            // next evaluation would take straight back off.
            if (!Locked
                && !Scene.HitPort(graph, out _, out _, out _)
                && Scene.HitBox(graph) is null
                && Scene.HitNode(graph) is null)
                MenuRequested?.Invoke(this, graph);

            // Over a module, or a shut box, the button is held to flip it; see
            // NodeEditor.Hold.cs. Over any other socket, or an open group's strip, it does nothing.
            if (!Scene.HitPort(graph, out _, out _, out _))
            {
                if (Scene.HitBox(graph) is { } shut) Hold(shut.Members);
                else if (Scene.HitNode(graph) is { } under) Hold([under.Id]);

                if (this.held.Count > 0) e.Pointer.Capture(this);
            }

            return;
        }

        if (!properties.IsLeftButtonPressed) return;

        // Ctrl means two things, and which one depends entirely on what is under
        // the pointer: over a module it adds to the selection, over an output it
        // lifts the wire off. They never meet — a press is over one or the other
        // — so one modifier serves both without either having to know.
        var ctrl = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;

        if (PickSocket(graph))
        {
            e.Handled = true;
            return;
        }

        // A socket on a locked canvas is not a handle. Falls through to the
        // module under it, so a press on a port still selects the module — which
        // is what somebody reading a patch was reaching for anyway.
        if (!Locked && Scene.HitPort(graph, out var portNode, out var portIndex, out var isOutput))
        {
            StartWire(portNode, portIndex, isOutput, lifting: ctrl, graph);
            e.Pointer.Capture(this);
            InvalidateVisual();
            return;
        }

        // A box before a module, because that is the order they are painted in:
        // a box is drawn at its members' least corner, which need not be where
        // any of them is, and over whatever module happens to be there.
        if (Scene.HitBox(graph) is null && Scene.HitNode(graph) is { } node)
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
        if (Scene.HitBox(graph) is { } box)
        {
            // Opening a box is an edit, so a locked canvas selects it instead —
            // the same answer Ctrl+E gets.
            if (e.ClickCount == 2 && !Locked) ToggleBox(box);
            else PressGroup(box, ctrl);

            e.Pointer.Capture(this);
            InvalidateVisual();
            return;
        }

        if (Scene.HitOpenGroupHandle(graph) is { } opened)
        {
            if (e.ClickCount == 2 && !Locked) ToggleBox(opened);
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

        marqueeWas.Clear();
        marqueeWas.UnionWith(selection);

        drag = Drag.Marquee;
        Sweep();

        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    /// <summary>
    /// Selects what the rubber band is currently over, together with whatever it
    /// was told to keep.
    /// </summary>
    private void Sweep()
    {
        var wanted = new HashSet<Guid>(marqueeBase);

        wanted.UnionWith(Scene.Swept(CanvasScene.Band(marqueeFrom, marqueeTo)));

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
    /// What a press on a module does to the selection, and the start of a drag of
    /// whatever that leaves selected.
    /// </summary>
    /// <remarks>
    /// The awkward case is a plain press on a module already in a larger selection,
    /// and it cannot be answered here: collapsing to it would make a group
    /// impossible to drag by one of its members, and not collapsing would make one
    /// impossible to pick apart. So it is deferred to the release — see
    /// <see cref="pendingNarrow"/>.
    /// </remarks>
    /// <summary>
    /// Ends whatever gesture was under way, and forgets what it was holding.
    /// </summary>
    /// <remarks>
    /// What is forgotten here is already out of reach, each field being cleared by
    /// the press that starts its gesture. Written down all the same, so ending a
    /// gesture is one thing with one name.
    /// </remarks>
    private void EndGesture()
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

    /// <summary>
    /// Puts back whatever the gesture under way has already changed, and says
    /// whether there was one to back out of.
    /// </summary>
    /// <remarks>
    /// Backing out is not the same as stopping, because two of the three have
    /// changed something before the hand has chosen anything: a wire lifted off an
    /// input is off it from the press — see <see cref="StartWire"/> — and a rubber
    /// band has replaced the selection on its first move. A module drag has carried
    /// modules and recorded nothing, so merely ending it would leave them wherever
    /// the hand had got to.
    /// <para>
    /// A pan is not backed out of: it holds nothing and the view is where somebody
    /// put it. So with one under way, what this ends is whatever the pan suspended.
    /// </para>
    /// </remarks>
    private bool Abort()
    {
        if (EndDial(restore: true)) return true;

        var panning = drag == Drag.Pan;
        var aborting = panning ? panSuspended : drag;

        if (aborting == Drag.None) return false;

        switch (aborting)
        {
            // Straight back into the place in the list it came out of, under the
            // name the lifting was recorded under: the patch is then the one the
            // gesture began with, which the history sees and drops the step for. A
            // wire being drawn new has nothing to put back and falls past this.
            case Drag.Wire when lifted is { } was:
                lifted = null;
                patch.Connections.Insert(Math.Min(was.At, patch.Connections.Count), was.Wire);
                NotifyPatchChanged(WireGesture);
                break;

            case Drag.Node:
                foreach (var moving in SelectedNodes)
                {
                    if (!dragOrigins.TryGetValue(moving.Id, out var from)) continue;

                    moving.X = from.X;
                    moving.Y = from.Y;
                }

                break;

            case Drag.Marquee:
                selection.Clear();
                selection.UnionWith(marqueeWas);
                Refocus();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                break;
        }

        // The button is still down, and the release that follows finds nothing
        // under way and completes nothing — which is what makes this an abort
        // rather than a pause. The cursor is put right here because the hand need
        // never move again, and a move would otherwise be the only thing to do it.
        if (panning)
        {
            panSuspended = Drag.None;
        }
        else
        {
            EndGesture();
            if (lastPointer is { } over) Cursor = CursorOver(over);
        }

        InvalidateVisual();
        return true;
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
    /// Grabbing a connected socket picks the existing wire up by the end that was
    /// not grabbed, so re-patching works the way it does on a real rig.
    /// </summary>
    /// <remarks>
    /// Which end that leaves free is the whole difference between the two gestures:
    /// grabbing an input chooses a new input for a signal that keeps its source,
    /// and grabbing an output chooses a new source for a socket that keeps being
    /// fed.
    /// </remarks>
    /// <param name="isOutput"></param>
    /// <param name="lifting">
    /// Whether Ctrl was held, which only matters on an output. An input holds one
    /// wire, so grabbing it can only mean that one; an output holds any number, and
    /// dragging from one has always meant "start another". So reaching for the wire
    /// already there asks for the modifier, and only where exactly one wire leaves
    /// the socket — with none or several this falls back to starting a new wire
    /// rather than picking one of four for you.
    /// </param>
    /// <param name="nodeId"></param>
    /// <param name="portIndex"></param>
    /// <param name="graph"></param>
    private void StartWire(Guid nodeId, int portIndex, bool isOutput, bool lifting, Point graph)
    {
        wireGesture++;
        lifted = null;

        // Before a wire is lifted off, so whatever hears about that change already
        // sees a gesture under way — the shell holds back moving the canvas until it
        // ends.
        drag = Drag.Wire;
        wireEnd = graph;

        if (!isOutput && patch.IncomingTo(nodeId, portIndex) is { } existing)
        {
            lifted = (existing, patch.Connections.IndexOf(existing));
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
            lifted = (sole, patch.Connections.IndexOf(sole));
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
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var screen = e.GetPosition(this);
        var graph = ToGraph(screen);

        lastPointer = graph;

        if (dialed is not null)
        {
            MoveDial(screen, e.KeyModifiers);
            return;
        }

        // The middle button's own state, sampled here rather than trusted to a
        // Pressed/Released pair: a second button going down while the first is
        // already captured for a wire, a node or a marquee does not reliably
        // raise one of its own, and a pan that only worked when nothing else
        // was under way would not be a pan that works while dragging a wire.
        var middleDown = e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed;

        if (middleDown && drag != Drag.Pan)
        {
            panSuspended = drag;
            drag = Drag.Pan;
            panOrigin = screen;
            Cursor = PanCursor;
        }
        else if (!middleDown && drag == Drag.Pan && panSuspended != Drag.None)
        {
            // Nothing to put right: every gesture a pan can suspend is held in
            // the canvas's coordinates, which a pan does not move.
            drag = panSuspended;
            panSuspended = Drag.None;

            Cursor = drag == Drag.Wire ? PortCursor : CursorOver(graph);
        }

        switch (drag)
        {
            case Drag.Pan:
                PanTo(pan + (screen - panOrigin));

                // Taken from where the pointer is rather than from where the
                // view ended up, so that a drag pushing at an edge does not
                // build up a debt of movement to be paid back before the view
                // will come away from it again.
                panOrigin = screen;
                InvalidateVisual();
                return;

            // Pressed to select it, which a locked canvas still does, and then
            // held — which it does not. Caught here rather than at the press so
            // that the selection a press makes is unaffected.
            case Drag.Node when Locked:
                return;

            case Drag.Node when dragOrigins.Count > 0:
                var delta = Scene.Held(graph - dragOrigin, dragOrigins);

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
                TipOver(graph);
                return;
        }
    }

    /// <summary>
    /// What the pointer should look like over <paramref name="graph"/>.
    /// </summary>
    /// <remarks>
    /// A box, and the strip above an open group, answer here exactly as a module
    /// does, because they are taken hold of exactly as one is: pressing either
    /// selects what is inside and the drag that follows is the ordinary module drag.
    /// </remarks>
    private Cursor CursorOver(Point graph)
    {
        if (Scene.HitPort(graph, out _, out _, out _)) return PortCursor;

        var draggable = Scene.HitNode(graph) is not null
            || Scene.HitBox(graph) is not null
            || Scene.HitOpenGroupHandle(graph) is not null;

        return draggable ? NodeCursor : ArrowCursor;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        // The middle button's own release is answered here and nowhere else,
        // whatever drag happens to be by the time it arrives — OnPointerMoved
        // may already have handed a suspended gesture back its pointer on a
        // move that crossed this release, and the wire (or drag, or marquee)
        // it interrupted must not be finished off by a button that was never
        // the one holding it.
        if (e.InitialPressMouseButton == MouseButton.Right)
        {
            if (EndDial()) e.Pointer.Capture(null);

            ReleaseHeld();
            return;
        }

        if (e.InitialPressMouseButton == MouseButton.Middle)
        {
            if (panSuspended != Drag.None)
            {
                drag = panSuspended;
                panSuspended = Drag.None;

                Cursor = drag == Drag.Wire ? PortCursor : CursorOver(ToGraph(e.GetPosition(this)));
                InvalidateVisual();
                return;
            }

            // A plain pan, nothing under it: ends like any other gesture.
            if (drag == Drag.Pan)
            {
                EndGesture();
                Cursor = CursorOver(ToGraph(e.GetPosition(this)));
                e.Pointer.Capture(null);
                InvalidateVisual();
            }

            return;
        }

        // The button that began the gesture came up while the pan it was put on
        // hold for was still going, so this release is the pan's own and the last
        // one there will be. What was on hold ends here as it would have: a module
        // stays where it was carried to, as a step; a wire is dropped rather than
        // completed, since its end was let go of over a view that was moving.
        if (drag == Drag.Pan && panSuspended == Drag.Node) RecordMove();

        if (drag == Drag.Wire)
            CompleteWire(ToGraph(e.GetPosition(this)));

        // Where a module sits is worth being able to take back and is nothing
        // the program can hear, so it goes into the history without asking
        // anything to recompile — a picture and a sound rebuilt because a block
        // was nudged would be work done for a change neither of them has in it.
        if (drag == Drag.Node && !RecordMove() && pendingNarrow is { } one)
        {
            // A press on one module of a group that turned out not to be a drag
            // was a click, and a click picks that module out of the group.
            Select(one);
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

    /// <summary>Puts a drag that moved something into the history, and says whether it did.</summary>
    private bool RecordMove()
    {
        var moved = SelectedNodes.Any(node =>
            dragOrigins.TryGetValue(node.Id, out var from)
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            && (node.X != from.X || node.Y != from.Y));

        if (!moved || !history.Record(patch, mark: Mark)) return false;

        HistoryChanged?.Invoke(this, EventArgs.Empty);
        Recorded?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <summary>
    /// The pointer was taken away mid-gesture, and the button will come up
    /// somewhere this never hears about: another window brought forward, a system
    /// dialog, the lock screen.
    /// </summary>
    /// <remarks>
    /// The gesture ends where it stood. A module stays where it was dragged to, as
    /// a step; a wire is dropped rather than completed, since nobody chose where it
    /// ended. A release gives up the capture itself and arrives here with nothing
    /// left under way.
    /// </remarks>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        EndDial();
        ReleaseHeld();

        if (drag == Drag.None) return;

        if (drag == Drag.Node || panSuspended == Drag.Node) RecordMove();

        EndGesture();
        Cursor = ArrowCursor;
        InvalidateVisual();
    }

    private void CompleteWire(Point graph)
    {
        if (!Scene.HitPort(graph, out var node, out var port, out var isOutput))
        {
            // Let go over nothing at all. Dropped on a module's body it is a
            // miss — the sockets are where a wire means something — but dropped
            // on bare canvas it is a request for something to plug into.
            if (Scene.HitNode(graph) is null) OfferSomethingToPlugInto(graph);

            return;
        }

        // A wire only means something between opposite kinds of socket.
        if (isOutput == wireFromOutput) return;

        var (sourceNode, sourcePort, targetNode, targetPort) = wireFromOutput
            ? (wireNode, wirePort, node, port)
            : (node, port, wireNode, wirePort);

        // A wire that closes a loop is drawn like any other, and carries the
        // previous evaluation by being the one that runs backwards — so drawing
        // the cycle is the whole gesture and there is nothing to put on it. The
        // canvas dashes it; see Cycles.Backwards and ADR-0075.
        patch.Connect(sourceNode, sourcePort, targetNode, targetPort);

        // Put straight back where it was lifted from, and into the place in the
        // list it was lifted out of: the patch is then the one the gesture began
        // with, which the history can see and drops the step for.
        if (lifted is { } was
            && was.Wire == new Connection(sourceNode, sourcePort, targetNode, targetPort)
            && patch.Connections.Remove(was.Wire))
        {
            patch.Connections.Insert(Math.Min(was.At, patch.Connections.Count), was.Wire);
        }

        NotifyPatchChanged(WireGesture);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var screen = e.GetPosition(this);
        var anchor = ToGraph(screen);

        zoom = Math.Clamp(zoom * Math.Pow(1.12, e.Delta.Y), MinZoom, 3.0);

        // Keep whatever was under the cursor pinned there — as far as the edge
        // of the canvas allows, since zooming out in a corner walks the view
        // outwards as surely as dragging it does.
        PanTo(new Point(screen.X - anchor.X * zoom, screen.Y - anchor.Y * zoom));

        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>
    /// Whether a key may change the patch: not on a locked canvas, and not while the
    /// pointer is holding a piece of it.
    /// </summary>
    /// <remarks>
    /// The pointer is captured for a drag and the keyboard is not, so Delete arrives
    /// mid-wire perfectly well — and the wire would then be completed from a module
    /// that has gone. Ignored rather than queued: letting go leaves the press to be
    /// made again.
    /// </remarks>
    private bool Editable => !Locked && drag == Drag.None && dialed is null;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Back out of the gesture under way, which is what this key means
        // everywhere else here — the module filter, a rename box, the full-screen
        // preview. Before the modifier check below, because the hand wanting out
        // may still be holding the Ctrl that began it: a wire comes off an output
        // with Ctrl down, and a selection is added to the same way. Unhandled with
        // nothing under way, so the window's own Escape still gets it.
        if (e.Key == Key.Escape && Abort())
        {
            e.Handled = true;
            return;
        }

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
                case Key.X when Editable:
                    Clipboard(CutSelectionAsync);
                    e.Handled = true;
                    return;

                case Key.V when Editable:
                    Clipboard(PasteAsync);
                    e.Handled = true;
                    return;

                // Duplicate, which the three above do not add up to: this
                // leaves the clipboard alone, so whatever was put there earlier
                // is still there to paste afterwards.
                case Key.D when Editable:
                    DuplicateSelection();
                    e.Handled = true;
                    return;

                case Key.A:
                    SelectAll();
                    e.Handled = true;
                    return;

                // Group and ungroup, on the letter every editor with a canvas
                // uses for it. Shift tells them apart rather than a second key,
                // which is the same pairing undo and redo already use.
                case Key.G when Editable:
                    if ((e.KeyModifiers & KeyModifiers.Shift) != 0) UngroupSelected();
                    else GroupSelected();

                    e.Handled = true;
                    return;

                // A double-click opens the one box it lands on; this opens every
                // group the selection reaches. Shift shuts them, the pairing
                // group and ungroup use above.
                case Key.E when Editable:
                    if ((e.KeyModifiers & KeyModifiers.Shift) != 0) CloseSelectedGroups();
                    else OpenSelectedGroups();

                    e.Handled = true;
                    return;

                // Bypass, on the letter a desk uses for it. One key both ways,
                // unlike group and open above: a module is off or it is on, and
                // there is nothing in between for a second key to mean.
                case Key.B when Editable:
                    SwitchSelected();
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
            case Key.Delete or Key.Back when Editable:
                DeleteSelected();
                e.Handled = true;
                break;

            // The module list, from the keyboard. Opened where the pointer last
            // was, so it lands under the hand the way the right-click does —
            // and in the middle of the view when the pointer has never been
            // over the canvas at all.
            case Key.Space when Editable:
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
