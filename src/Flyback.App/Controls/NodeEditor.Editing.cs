using Avalonia;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// What an edit is and how one is taken back: the history, the two ways a patch is
/// put on the canvas without being opened as a document, and the commands that add
/// and remove modules.
/// </summary>
/// <remarks>
/// The history itself is the engine's and holds JSON snapshots; here is the
/// canvas's side of it — which gestures earn a step, and the gate every arriving
/// patch passes so a step can never be undone into a module standing half off the
/// canvas.
/// </remarks>
public sealed partial class NodeEditor
{
    /// <summary>
    /// The patch as it stands has been written to a file, so there is nothing
    /// in it left to lose. What can be undone is untouched: saving is not an
    /// edit, and no reason to stop being able to take one back.
    /// </summary>
    public void MarkSaved()
    {
        history.Saved(patch);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The patch as it stands is a document that has just arrived, so there is
    /// nothing behind it and nothing left to lose.
    /// </summary>
    /// <remarks>
    /// For the caller that put the document here through an edit, because it had to
    /// build the patch before it could know what it was. Going through the setter
    /// again would show the same patch twice; what is wrong is the history alone,
    /// holding a step for an arrival.
    /// </remarks>
    public void MarkOpened()
    {
        history.Opened(patch, Mark);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The hand has come off whatever it was holding, so the next edit starts a step
    /// of its own rather than folding into the one before.
    /// </summary>
    /// <remarks>
    /// For the gestures this canvas does not make itself: an edit filed under a
    /// control in the panel is named after that control, and every drag of one
    /// slider is the same name. Somebody outside can see the hand let go; the
    /// history cannot.
    /// </remarks>
    public void GestureEnded() => history.GestureEnded();

    /// <summary>
    /// Call after editing a node from outside the canvas, e.g. the inspector.
    /// </summary>
    /// <param name="coalesce">
    /// Names the gesture, where the edit is one frame of a control being held
    /// down — see <see cref="PatchHistory.Record"/>. A slider dragged across
    /// its range is one thing somebody did and wants back in one press.
    /// </param>
    public void NotifyPatchChanged(string? coalesce = null)
    {
        // Before the step is recorded, so what can be undone into is a patch
        // whose modules are all on the canvas. Every edit that places one comes
        // through here — a paste, a module added, a layout — and none of them
        // knows how large what it placed is.
        HoldInside();

        var stepped = history.Record(patch, coalesce, Mark);

        InvalidateVisual();
        PatchChanged?.Invoke(this, EventArgs.Empty);
        HistoryChanged?.Invoke(this, EventArgs.Empty);

        if (stepped) Recorded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Puts the patch back as it was before the last edit.</summary>
    /// <returns>Whether there was one.</returns>
    public bool Undo() => Restore(history.Undo());

    /// <summary>Puts back the edit the last undo took away.</summary>
    public bool Redo() => Restore(history.Redo());

    /// <summary>
    /// Says the same thing beside every step there is, for a <see cref="Mark"/>
    /// that has changed with no edit made to change it.
    /// </summary>
    public void Remark(object? mark)
    {
        Mark = mark;
        history.Remark(mark);
    }

    /// <summary>
    /// Shows a patch that came out of the history. Not the <see cref="Patch"/>
    /// setter, which is for a document arriving from outside and resets both the
    /// view and the history — neither of which an undo should touch. The canvas
    /// stays exactly where it was, because the thing being looked at is the edit
    /// that just came back rather than the patch as a whole.
    /// </summary>
    private bool Restore(Patch? restored)
    {
        if (restored is null) return false;

        Show(restored);

        // Whatever the owner had beside the patch when this step was taken is
        // what it has again, read back off the canvas the moment this returns.
        Mark = history.Mark;

        Announce();

        return true;
    }

    /// <summary>
    /// Shows a patch built from the one that was open rather than opened in its
    /// place — the assistant's work, which is a large edit and not a new document.
    /// Recorded like any other edit.
    /// </summary>
    /// <remarks>
    /// Framed, which an undo is not: nothing about where an assistant lays its
    /// modules out has to resemble the current canvas.
    /// </remarks>
    public void ApplyEdit(Patch edited)
    {
        Show(edited);

        var stepped = history.Record(patch, mark: Mark);

        FrameAll();
        Announce();

        if (stepped) Recorded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Puts a patch on the canvas without saying where it came from. The
    /// selection survives only if what it named does — everything here is a
    /// fresh object, so an id is the one thing that can be carried across.
    /// </summary>
    private void Show(Patch next)
    {
        patch = next;
        patch.EnsureOutput();
        HoldInside();

        selection.RemoveWhere(id => patch.Find(id) is null);
        if (focus is { } kept && !selection.Contains(kept)) focus = null;

        EndGesture();
        InvalidateVisual();
    }

    /// <summary>
    /// Every module on the canvas is a fresh object after the patch is swapped,
    /// so anything holding one — the inspector, a step list — has to be built
    /// again whether or not the selection itself changed. Which is why the
    /// selection is announced even when the id is the one it already was.
    /// </summary>
    private void Announce()
    {
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        PatchChanged?.Invoke(this, EventArgs.Empty);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Drops a new module on the canvas and hands it back, or returns null having
    /// added nothing where the patch may not hold another — the Output, of which
    /// there is always one. That case selects the one already there: whoever asked
    /// for it wanted it, and this is where it is.
    /// </summary>
    /// <param name="typeId">Which module to add.</param>
    /// <param name="at">
    /// Where to centre it, in graph space. The middle of the view when nothing says
    /// otherwise, which is what a module added from anywhere but the canvas gets.
    /// </param>
    public NodeInstance? AddNode(string typeId, Point? at = null)
    {
        var def = NodeCatalog.Require(typeId);

        if (!patch.CanAdd(typeId))
        {
            if (patch.FirstOf(typeId) is { } already) Select(already.Id);

            InvalidateVisual();
            return null;
        }

        var centre = at ?? ToGraph(new Point(Bounds.Width / 2, Bounds.Height / 2));

        var node = NodeInstance.Create(def, centre.X - NodeGeometry.Width / 2, centre.Y - NodeGeometry.Height(def) / 2);
        patch.Nodes.Add(node);
        Select(node.Id);
        NotifyPatchChanged();
        return node;
    }

    /// <summary>
    /// Adds a module where a wire was dropped and plugs the wire into it, as one
    /// edit — the module and the wire arrived in one gesture and come back the same
    /// way.
    /// </summary>
    /// <remarks>
    /// Which socket it lands on is <see cref="Fitting"/>'s decision. Nothing is
    /// refused for being the wrong kind, because nothing is: the compiler broadcasts
    /// a scalar and takes luma from a color, so the question is only which socket
    /// was meant.
    /// </remarks>
    public NodeInstance? AddNodeWired(string typeId, WireDrop drop)
    {
        var def = NodeCatalog.Require(typeId);

        if (!patch.CanAdd(typeId)) return AddNode(typeId, drop.At);

        var centre = drop.At;
        var node = NodeInstance.Create(def, centre.X - NodeGeometry.Width / 2, centre.Y - NodeGeometry.Height(def) / 2);

        patch.Nodes.Add(node);

        // Both halves before the one record, so one press of undo takes the
        // module and the wire away together.
        var sockets = drop.FromOutput ? def.Inputs : def.Outputs;

        if (Fitting(sockets, drop.Kind) is { } socket)
        {
            if (drop.FromOutput) patch.Connect(drop.Node, drop.Port, node.Id, socket);
            else patch.Connect(node.Id, socket, drop.Node, drop.Port);
        }

        Select(node.Id);
        NotifyPatchChanged();
        return node;
    }

    /// <summary>
    /// Which socket of a new module a dropped wire belongs on, or null where the
    /// module has none of that kind at all.
    /// </summary>
    /// <remarks>
    /// The port a module is about comes first: <see cref="PortSpec.Domain"/> and
    /// <see cref="PortSpec.Swept"/> are the socket the module exists to have
    /// something in, which is what the compiler already warns about. Then an exact
    /// match of kind, which tells a Scan's <c>view</c> from its <c>out</c>. Then the
    /// first socket, which is where this would land anyway — the catalogue is
    /// written with the principal one first.
    /// </remarks>
    private static int? Fitting(IReadOnlyList<PortSpec> sockets, PortKind kind)
    {
        if (sockets.Count == 0) return null;

        for (var i = 0; i < sockets.Count; i++)
            if (sockets[i].Domain || sockets[i].Swept)
                return i;

        for (var i = 0; i < sockets.Count; i++)
            if (sockets[i].Kind == kind)
                return i;

        return 0;
    }

    /// <summary>
    /// A wire let go over bare canvas, handed to whoever can offer something to
    /// plug it into.
    /// </summary>
    private void OfferSomethingToPlugInto(Point graph)
    {
        if (patch.Find(wireNode) is not { } holding) return;
        if (NodeCatalog.Get(holding.TypeId) is not { } def) return;

        var sockets = wireFromOutput ? def.Outputs : def.Inputs;
        if (wirePort < 0 || wirePort >= sockets.Count) return;

        WireDropped?.Invoke(this, new WireDrop(
            graph, wireNode, wirePort, wireFromOutput, sockets[wirePort].Kind));
    }

    /// <summary>
    /// Removes every selected module except the Output, which the graph refuses. A
    /// refused module is left selected, since losing the selection would take its
    /// settings panel away.
    /// </summary>
    /// <remarks>
    /// One edit however many modules go, because one gesture asked for all of them
    /// (ADR-0044): deleting five and undoing them one at a time would be five
    /// presses for something nobody did five times.
    /// </remarks>
    public void DeleteSelected()
    {
        if (selection.Count == 0) return;

        var went = false;

        foreach (var id in selection.ToArray())
            if (patch.Remove(id))
            {
                selection.Remove(id);
                went = true;
            }

        if (!went) return;

        if (focus is { } kept && !selection.Contains(kept))
            focus = selection.Count == 0 ? null : SelectedNodes[^1].Id;

        SelectionChanged?.Invoke(this, EventArgs.Empty);
        NotifyPatchChanged();
    }
}
