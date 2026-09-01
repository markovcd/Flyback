using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// A wire let go over empty canvas: where it landed, and which socket is still
/// holding the other end.
/// </summary>
/// <param name="At">Where it was dropped, in graph space — where the new module goes.</param>
/// <param name="Node">The module the wire is still attached to.</param>
/// <param name="Port">Which of that module's sockets.</param>
/// <param name="FromOutput">
/// True when the loose end is looking for an input, because the end still held
/// is an output. The whole of which direction the new wire runs.
/// </param>
/// <param name="Kind">What flows down it, which is a hint about where it belongs on the far end.</param>
public readonly record struct WireDrop(Point At, Guid Node, int Port, bool FromOutput, PortKind Kind);

/// <summary>
/// The patch bay. Everything is drawn directly rather than built from controls,
/// which keeps panning and zooming over a few hundred modules cheap and puts
/// layout, painting and hit-testing in one place.
/// </summary>
public sealed class NodeEditor : Control
{
    private enum Drag
    {
        None,
        Pan,
        Node,
        Wire,
        Marquee,
    }

    private static readonly IBrush Background = new SolidColorBrush(Colors.Canvas);
    private static readonly IBrush NodeFill = new SolidColorBrush(Colors.Node);
    private static readonly IBrush NodeFillSelected = new SolidColorBrush(Colors.NodeSelected);
    private static readonly IBrush LabelBrush = new SolidColorBrush(Colors.Label);
    private static readonly IBrush ValueBrush = new SolidColorBrush(Colors.Value);
    private static readonly IBrush NormalBrush = new SolidColorBrush(Colors.Normalled);
    private static readonly IBrush HeaderTextBrush = Brushes.White;
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Colors.Grid));
    private static readonly IPen GridPenMajor = new Pen(new SolidColorBrush(Colors.GridMajor));
    private static readonly IPen NodeBorder = new Pen(new SolidColorBrush(Colors.Outline), 1.5);
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
    /// The rubber band. Dashed, because it is a gesture in progress rather than
    /// anything in the patch, and drawn over the canvas rather than in it — so
    /// the dashes and the hairline stay the same size however far the view is
    /// zoomed out.
    /// </summary>
    private static readonly IPen MarqueePen = new Pen(
        new SolidColorBrush(Colors.Attention),
        1,
        new DashStyle([4, 3], 0));

    private static readonly IBrush MarqueeFill = new SolidColorBrush(Colors.Attention, 0.08);
    private static readonly IPen PortOutline = new Pen(new SolidColorBrush(Colors.Outline), 1.2);

    /// <summary>
    /// Ground beyond the canvas edge.
    /// </summary>
    /// <summary>
    /// A box's header, in the one color on the canvas that belongs to no
    /// category. A module's header is tinted by what it does; a group does
    /// nothing, so it is drawn in the outline color and reads as canvas
    /// furniture rather than as a module whose kind you have forgotten.
    /// </summary>
    private static readonly IBrush GroupHeaderFill = new SolidColorBrush(Colors.Outline, 0.85);

    /// <summary>
    /// The dashed ring round a group that is open — see OpenGroup.
    /// </summary>
    /// <remarks>
    /// In the separator color rather than the outline one, at half strength. A
    /// box's border is drawn <em>on</em> a module, where a grey darker than the
    /// canvas reads as an edge; this is drawn on the canvas itself, which is
    /// lighter than that grey — so the ring meant to say "these belong together"
    /// was saying it at six values in two hundred and fifty-five. Half strength
    /// because the full one goes the other way and reads as a thing in the
    /// patch: this is furniture, and furniture that shouts is furniture in the
    /// way. Dashed for the same reason, an open group being a region rather
    /// than a thing.
    /// </remarks>
    private static readonly IPen OpenGroupPen = new Pen(
        new SolidColorBrush(Colors.Separator, 0.5),
        1.5,
        new DashStyle([6, 4], 0));

    /// <summary>
    /// The same ring while everything inside it is selected.
    /// </summary>
    /// <remarks>
    /// A shut box turns its border the selected color and an open one had
    /// nothing that did, so the one picture saying which modules a gesture is
    /// about went missing exactly when the group was opened up to work on. Held
    /// well under <see cref="SelectionPen"/>, which is what a module wears: the
    /// modules inside are already ringed one by one, and this is only the line
    /// round the lot of them.
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
    private static readonly IBrush OpenGroupFill = new SolidColorBrush(Colors.Separator, 0.06);

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

    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor PortCursor = new(StandardCursorType.Cross);
    private static readonly Cursor NodeCursor = new(StandardCursorType.SizeAll);

    /// <summary>
    /// While the middle button is dragging the view.
    /// </summary>
    /// <remarks>
    /// A hand rather than the four-way arrow a module gets, because what is
    /// moving is not in the patch: the sheet is going under the pointer and
    /// nothing on it has changed. The same distinction the two gestures already
    /// make — a module drag edits the patch and a pan does not — said in the one
    /// place a person is looking while doing either.
    /// <para>
    /// The pointing hand and not a grabbing one, because there is no grabbing
    /// one to have: Windows ships sixteen cursors and no hand but this, and
    /// <c>grab</c> is a picture browsers carry themselves rather than anything
    /// the system knows about. <c>DragMove</c> is not the way round it — on
    /// Windows that is the OLE drag icon, an arrow wearing a small box, and it
    /// falls back to an <em>up arrow</em> when ole32 declines to give it up.
    /// The four-way arrow is the other candidate and is what a module drag
    /// already wears, which is the one thing this is here to say it is not.
    /// </para>
    /// </remarks>
    private static readonly Cursor PanCursor = new(StandardCursorType.Hand);

    private readonly PatchHistory history = new();

    /// <summary>
    /// How far past the canvas the view may be scrolled, in graph units: a strip
    /// of the ground beyond, so the edge reads as an edge with something on the
    /// far side of it rather than as the window's own frame.
    /// </summary>
    /// <remarks>
    /// Nothing is ever out there to be looked at — a module is held wholly
    /// inside the canvas, body and all — so this is as much room as the line
    /// needs to be seen and no more. Scaled by the zoom like everything else in
    /// graph units, which puts it between a thin band and a comfortable one
    /// across the range the wheel allows.
    /// </remarks>
    internal const double ViewMargin = 160;

    /// <summary>How far from the origin the view may see, on each axis.</summary>
    internal const double ViewReach = NodeInstance.Extent + ViewMargin;

    /// <summary>
    /// The canvas itself, in graph units: the square a module may stand on.
    /// </summary>
    /// <remarks>
    /// Drawn rather than merely enforced: a bound with nothing to show is a wall
    /// in the dark, and a drag that stops there would have nothing to blame but
    /// the program.
    /// </remarks>
    internal static readonly Rect CanvasBounds = new(
        -NodeInstance.Extent,
        -NodeInstance.Extent,
        NodeInstance.Extent * 2,
        NodeInstance.Extent * 2);

    private Patch patch = new();
    private double zoom = 1;
    private Point pan = new(40, 40);

    /// <summary>
    /// Every selected module. A set rather than one id, so that a gesture can
    /// name several — which is what dragging a group and copying one need, and
    /// neither of those can be built on a selection that holds one thing.
    /// </summary>
    private readonly HashSet<Guid> selection = [];

    /// <summary>
    /// Which of the selected modules the inspector is about. Always one of
    /// <see cref="selection"/> or nothing at all, and it is the last one the
    /// pointer named: a panel has room for one module's values, and the one
    /// just clicked is the one that was being asked about.
    /// </summary>
    private Guid? focus;

    private bool framePending = true;
    private Drag drag;
    private Point dragOrigin;

    /// <summary>
    /// Where each module of the selection was when the drag began. Recorded for
    /// all of them rather than tracked as one offset, so that a drag ending
    /// exactly where it started can be told from one that moved — which is what
    /// decides whether the history gains a step.
    /// </summary>
    private readonly Dictionary<Guid, Point> dragOrigins = [];

    /// <summary>
    /// A module pressed while it was already part of a larger selection, which
    /// is a click that cannot be resolved until the button comes back up.
    /// Pressing it must not narrow the selection, or a set could never be
    /// dragged by one of its own members; releasing it without having dragged
    /// must, or there would be no way to pick one module out of a set.
    /// </summary>
    /// <remarks>
    /// "Narrow" rather than "collapse", which since <see cref="NodeGroup"/> means
    /// the other thing: drawing several modules as one box. Nothing here has
    /// anything to do with that — this is about how many modules a click leaves
    /// selected.
    /// </remarks>
    private Guid? pendingNarrow;

    /// <summary>
    /// The two corners of the rubber band, in graph space so that it stays over
    /// the same modules whatever the zoom.
    /// </summary>
    private Point marqueeFrom;
    private Point marqueeTo;

    /// <summary>
    /// What was selected when the rubber band was started. A band with the
    /// modifier held adds to it, so the modules it sweeps have to be added to
    /// something that does not itself change as the band moves — sweeping back
    /// off a module must take it out again, and it cannot if the previous frame
    /// has already been folded in.
    /// </summary>
    private readonly HashSet<Guid> marqueeBase = [];

    /// <summary>
    /// Where the pointer was last seen, in graph space. Kept so that a gesture
    /// with no position of its own — the space bar — can still open the module
    /// list where the hand is rather than in the middle of the view.
    /// </summary>
    private Point? lastPointer;

    private Guid wireNode;
    private int wirePort;
    private bool wireFromOutput;

    /// <summary>
    /// Which re-patch this is. Unplugging an input and plugging it in somewhere
    /// else is two edits and one gesture, so both carry this and fold into one
    /// step — while two unpluggings in a row stay two, which counting is what
    /// tells them apart.
    /// </summary>
    private int wireGesture;

    /// <summary>The name this re-patch records its edits under.</summary>
    private string WireGesture => $"wire {wireGesture}";

    private Point wireEnd;

    public NodeEditor()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    /// <summary>Raised whenever the graph itself changed and needs recompiling.</summary>
    public event EventHandler? PatchChanged;

    /// <summary>Raised when a different node becomes selected.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// Raised when what can be undone or redone changed. Separate from
    /// <see cref="PatchChanged"/> because the two do not always coincide: moving
    /// a module is an edit worth taking back and not one the program can hear,
    /// so it goes in the history without asking anything to recompile.
    /// </summary>
    public event EventHandler? HistoryChanged;

    /// <summary>
    /// Raised when something the canvas was asked to do has to be explained
    /// rather than done — a paste of something that is not a patch, or of one
    /// naming a module this build has not got. The canvas has nowhere to say it;
    /// the window does.
    /// </summary>
    public event EventHandler<string>? Reported;

    /// <summary>
    /// Raised by a right-click on empty canvas, carrying the point in graph
    /// space that was clicked. What the shell puts there is the module palette,
    /// and what is picked from it belongs at this point rather than wherever the
    /// view happens to be centred.
    /// </summary>
    /// <remarks>
    /// A click and not a drag: the right button still pans, so this waits for
    /// the button to come up and asks whether the pointer went anywhere. And not
    /// over a module, because a right-click there is about that module rather
    /// than about adding another beside it.
    /// </remarks>
    public event EventHandler<Point>? MenuRequested;

    /// <summary>
    /// Raised when a wire is let go over empty canvas, carrying the loose end
    /// and where it was dropped. What the shell puts there is the module list,
    /// narrowed to what could actually take the wire — and whatever is picked
    /// arrives already plugged in.
    /// </summary>
    public event EventHandler<WireDrop>? WireDropped;

    public Patch Patch
    {
        get => patch;
        set
        {
            patch = value;

            // The last gate before a patch is shown. Presets, files and the
            // assistant all place one already; this is what makes "every patch
            // has an Output" true of anything that reaches the canvas, rather
            // than true of each route to it separately.
            patch.EnsureOutput();

            // And the same gate for where its modules stand. A file written
            // before the canvas was bounded, or by hand, may put one half off
            // the edge — brought in here, before the history opens on it, so
            // that what a patch was opened as is a patch that fits.
            HoldInside();

            // A different document rather than an edit to this one, so what
            // came before it is not something to undo into.
            history.Opened(patch);

            selection.Clear();
            focus = null;
            drag = Drag.None;
            FrameAll();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            PatchChanged?.Invoke(this, EventArgs.Empty);
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// The module the inspector is about — the last one the pointer named, of
    /// however many are selected. Null when nothing is.
    /// </summary>
    public NodeInstance? SelectedNode => focus is { } id ? patch.Find(id) : null;

    /// <summary>
    /// Every selected module, in the order the patch holds them so that what
    /// comes out of a selection reads the same way twice. Empty when nothing is
    /// selected, and one entry deep for the ordinary click.
    /// </summary>
    public IReadOnlyList<NodeInstance> SelectedNodes =>
        [.. patch.Nodes.Where(node => selection.Contains(node.Id))];

    public bool CanUndo => history.CanUndo;

    public bool CanRedo => history.CanRedo;

    /// <summary>
    /// Whether the patch differs from the one that was opened, or from the last
    /// one written out. Undoing back to where it started clears it again, since
    /// what is being compared is the document rather than whether anybody typed.
    /// </summary>
    public bool IsModified => history.IsModified;

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

        history.Record(patch, coalesce);
        InvalidateVisual();
        PatchChanged?.Invoke(this, EventArgs.Empty);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Puts the patch back as it was before the last edit.</summary>
    /// <returns>Whether there was one.</returns>
    public bool Undo() => Restore(history.Undo());

    /// <summary>Puts back the edit the last undo took away.</summary>
    public bool Redo() => Restore(history.Redo());

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
        Announce();

        return true;
    }

    /// <summary>
    /// Shows a patch built from the one that was open rather than opened in its
    /// place — the assistant's work, which is a large edit and not a new
    /// document. Recorded like any other edit, so one press of undo puts back
    /// what was there before it, and one of redo brings it round again.
    /// </summary>
    /// <remarks>
    /// Framed, which an undo is not. Nothing about where an assistant lays its
    /// modules out has to resemble the current canvas, so the patch does not end
    /// up pointing at an empty section of the grid.
    /// </remarks>
    public void ApplyEdit(Patch edited)
    {
        Show(edited);
        history.Record(patch);
        FrameAll();
        Announce();
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

        drag = Drag.None;
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
    /// Drops a new module on the canvas and hands it back, or returns null
    /// having added nothing where the patch may not hold another of that module
    /// — the Output, of which there is always exactly one. Rather than do
    /// nothing at all, that case selects the one already there: whoever asked
    /// for it wanted it, and this is where it is.
    /// </summary>
    /// <param name="typeId">Which module to add.</param>
    /// <param name="at">
    /// Where to centre it, in graph space. The middle of the view when nothing
    /// says otherwise — which is what a module added from anywhere but the
    /// canvas gets, since nowhere else has a place in mind.
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
    /// edit — the module and the wire arrived in one gesture and come back the
    /// same way.
    /// </summary>
    /// <remarks>
    /// Which socket it lands on is <see cref="Fitting"/>'s decision. Nothing is
    /// refused for being the wrong kind, because nothing is: the compiler
    /// broadcasts a scalar to three channels and takes luma from a color, so
    /// every socket accepts every wire and the question is only which one was
    /// meant.
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
    /// <para>
    /// The port a module is <em>about</em> comes first:
    /// <see cref="PortSpec.Domain"/> is the axis it is read across and
    /// <see cref="PortSpec.Swept"/> is what it reads under a domain of its own,
    /// and both are the socket the module exists to have something in. The
    /// compiler already says as much — it warns about a Domain port left on its
    /// knob, and about no other.
    /// </para>
    /// <para>
    /// Then an exact match of kind, which is what tells a Scan's <c>view</c>
    /// from its <c>out</c> when a color was wanted, and puts a scalar into a
    /// Blend's <c>t</c> rather than broadcasting it to grey down <c>a</c>.
    /// </para>
    /// <para>
    /// Then the first socket, which is where this would land anyway: the
    /// catalogue is written with the principal one first, and the two rules
    /// above agree with it almost everywhere. They are here for the almost.
    /// </para>
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
    /// Removes every selected module except the Output, which the graph refuses.
    /// A refused module is left selected rather than cleared: pressing Delete on
    /// the Output should do nothing at all, and losing the selection would take
    /// its settings panel away with it.
    /// </summary>
    /// <remarks>
    /// One edit however many modules go, because one gesture asked for all of
    /// them — the same reason laying out is one edit (ADR-0044). Deleting five
    /// and undoing them one at a time would be five presses for something
    /// nobody did five times.
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

    // --- copy and paste ------------------------------------------------------

    /// <summary>
    /// Puts the selected modules on the system clipboard, as the JSON a patch is
    /// saved as. Nothing happens where the selection holds nothing that can be
    /// copied — the Output alone, or an empty canvas — rather than the clipboard
    /// being emptied by a gesture that found nothing.
    /// </summary>
    /// <remarks>
    /// The system clipboard rather than a buffer of this program's own, because
    /// a copy that cannot leave the window is not really one: what this writes is
    /// a patch file, so it pastes into another Flyback, and into a text editor as
    /// something readable. See ADR-0045.
    /// </remarks>
    /// <returns>What to say about it, or null where there is nothing to say.</returns>
    public async Task<string?> CopySelectionAsync()
    {
        if (selection.Count == 0) return null;
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return null;

        var fragment = PatchClipboard.Copy(patch, selection);

        // Selected, and yet none of it can be copied — which can only be the
        // Output on its own. Said, because a gesture that silently does nothing
        // reads as a broken one.
        if (fragment.Nodes.Count == 0) return "The Output cannot be copied.";

        await clipboard.SetTextAsync(PatchIO.ToJson(fragment, NodeCatalog.Current));
        return null;
    }

    /// <summary>
    /// Copies the selection and then deletes it. Nothing is deleted where
    /// nothing could be copied, so a cut that fails leaves the patch alone.
    /// </summary>
    public async Task<string?> CutSelectionAsync()
    {
        var trouble = await CopySelectionAsync();
        if (trouble is null && selection.Count > 0) DeleteSelected();

        return trouble;
    }

    /// <summary>
    /// Reads a patch off the clipboard and merges it in, centred on the view,
    /// with what arrived left selected so it can be dragged straight into place.
    /// </summary>
    /// <remarks>
    /// One edit, so one Ctrl+Z takes the whole paste back. The text is read the
    /// way a file is — a fragment naming a module this build has not got is
    /// refused with the sentence <see cref="PatchLoad.Summary"/> already words,
    /// rather than pasted with holes in it.
    /// </remarks>
    /// <returns>What to say about it, or null where there is nothing to say.</returns>
    public async Task<string?> PasteAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return null;

        var text = await clipboard.TryGetTextAsync();
        if (string.IsNullOrWhiteSpace(text)) return null;

        PatchLoad loaded;

        try
        {
            loaded = PatchIO.Read(text, NodeCatalog.Current);
        }
        catch (Exception)
        {
            // Whatever was on the clipboard was not a patch. Said plainly and
            // without the parser's own wording, because the ordinary way to
            // reach this is having copied something else entirely.
            return "Nothing to paste: the clipboard does not hold a patch.";
        }

        if (!loaded.IsComplete) return $"Not pasted. {loaded.Summary}";

        AddFragment(loaded.Patch);
        return null;
    }

    /// <summary>
    /// Merges a fragment into the patch and leaves what arrived selected, so it
    /// can be dragged straight into place.
    /// </summary>
    /// <remarks>
    /// The graph half of this is <see cref="PatchClipboard.Paste"/> and knows
    /// nothing about a canvas; what is here is the two things that need one —
    /// where it lands and what is selected afterwards. One edit either way, so
    /// one Ctrl+Z takes the whole of it back.
    /// <para>
    /// Where it lands is the whole difference between the two ways in. A paste
    /// has no point of its own and goes to the middle of the view, stepped clear
    /// of what is already there; something picked out of the module list was
    /// picked <em>somewhere</em>, and lands there exactly as a module does — see
    /// <see cref="AddNode"/>.
    /// </para>
    /// </remarks>
    /// <param name="fragment">What to add. Not modified, so the same one may be added again.</param>
    /// <param name="at">Where its middle should land, or null for the middle of the view.</param>
    public IReadOnlyList<NodeInstance> AddFragment(Patch fragment, Point? at = null)
    {
        var arriving = fragment.Nodes.Where(n => !NodeCatalog.IsSink(n.TypeId)).ToArray();
        if (arriving.Length == 0) return [];

        var box = BoxAround(arriving);

        var (dx, dy) = at is { } point
            ? (point.X - box.Center.X, point.Y - box.Center.Y)
            : WhereToPaste(arriving);

        var added = PatchClipboard.Paste(patch, fragment, dx, dy);
        if (added.Count == 0) return [];

        selection.Clear();
        foreach (var node in added) selection.Add(node.Id);
        focus = added[^1].Id;

        SelectionChanged?.Invoke(this, EventArgs.Empty);
        NotifyPatchChanged();

        return added;
    }
    /// <summary>
    /// How far to shift what is arriving so that it lands in the middle of what
    /// is on screen, clear of anything already there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The middle of the view rather than where the modules were copied from,
    /// which is the same choice <see cref="AddNode"/> makes and for the same
    /// reason: a paste has to arrive somewhere it can be seen, and where it came
    /// from may be a screen away.
    /// </para>
    /// <para>
    /// Then stepped down and right until it is not sitting on anything. Landing
    /// on top of what is already there reads as nothing having happened, and it
    /// is the ordinary case rather than the rare one — the middle of the view is
    /// where the patch is. The step is capped because a dense enough patch has
    /// no clear middle at all, and walking off the edge looking for one would be
    /// worse than overlapping: what arrives is selected, and dragging it
    /// somewhere better is one gesture.
    /// </para>
    /// </remarks>
    private (double X, double Y) WhereToPaste(IReadOnlyList<NodeInstance> arriving)
    {
        const double step = 28;
        const int tries = 40;

        var group = BoxAround(arriving);
        var taken = patch.Nodes.Select(node => BoxAround([node])).ToArray();

        var centre = ToGraph(new Point(Bounds.Width / 2, Bounds.Height / 2));
        var dx = centre.X - group.Center.X;
        var dy = centre.Y - group.Center.Y;

        for (var s = 0; s < tries; s++)
        {
            // Inflated, so that "clear of" means with room to see the edge
            // rather than merely not overlapping by a pixel.
            var moved = group.Translate(new Vector(dx, dy)).Inflate(step);
            if (!taken.Any(box => box.Intersects(moved))) break;

            dx += step;
            dy += step;
        }

        return (dx, dy);
    }

    /// <summary>The one rectangle that holds all of these modules.</summary>
    private static Rect BoxAround(IReadOnlyList<NodeInstance> nodes)
    {
        double left = double.MaxValue, top = double.MaxValue;
        double right = double.MinValue, bottom = double.MinValue;

        foreach (var node in nodes)
        {
            // A module whose plugin is missing has no height to ask for. Counted
            // at nothing rather than skipped, so its corner still keeps a paste
            // off it.
            var height = NodeCatalog.Get(node.TypeId) is { } def ? NodeGeometry.Height(def) : 0;

            left = Math.Min(left, node.X);
            top = Math.Min(top, node.Y);
            right = Math.Max(right, node.X + NodeGeometry.Width);
            bottom = Math.Max(bottom, node.Y + height);
        }

        return left > right ? default : new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Lays the patch out so it reads left to right with its wires clear of one
    /// another, and frames the result. One edit, so one Ctrl+Z puts every node
    /// back where it was.
    /// </summary>
    /// <remarks>
    /// Only coordinates change — no wire is added, removed or rerouted — so the
    /// patch compiles to exactly the same program before and after, and the
    /// picture and the sound are untouched. See ADR-0044.
    /// </remarks>
    public void Tidy()
    {
        if (patch.Nodes.Count == 0) return;

        PatchLayout.Arrange(patch, NodeCatalog.Current, NodeGeometry.Metrics);

        NotifyPatchChanged();
        FrameAll();
    }

    /// <summary>
    /// Fits every node into view. A patch is usually loaded before the control
    /// has been measured, so this defers until there is a viewport to fit into.
    /// </summary>
    public void FrameAll()
    {
        if (Bounds.Width < 1 || Bounds.Height < 1)
        {
            framePending = true;
            return;
        }

        framePending = false;

        if (patch.Nodes.Count == 0)
        {
            zoom = 1;
            PanTo(new Point(40, 40));
            InvalidateVisual();
            return;
        }

        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;

        foreach (var node in patch.Nodes)
        {
            var def = NodeCatalog.Get(node.TypeId);
            if (def is null) continue;

            var bounds = NodeGeometry.Bounds(node, def);
            left = Math.Min(left, bounds.Left);
            top = Math.Min(top, bounds.Top);
            right = Math.Max(right, bounds.Right);
            bottom = Math.Max(bottom, bounds.Bottom);
        }

        if (left > right) return;

        const double margin = 50;
        var scaleX = Bounds.Width / (right - left + margin * 2);
        var scaleY = Bounds.Height / (bottom - top + margin * 2);

        zoom = Math.Clamp(Math.Min(scaleX, scaleY), 0.2, 1.4);

        // Centred on what it is framing, and then held inside the canvas — so a
        // patch built hard against an edge is pushed off centre rather than
        // being centred over ground the view is not allowed to be on. It stays
        // wholly in sight either way: the view is wider than what it frames,
        // and holding it only ever slides it back towards the middle.
        PanTo(new Point(
            (Bounds.Width - (right - left) * zoom) / 2 - left * zoom,
            (Bounds.Height - (bottom - top) * zoom) / 2 - top * zoom));

        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        if (framePending)
        {
            FrameAll();
            return;
        }

        // A window pulled wider shows more canvas without the view having
        // moved, which is the one way to end up outside it without panning.
        PanTo(pan);
        InvalidateVisual();
    }

    /// <summary>
    /// Moves the view, held so that it never leaves the canvas.
    /// </summary>
    /// <remarks>
    /// Every pan goes through here, the one a zoom performs included: zooming
    /// out in a corner walks the view outwards as surely as dragging it does,
    /// and a guard on the drag alone is one the wheel steps straight past.
    /// <para>
    /// What it holds is the view rather than its centre, so the far side of the
    /// canvas comes to the far side of the window and stops. The reach is a
    /// little wider than <see cref="NodeInstance.Extent"/>, because that holds a
    /// module's corner and its body hangs below and to the right of it — a view
    /// stopped on the coordinate itself would cut the last module in half and
    /// refuse to show the rest.
    /// </para>
    /// <para>
    /// A view wider than the canvas cannot be held inside it, so it is centred
    /// on it instead — and that is a real case rather than a defensive one. The
    /// zoom stops at a fifth, which puts five windows' worth of units across the
    /// view, so any window past about two thousand pixels can see the whole
    /// canvas at once with room to spare. There is then nowhere to pan to, and
    /// the canvas sits in the middle of the window where it belongs.
    /// </para>
    /// </remarks>
    private void PanTo(Point to)
    {
        pan = new Point(Held(to.X, Bounds.Width), Held(to.Y, Bounds.Height));

        double Held(double offset, double viewport)
        {
            var edge = ViewReach * zoom;

            return viewport > edge * 2
                ? viewport / 2
                : Math.Clamp(offset, viewport - edge, edge);
        }
    }

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

            DrawPendingWire(context);
        }

        // Outside the transform, so a hairline stays a hairline and the dashes
        // keep their spacing however far out the view is zoomed. These are drawn
        // on the canvas rather than in it — neither is part of the patch.
        DrawEdge(context);
        DrawMarquee(context);
    }

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
    /// The rectangle between two corners, whichever way round they are.
    /// </summary>
    /// <remarks>
    /// Built by hand rather than from <c>new Rect(a, b)</c>, which takes the
    /// first point as the top left and subtracts. Started from any corner but
    /// the top left that gives a negative width or height — a rectangle which
    /// draws nothing and intersects nothing, so the band would appear to do
    /// nothing at all.
    /// </remarks>
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
    /// The modules being dragged, empty while none are. A set rather than one
    /// id because a drag may carry a whole selection, and a wire between two of
    /// its members is as much in play as one leaving it.
    /// </param>
    /// <param name="theirs">
    /// Which half of the wires this pass draws: those modules' own, or all the
    /// rest. One loop serves both, so a wire cannot be drawn twice or missed
    /// entirely — the two passes partition the same set rather than each
    /// deciding for themselves what belongs in it.
    /// </param>
    private void DrawConnections(DrawingContext context, IReadOnlySet<Guid> lifted, bool theirs)
    {
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

            // Heavier and at full strength, which is the same signal the pending
            // wire gives: this one is in play.
            var pen = theirs
                ? new Pen(new SolidColorBrush(color), LiftedWireThickness)
                : new Pen(new SolidColorBrush(color, RestingWireOpacity), WireThickness);

            DrawWire(context, from, to, pen);
        }
    }

    /// <summary>
    /// Where the wire being dragged is anchored, and null when none is.
    /// </summary>
    /// <remarks>
    /// What <see cref="DrawPendingWire"/> draws from, rather than a second
    /// reading of the same question: the anchor is a port that may be behind a
    /// box, so it has to come through the anchors like every other wire, and one
    /// of the two going back to <see cref="NodeGeometry"/> directly is exactly
    /// the bug this had. One of them can be tested and the other cannot, so they
    /// are the same one.
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
        // the two are handed to DrawWire in whichever order makes the curve leave
        // an output and arrive at an input.
        if (wireFromOutput) DrawWire(context, anchor, wireEnd, pen);
        else DrawWire(context, wireEnd, anchor, pen);
    }

    /// <summary>A horizontal-tangent bezier, so wires leave and enter sockets cleanly.</summary>
    private static void DrawWire(DrawingContext context, Point from, Point to, IPen pen)
    {
        var reach = Math.Max(45, Math.Abs(to.X - from.X) * 0.5);

        var geometry = new StreamGeometry();
        using (var sink = geometry.Open())
        {
            sink.BeginFigure(from, false);
            sink.CubicBezierTo(from.WithX(from.X + reach), to.WithX(to.X - reach), to);
            sink.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private void DrawNode(DrawingContext context, NodeInstance node, NodeDef def)
    {
        var bounds = NodeGeometry.Bounds(node, def);
        var isSelected = selection.Contains(node.Id);
        var accent = Colors.Accent(def.Category);

        context.DrawRectangle(
            isSelected ? NodeFillSelected : NodeFill,
            !isSelected ? NodeBorder : focus == node.Id ? SelectionPen : SelectionPenSecondary,
            new RoundedRect(bounds, NodeGeometry.CornerRadius));

        // Header band, square at the bottom so it reads as a title bar.
        var header = new Rect(bounds.X, bounds.Y, bounds.Width, NodeGeometry.HeaderHeight);
        context.DrawRectangle(
            new SolidColorBrush(accent, 0.85),
            null,
            new RoundedRect(header, NodeGeometry.CornerRadius, NodeGeometry.CornerRadius, 0, 0));

        context.DrawText(
            Text(node.Title(def), 12.5, HeaderTextBrush, bounds.Width - 16, true),
            new Point(bounds.X + 9, bounds.Y + 5));

        for (var i = 0; i < def.Outputs.Count; i++)
        {
            var port = def.Outputs[i];
            var centre = NodeGeometry.OutputPort(node, i);
            var label = Text(port.Name, 11.5, LabelBrush, bounds.Width - 24, true);

            context.DrawText(label, new Point(bounds.Right - 14 - label.Width, centre.Y - label.Height / 2));
            DrawPort(context, centre, port.Kind);
        }

        for (var i = 0; i < def.Inputs.Count; i++)
        {
            var port = def.Inputs[i];
            var centre = NodeGeometry.InputPort(node, def, i);
            var connected = patch.IncomingTo(node.Id, i) is not null;

            var label = Text(port.Name, 11.5, LabelBrush, bounds.Width * 0.55, true);
            context.DrawText(label, new Point(bounds.X + 14, centre.Y - label.Height / 2));

            // An unconnected input shows what it will compile to: the module
            // normalled to it where there is one — no wire is drawn for a wire
            // that is not in the patch — and otherwise the knob value.
            if (!connected && NodeCatalog.Normalled(port) is { } source)
            {
                // Wider than the column a number gets, because this is a module
                // name and a qualified one at that — "Coordinates x" does not
                // fit where "0.25" does, and trimmed to "Coordinates…" it would
                // stop telling x from y.
                var name = Text(source, 11.5, NormalBrush, bounds.Width * 0.5, true);
                context.DrawText(name, new Point(bounds.Right - 12 - name.Width, centre.Y - name.Height / 2));
            }
            else if (!connected && i < node.InputValues.Length)
            {
                var value = Text(port.Format(node.InputValues[i]), 11.5, ValueBrush, bounds.Width * 0.4, true);
                context.DrawText(value, new Point(bounds.Right - 12 - value.Width, centre.Y - value.Height / 2));
            }

            DrawPort(context, centre, port.Kind);
        }
    }

    // --- groups ---------------------------------------------------------------
    //
    // Everything below is drawing and pointing. Nothing here touches the graph:
    // a collapsed box is several modules that are not being painted and one that
    // is, and every wire still runs between exactly the modules it always ran
    // between. See NodeGroup.

    /// <summary>
    /// Every group that is currently a box, with the sockets it shows and the
    /// room it takes up.
    /// </summary>
    /// <remarks>
    /// Worked out afresh each time rather than kept: the sockets come off the
    /// wires and their order comes off where the modules sit, so a cache would
    /// have to be dropped on every wire drawn, every module moved and every undo
    /// — three chances to forget, to save arithmetic over a few dozen wires.
    /// </remarks>
    private IEnumerable<(NodeGroup Group, GroupSockets Sockets, Rect Bounds)> Boxes()
    {
        if (patch.Groups is null) yield break;

        foreach (var group in patch.Groups)
        {
            if (!group.Collapsed) continue;

            var sockets = patch.SocketsOf(group);
            var bounds = NodeGeometry.GroupBounds(patch, group, sockets);

            if (bounds.Width > 0) yield return (group, sockets, bounds);
        }
    }

    /// <summary>Whether this module is inside a box, and so is not drawn itself.</summary>
    private bool Shut(Guid nodeId) => patch.CollapsedGroupOf(nodeId) is not null;

    /// <summary>Whether both ends of a wire are inside the same box.</summary>
    private bool Hidden(Connection wire) =>
        patch.CollapsedGroupOf(wire.SourceNode) is { } group
        && ReferenceEquals(patch.CollapsedGroupOf(wire.TargetNode), group);

    /// <summary>
    /// Where an output is to be reached, which is the box standing in front of it
    /// where one is and the module itself where none is.
    /// </summary>
    /// <remarks>
    /// The one seam the whole feature hangs on. Painting, hit-testing and wire
    /// dragging all ask this rather than <see cref="NodeGeometry"/> directly, so
    /// none of them has to know that a box can exist — a socket on a box names a
    /// module and a port, so what comes back is still an answer about the module,
    /// only somewhere else on the screen.
    /// </remarks>
    private Point OutputAnchor(NodeInstance node, int port)
    {
        if (patch.CollapsedGroupOf(node.Id) is { } group)
        {
            var sockets = patch.SocketsOf(group);
            var row = sockets.IndexOfOutput(new GroupSocket(node.Id, port, IsOutput: true));

            if (row >= 0)
                return NodeGeometry.GroupOutputPort(
                    NodeGeometry.GroupBounds(patch, group, sockets), row);
        }

        return NodeGeometry.OutputPort(node, port);
    }

    /// <inheritdoc cref="OutputAnchor"/>
    private Point InputAnchor(NodeInstance node, NodeDef def, int port)
    {
        if (patch.CollapsedGroupOf(node.Id) is { } group)
        {
            var sockets = patch.SocketsOf(group);
            var row = sockets.IndexOfInput(new GroupSocket(node.Id, port, IsOutput: false));

            if (row >= 0)
                return NodeGeometry.GroupInputPort(
                    NodeGeometry.GroupBounds(patch, group, sockets), sockets, row);
        }

        return NodeGeometry.InputPort(node, def, port);
    }

    private NodeGroup? HitBox(Point graph)
    {
        foreach (var (group, _, bounds) in Boxes())
            if (bounds.Contains(graph))
                return group;

        return null;
    }

    private NodeGroup? HitOpenGroupHandle(Point graph)
    {
        if (patch.Groups is null) return null;

        foreach (var group in patch.Groups)
            if (OpenGroup(group) is var (_, handle) && handle.Contains(graph))
                return group;

        return null;
    }

    /// <summary>
    /// How many of the selected modules a group would actually take, which is
    /// every one of them but the sink.
    /// </summary>
    /// <remarks>
    /// The same count the delete button shows and for the same reason: a gesture
    /// that says it will take six and takes five is lying about what it does.
    /// Selecting everything and grouping it is the case that makes the
    /// difference, because Ctrl+A takes the Output too.
    /// <para>
    /// A module already in another group is counted, because it is taken: it
    /// leaves the group it was in, since two boxes both claiming to draw one
    /// module is a picture that means nothing. This has to agree with
    /// <see cref="Patch.Group"/> exactly or the label is the lie above.
    /// </para>
    /// </remarks>
    public int Groupable => SelectedNodes.Count(n => !NodeCatalog.IsSink(n.TypeId));

    /// <summary>
    /// The group the selection is exactly, and null where it is anything else —
    /// which is what tells "ungroup this" from "group these".
    /// </summary>
    public NodeGroup? SelectedGroup
    {
        get
        {
            if (patch.Groups is null || selection.Count == 0) return null;

            foreach (var group in patch.Groups)
                if (group.Members.Count == selection.Count && group.Members.All(selection.Contains))
                    return group;

            return null;
        }
    }

    /// <summary>Draws the selected modules as one box.</summary>
    public void GroupSelected()
    {
        if (patch.Group(selection) is not { } made)
        {
            // Said rather than ignored, but only where something was actually
            // selected: a key pressed on an empty canvas is a key pressed by
            // mistake, and the status bar is not the place to point that out.
            if (Groupable > 0)
                Reported?.Invoke(
                    this,
                    $"A group needs {NodeGroup.Fewest} modules or more — a box round one "
                    + "would be the module again with a second name.");

            return;
        }

        // The box stands for what was selected, and what was selected is what
        // stays selected — so the panel goes on showing the same modules and a
        // second Ctrl+G has something to ungroup.
        selection.Clear();
        foreach (var id in made.Members) selection.Add(id);

        focus = made.Members[^1];

        SelectionChanged?.Invoke(this, EventArgs.Empty);
        NotifyPatchChanged();
        Reported?.Invoke(
            this,
            $"Grouped {made.Members.Count} modules. "
            + "Ctrl+Shift+G ungroups them, double-click opens them.");
    }

    /// <summary>
    /// Stops drawing whatever groups the selection is inside, leaving every
    /// module and every wire exactly where they were.
    /// </summary>
    public void UngroupSelected()
    {
        if (patch.Groups is null || selection.Count == 0) return;

        var going = patch.Groups
            .Where(g => g.Members.Any(selection.Contains))
            .Select(g => g.Id)
            .ToArray();

        if (going.Length == 0) return;

        foreach (var id in going) patch.Ungroup(id);

        NotifyPatchChanged();
        Reported?.Invoke(this, going.Length == 1 ? "Ungrouped." : $"Ungrouped {going.Length} groups.");
    }

    /// <summary>Shuts a group that is open, or opens one that is shut.</summary>
    public void ToggleBox(NodeGroup group)
    {
        group.Collapsed = !group.Collapsed;

        // Opening one selects what came out, so the panel is about the modules
        // rather than about a box that is no longer there.
        if (!group.Collapsed)
        {
            selection.Clear();
            foreach (var id in group.Members) selection.Add(id);

            focus = group.Members.Count == 0 ? null : group.Members[^1];
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        NotifyPatchChanged();
    }

    /// <summary>Toggles whatever group the selection is exactly, if it is one.</summary>
    public void ToggleSelectedGroup()
    {
        if (SelectedGroup is { } group) ToggleBox(group);
    }

    /// <summary>
    /// Takes a socket off a box's edge.
    /// </summary>
    /// <remarks>
    /// Refused while a wire is on it, and not out of caution: a crossing wire is
    /// a socket whatever the stored list says, so this would appear to do nothing
    /// and the row would still be there afterwards. Unplug it first, which is the
    /// order the panel offers them in anyway.
    /// </remarks>
    public void HideSocket(NodeGroup group, GroupSocket socket)
    {
        if (patch.Wired(group, socket)) return;
        if (!group.Hide(socket)) return;

        NotifyPatchChanged();
    }

    /// <summary>
    /// A press on a box, or on the strip above an open group: what is inside is
    /// what gets selected.
    /// </summary>
    private void PressGroup(NodeGroup group, bool adding)
    {
        pendingNarrow = null;

        if (!adding) selection.Clear();

        foreach (var id in group.Members) selection.Add(id);

        focus = group.Members.Count == 0 ? null : group.Members[^1];
        SelectionChanged?.Invoke(this, EventArgs.Empty);

        foreach (var moving in SelectedNodes) BringToFront(moving);

        drag = Drag.Node;

        dragOrigins.Clear();
        foreach (var moving in SelectedNodes)
            dragOrigins[moving.Id] = new Point(moving.X, moving.Y);
    }

    /// <summary>
    /// The outline drawn round a group that is open, and the strip above it that
    /// shuts it again.
    /// </summary>
    /// <remarks>
    /// An open group has no box, so without this there would be nothing on the
    /// canvas to say one was there and no way back to the box but the inspector.
    /// The strip is where a double-click lands, which is the same gesture that
    /// opened it — a thing that opens by being double-clicked should shut the
    /// same way.
    /// </remarks>
    private (Rect Outline, Rect Handle)? OpenGroup(NodeGroup group)
    {
        if (group.Collapsed) return null;

        var x = double.MaxValue;
        var y = double.MaxValue;
        var right = double.MinValue;
        var bottom = double.MinValue;

        foreach (var id in group.Members)
            if (patch.Find(id) is { } node && NodeCatalog.Get(node.TypeId) is { } def)
            {
                var bounds = NodeGeometry.Bounds(node, def);

                x = Math.Min(x, bounds.X);
                y = Math.Min(y, bounds.Y);
                right = Math.Max(right, bounds.Right);
                bottom = Math.Max(bottom, bounds.Bottom);
            }

        if (x == double.MaxValue) return null;

        var outline = new Rect(x, y, right - x, bottom - y).Inflate(OpenGroupPadding);
        var handle = new Rect(outline.X, outline.Y - OpenGroupHandle, outline.Width, OpenGroupHandle);

        return (outline, handle);
    }

    private const double OpenGroupPadding = 24;
    private const double OpenGroupHandle = 20;

    /// <summary>
    /// The ring, the ground inside it and the title above it, for every group
    /// that is open.
    /// </summary>
    /// <remarks>
    /// A wash as well as a line, because a line alone out here is nearly
    /// nothing: this is drawn under the wires and the modules, on ground a
    /// person is reading past rather than at. Both are held faint. What an open
    /// group has to do is say where it is while somebody works inside it, and a
    /// region that draws the eye harder than the modules standing in it is a
    /// region in the way of the work.
    /// <para>
    /// The strip is left bare, and the title on it stays the muted grey the rest
    /// of the canvas furniture is written in. Filling it made a header, and a
    /// header is what a group wears when it is <em>shut</em> — a second one up
    /// here reads as a box that is somehow both.
    /// </para>
    /// </remarks>
    private void DrawOpenGroups(DrawingContext context)
    {
        if (patch.Groups is null) return;

        foreach (var group in patch.Groups)
        {
            if (OpenGroup(group) is not var (outline, handle)) continue;

            // Selected when its modules are, which is the rule a shut box uses —
            // and it is the same gesture that selects them, since pressing the
            // strip takes the group.
            var isSelected = group.Members.Count > 0 && group.Members.All(selection.Contains);

            context.DrawRectangle(
                OpenGroupFill,
                isSelected ? OpenGroupPenSelected : OpenGroupPen,
                new RoundedRect(outline, NodeGeometry.CornerRadius));

            var label = Text(group.Title(), 11.5, NormalBrush, outline.Width - 12, true);
            context.DrawText(label, new Point(handle.X + 6, handle.Y + (handle.Height - label.Height) / 2));
        }
    }

    private void DrawBox(DrawingContext context, NodeGroup group, GroupSockets sockets, Rect bounds)
    {
        // Selected when its modules are, because pressing the box is what selects
        // them — there is nothing else it could mean for a box to be picked.
        var isSelected = group.Members.Count > 0 && group.Members.All(selection.Contains);

        context.DrawRectangle(
            isSelected ? NodeFillSelected : NodeFill,
            isSelected ? SelectionPenSecondary : NodeBorder,
            new RoundedRect(bounds, NodeGeometry.CornerRadius));

        var header = new Rect(bounds.X, bounds.Y, bounds.Width, NodeGeometry.HeaderHeight);
        context.DrawRectangle(
            GroupHeaderFill,
            null,
            new RoundedRect(header, NodeGeometry.CornerRadius, NodeGeometry.CornerRadius, 0, 0));

        context.DrawText(
            Text(group.Title(), 12.5, HeaderTextBrush, bounds.Width - 16, true),
            new Point(bounds.X + 9, bounds.Y + 5));

        for (var i = 0; i < sockets.Outputs.Count; i++)
            DrawBoxSocket(context, sockets.Outputs[i], NodeGeometry.GroupOutputPort(bounds, i), bounds);

        for (var i = 0; i < sockets.Inputs.Count; i++)
            DrawBoxSocket(
                context, sockets.Inputs[i], NodeGeometry.GroupInputPort(bounds, sockets, i), bounds);
    }

    /// <summary>
    /// One socket of a box, named for the port inside that it stands for.
    /// </summary>
    /// <remarks>
    /// "filter.cutoff" rather than a name of its own, and that is deliberate: the
    /// socket is a way of pointing at an inner port and reads as one. It also
    /// means renaming a module inside relabels the box for nothing, which is the
    /// whole of how a group gets a readable edge.
    /// </remarks>
    private void DrawBoxSocket(DrawingContext context, GroupSocket socket, Point centre, Rect bounds)
    {
        if (Named(socket) is not var (label, spec)) return;

        var text = Text(label, 11.5, LabelBrush, bounds.Width - 26, true);

        context.DrawText(
            text,
            socket.IsOutput
                ? new Point(bounds.Right - 14 - text.Width, centre.Y - text.Height / 2)
                : new Point(bounds.X + 14, centre.Y - text.Height / 2));

        DrawPort(context, centre, spec.Kind);
    }

    /// <summary>
    /// What a socket is called and what it is, or null where it names a module or
    /// a port that is not there.
    /// </summary>
    /// <remarks>
    /// "filter.cutoff" rather than a name of its own, and that is deliberate: the
    /// socket is a way of pointing at an inner port and reads as one. It also
    /// means renaming a module inside relabels the box for nothing, which is the
    /// whole of how a group gets a readable edge.
    /// </remarks>
    public (string Label, PortSpec Spec)? Named(GroupSocket socket)
    {
        if (patch.Find(socket.Node) is not { } node) return null;
        if (NodeCatalog.Get(node.TypeId) is not { } def) return null;

        var ports = socket.IsOutput ? def.Outputs : def.Inputs;
        if (socket.Port >= ports.Count) return null;

        return ($"{node.Title(def)}.{ports[socket.Port].Name}", ports[socket.Port]);
    }

    private static void DrawPort(DrawingContext context, Point centre, PortKind kind) =>
        context.DrawEllipse(
            new SolidColorBrush(Colors.PortColor(kind)),
            PortOutline,
            centre,
            NodeGeometry.PortRadius,
            NodeGeometry.PortRadius);

    private static FormattedText Text(string text, double size, IBrush brush, double maxWidth, bool trim)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            size,
            brush);

        if (trim)
        {
            formatted.MaxTextWidth = maxWidth;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }

        return formatted;
    }

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
            // than about adding another one beside it.
            if (!HitPort(graph, out _, out _, out _) && HitNode(graph) is null)
                MenuRequested?.Invoke(this, graph);

            return;
        }

        if (!properties.IsLeftButtonPressed) return;

        // Ctrl means two things, and which one depends entirely on what is under
        // the pointer: over a module it adds to the selection, over an output it
        // lifts the wire off. They never meet — a press is over one or the other
        // — so one modifier serves both without either having to know.
        var ctrl = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;

        if (HitPort(graph, out var portNode, out var portIndex, out var isOutput))
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

            if (moved)
            {
                history.Record(patch);
                HistoryChanged?.Invoke(this, EventArgs.Empty);
            }

            // A press on one module of a group that turned out not to be a drag
            // was a click, and a click picks that module out of the group.
            else if (pendingNarrow is { } one) Select(one);
        }

        pendingNarrow = null;
        dragOrigins.Clear();
        marqueeBase.Clear();

        drag = Drag.None;

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

                case Key.X:
                    Clipboard(CutSelectionAsync);
                    e.Handled = true;
                    return;

                case Key.V:
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
                case Key.G:
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
            case Key.Delete or Key.Back:
                DeleteSelected();
                e.Handled = true;
                break;

            // The module list, from the keyboard. Opened where the pointer last
            // was, so it lands under the hand the way the right-click does —
            // and in the middle of the view when the pointer has never been
            // over the canvas at all.
            case Key.Space:
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

    // --- hit testing (all in graph space) ------------------------------------

    private NodeInstance? HitNode(Point graph)
    {
        for (var i = patch.Nodes.Count - 1; i >= 0; i--)
        {
            var node = patch.Nodes[i];
            var def = NodeCatalog.Get(node.TypeId);

            // A module inside a shut box is not on the canvas, so nothing can
            // land on it. Without this a click would reach a module it cannot
            // see and drag it out from under the box drawn over it.
            if (Shut(node.Id)) continue;

            if (def is not null && NodeGeometry.Bounds(node, def).Contains(graph))
                return node;
        }

        return null;
    }

    /// <summary>
    /// Which port is under the pointer, whether it is drawn on a module or on the
    /// box standing in front of one.
    /// </summary>
    /// <remarks>
    /// A box's socket answers with the module and port it stands for, so
    /// everything downstream of this — starting a wire, lifting one, dropping one
    /// — goes on working on the graph without ever learning that groups exist.
    /// That is the whole dividend of a socket being a pointer rather than a port
    /// of its own.
    /// </remarks>
    private bool HitPort(Point graph, out Guid nodeId, out int portIndex, out bool isOutput)
    {
        var tolerance = NodeGeometry.PortRadius + NodeGeometry.HitPadding;

        // Boxes first, because they are painted over the modules they stand for
        // and a click should reach whatever is on top.
        foreach (var (_, sockets, bounds) in Boxes())
        {
            for (var p = 0; p < sockets.Outputs.Count; p++)
            {
                if (!Near(NodeGeometry.GroupOutputPort(bounds, p), graph, tolerance)) continue;

                var socket = sockets.Outputs[p];
                (nodeId, portIndex, isOutput) = (socket.Node, socket.Port, true);
                return true;
            }

            for (var p = 0; p < sockets.Inputs.Count; p++)
            {
                if (!Near(NodeGeometry.GroupInputPort(bounds, sockets, p), graph, tolerance)) continue;

                var socket = sockets.Inputs[p];
                (nodeId, portIndex, isOutput) = (socket.Node, socket.Port, false);
                return true;
            }
        }

        for (var i = patch.Nodes.Count - 1; i >= 0; i--)
        {
            var node = patch.Nodes[i];
            var def = NodeCatalog.Get(node.TypeId);
            if (def is null || Shut(node.Id)) continue;

            for (var p = 0; p < def.Outputs.Count; p++)
            {
                if (!Near(NodeGeometry.OutputPort(node, p), graph, tolerance)) continue;

                (nodeId, portIndex, isOutput) = (node.Id, p, true);
                return true;
            }

            for (var p = 0; p < def.Inputs.Count; p++)
            {
                if (!Near(NodeGeometry.InputPort(node, def, p), graph, tolerance)) continue;

                (nodeId, portIndex, isOutput) = (node.Id, p, false);
                return true;
            }
        }

        (nodeId, portIndex, isOutput) = (Guid.Empty, -1, false);
        return false;
    }

    private static bool Near(Point a, Point b, double tolerance)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy <= tolerance * tolerance;
    }

    /// <summary>
    /// Selects every module on the canvas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every module that is <em>drawn</em>, which is not quite the same thing: a
    /// module whose plugin is missing has no size, so the canvas neither paints
    /// it nor lets a click reach it. Putting one into a selection would be the
    /// one way to drag or delete something invisible, and "all" ought to mean
    /// what can be seen.
    /// </para>
    /// <para>
    /// The Output is included, because it is on the canvas and this is not a
    /// gesture that does anything to it. What follows already knows: copy leaves
    /// it out (ADR-0045) and delete refuses it, so selecting everything and
    /// pressing either does the sensible thing without this having to guess
    /// which was coming.
    /// </para>
    /// </remarks>
    public void SelectAll()
    {
        var all = patch.Nodes
            .Where(node => NodeCatalog.Get(node.TypeId) is not null)
            .Select(node => node.Id)
            .ToArray();

        if (all.Length == selection.Count && all.All(selection.Contains)) return;

        selection.Clear();
        foreach (var id in all) selection.Add(id);

        // The last in the patch's own order, which is the one drawn on top —
        // the same module Toggle falls back to, for the same reason.
        focus = all.Length == 0 ? null : all[^1];

        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>
    /// Makes the selection exactly this one module, or nothing at all. What an
    /// ordinary click does, and what every caller outside the pointer handling
    /// wants — adding a module selects it rather than joining it to whatever was
    /// selected before.
    /// </summary>
    private void Select(Guid? id)
    {
        if (focus == id && selection.Count == (id is null ? 0 : 1)) return;

        selection.Clear();
        if (id is { } one) selection.Add(one);

        focus = id;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Adds a module to the selection, or takes it out again if it was already
    /// in — what Ctrl (or Command) held down turns a click into.
    /// </summary>
    /// <remarks>
    /// Taking the focused one out moves the focus rather than dropping it, so
    /// the inspector keeps showing something for as long as anything is
    /// selected. Whatever is last in the patch's own order is picked, which is
    /// the module drawn on top.
    /// </remarks>
    private void Toggle(Guid id)
    {
        if (!selection.Add(id))
        {
            selection.Remove(id);

            if (focus == id)
                focus = selection.Count == 0 ? null : SelectedNodes[^1].Id;
        }
        else
        {
            focus = id;
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Nodes paint in list order, so the last one drawn is the one on top.</summary>
    private void BringToFront(NodeInstance node)
    {
        patch.Nodes.Remove(node);
        patch.Nodes.Add(node);
    }
}
