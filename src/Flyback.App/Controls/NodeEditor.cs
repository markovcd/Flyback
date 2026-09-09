using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
public sealed partial class NodeEditor : Control
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
            history.Opened(patch, Mark);

            selection.Clear();
            focus = null;
            EndGesture();
            FrameAll();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            PatchChanged?.Invoke(this, EventArgs.Empty);
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Whether the patch belongs to somebody else, and this is a view of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A canvas showing a patch built from source (ADR-0068). Everything that
    /// looks stays: selecting, panning, zooming, framing, copying — a locked
    /// canvas is still how somebody reads a patch and picks the module the
    /// inspector should be about. What goes is everything that changes it, since
    /// the next evaluation would overwrite it and the person would have watched
    /// their work disappear.
    /// </para>
    /// <para>
    /// Gated at the gestures rather than by refusing the methods behind them.
    /// The methods are the shell's to call — a menu, the toolbar, the assistant
    /// — and a public method that silently did nothing would be a worse thing to
    /// hand a caller than a button that is visibly off.
    /// </para>
    /// </remarks>
    public bool Locked { get; set; }

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
    /// Whether a gesture is under way on the canvas: a module being moved, a
    /// wire being drawn, the view being panned or a marquee drawn out.
    /// </summary>
    /// <remarks>
    /// For the shell, which takes the keyboard while the pointer is held and has
    /// to know that the hand is in the middle of something. The canvas itself
    /// asks its own state rather than this.
    /// </remarks>
    public bool Gesturing => drag != Drag.None;

    /// <summary>
    /// What the owner of this canvas keeps beside the patch, noted with every
    /// step so that an undo hands back the state that step was taken in.
    /// </summary>
    /// <remarks>
    /// Opaque here on purpose: the canvas records a step for every gesture it
    /// has, and would otherwise have to know about each thing outside it that
    /// an edit can change as well. Set it before making the edit that changes
    /// it, so the step the edit records is the one belonging to the new state —
    /// and see <see cref="Remark"/> for the changes no edit is made for.
    /// </remarks>
    public object? Mark { get; set; }

    /// <summary>
    /// A step has just been added to the history.
    /// </summary>
    /// <remarks>
    /// Not raised for an undo or a redo, and not for an edit that made no step
    /// — one that changed nothing, or one frame of a gesture folded into the
    /// step before it. So a caller keeping a history of its own beside this one
    /// hears once per thing somebody did, which is what it has to match.
    /// </remarks>
    public event EventHandler? Recorded;

    /// <summary>
    /// Whether the patch differs from the one that was opened, or from the last
    /// one written out. Undoing back to where it started clears it again, since
    /// what is being compared is the document rather than whether anybody typed.
    /// </summary>
    public bool IsModified => history.IsModified;
}
