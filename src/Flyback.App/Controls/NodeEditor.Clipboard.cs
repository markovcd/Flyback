using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// Copy, cut and paste, and where a pasted selection is put down.
/// </summary>
/// <remarks>
/// What travels is a patch file rather than a private format (ADR-0045). This is
/// also the one place the canvas reaches outside itself and can fail in a way
/// somebody has to be told about, which is what <see cref="Reported"/> is for.
/// </remarks>
public sealed partial class NodeEditor
{
    // --- copy and paste ------------------------------------------------------

    /// <summary>
    /// Puts the selected modules on the system clipboard, as the JSON a patch is
    /// saved as. Nothing happens where the selection holds nothing copiable, rather
    /// than the clipboard being emptied by a gesture that found nothing.
    /// </summary>
    /// <remarks>
    /// The system clipboard rather than a buffer of this program's own, because a
    /// copy that cannot leave the window is not really one: what this writes pastes
    /// into another Flyback and into a text editor. See ADR-0045.
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
    /// Reads a patch off the clipboard and merges it in, centred on the view, with
    /// what arrived left selected so it can be dragged into place.
    /// </summary>
    /// <remarks>
    /// One edit, so one Ctrl+Z takes the whole paste back. The text is read the way
    /// a file is: a fragment naming a module this build has not got is refused with
    /// the sentence <see cref="PatchLoad.Summary"/> already words.
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
    /// Puts a second copy of the selection on the canvas, a step down and right of
    /// the original and selected, so one drag carries it wherever it is wanted.
    /// </summary>
    /// <remarks>
    /// The clipboard is not touched. A fragment and somewhere to put it are both
    /// already here, so nothing has to go out to the system and back through JSON
    /// — and what was copied earlier survives duplicating something else. Where it
    /// lands is the other difference from a paste: a paste goes to the middle of
    /// the view, and a duplicate is about the module being looked at.
    /// </remarks>
    public void DuplicateSelection()
    {
        if (selection.Count == 0) return;

        var fragment = PatchClipboard.Copy(patch, selection);

        // Selected, and yet nothing of it can be duplicated — which can only be
        // the Output on its own. Said, because a gesture that silently does
        // nothing reads as a broken one.
        if (fragment.Nodes.Count == 0)
        {
            Reported?.Invoke(this, "The Output cannot be duplicated.");
            return;
        }

        // Far enough to read as a second copy rather than a redraw of the first,
        // and near enough to still be under the hand.
        const double step = 28;

        AddFragment(fragment, Drawn(fragment, fragment.Nodes).Center + new Vector(step, step));
    }

    /// <summary>
    /// Merges a fragment into the patch and leaves what arrived selected, so it can
    /// be dragged straight into place.
    /// </summary>
    /// <remarks>
    /// The graph half is <see cref="PatchClipboard.Paste"/> and knows nothing about
    /// a canvas; here are the two things that need one — where it lands and what is
    /// selected afterwards. A paste has no point of its own and goes to the middle
    /// of the view, stepped clear of what is there; something picked out of the
    /// module list was picked somewhere, and lands there.
    /// </remarks>
    /// <param name="fragment">What to add. Not modified, so the same one may be added again.</param>
    /// <param name="at">Where its middle should land, or null for the middle of the view.</param>
    public IReadOnlyList<NodeInstance> AddFragment(Patch fragment, Point? at = null)
    {
        var arriving = fragment.Nodes.Where(n => !NodeCatalog.IsSink(n.TypeId)).ToArray();
        if (arriving.Length == 0) return [];

        var box = Drawn(fragment, arriving);

        var (dx, dy) = at is { } point
            ? (point.X - box.Center.X, point.Y - box.Center.Y)
            : WhereToPaste(box);

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
    /// How far to shift what is arriving so it lands in the middle of what is on
    /// screen, clear of anything already there.
    /// </summary>
    /// <remarks>
    /// The middle of the view rather than where the modules were copied from, which
    /// may be a screen away. Then stepped down and right until it is not sitting on
    /// anything, since landing on top of what is there reads as nothing having
    /// happened. The step is capped: a dense patch has no clear middle, and what
    /// arrives is selected, so dragging it somewhere better is one gesture.
    /// </remarks>
    private (double X, double Y) WhereToPaste(Rect group)
    {
        const double step = 28;
        const int tries = 40;

        var taken = OnCanvas().ToArray();

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

    /// <summary>
    /// The one rectangle that holds what a fragment will look like on the canvas.
    /// </summary>
    /// <remarks>
    /// Which is not where its modules are, for a fragment with a shut box in it: a
    /// box is drawn at its members' least corner and one module wide, however far
    /// apart they were left. Placed by the spread instead, a group saved from a
    /// chain laid out by hand lands half its hidden width away from the click.
    /// </remarks>
    private static Rect Drawn(Patch fragment, IReadOnlyList<NodeInstance> arriving)
    {
        var shut = fragment.Groups?.Where(group => group.Collapsed).ToArray() ?? [];
        var hidden = shut.SelectMany(group => group.Members).ToHashSet();

        var seen = BoxAround([.. arriving.Where(node => !hidden.Contains(node.Id))]);

        foreach (var group in shut)
        {
            var box = NodeGeometry.GroupBounds(fragment, group, fragment.SocketsOf(group));

            seen = seen == default ? box : seen.Union(box);
        }

        return seen;
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
    /// another, and frames the result. One edit, so one Ctrl+Z puts every node back.
    /// </summary>
    /// <param name="onlySelected">
    /// Lay out the selection alone, leaving the rest of the patch exactly where it
    /// is and the view where it was. See ADR-0110.
    /// </param>
    /// <remarks>
    /// Nothing the compiler reads changes, so the patch compiles to exactly the same
    /// program before and after (ADR-0044) — a box shut to make the drawing fit is a
    /// fact about the canvas and not about the patch (ADR-0092), and comes back in the
    /// same one press. A patch too big to draw even with every box shut is left
    /// exactly as it was, and said instead of shown.
    /// </remarks>
    public void Tidy(bool onlySelected = false)
    {
        if (patch.Nodes.Count == 0) return;

        if (onlySelected && selection.Count == 0)
        {
            Reported?.Invoke(this, "Nothing is selected. Ctrl+L lays the whole patch out.");
            return;
        }

        var laid = PatchLayout.Arrange(
            patch,
            NodeCatalog.Current,
            NodeGeometry.Metrics,
            onlySelected ? selection : null);

        if (!laid.Fitted)
        {
            Reported?.Invoke(
                this,
                $"This {(onlySelected ? "selection" : "patch")} is too big to draw on the canvas "
                + "even with every box shut, so nothing has been moved.");

            return;
        }

        // A box the layout shut may have had part of itself selected.
        if (SelectWholeBoxes()) SelectionChanged?.Invoke(this, EventArgs.Empty);

        NotifyPatchChanged();

        // The selection went back where it was, so there is nothing to bring into
        // view — and moving the view would lose the part of the patch being worked
        // on, which is the whole of why only part of it was laid out.
        if (!onlySelected) FrameAll();

        if (laid.Shut.Count > 0)
            Reported?.Invoke(
                this,
                $"With every box open this {(onlySelected ? "selection" : "patch")} is wider than "
                + $"the canvas, so {Named(laid.Shut)} {(laid.Shut.Count == 1 ? "was" : "were")} shut.");
    }

    /// <summary>
    /// What to call the boxes the layout shut, for the line that reports them. Three
    /// by name at most: which part of the patch went away is the point, and the line
    /// it is written on is one line.
    /// </summary>
    private static string Named(IReadOnlyList<NodeGroup> groups)
    {
        var names = groups.Take(3).Select(group => group.Title()).ToArray();
        var rest = groups.Count - names.Length;

        if (rest > 0) return $"{string.Join(", ", names)} and {rest} more";

        return names.Length == 1 ? names[0] : $"{string.Join(", ", names[..^1])} and {names[^1]}";
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

        foreach (var bounds in OnCanvas())
        {
            left = Math.Min(left, bounds.Left);
            top = Math.Min(top, bounds.Top);
            right = Math.Max(right, bounds.Right);
            bottom = Math.Max(bottom, bounds.Bottom);
        }

        if (left > right) return;

        const double margin = 50;
        var scaleX = Bounds.Width / (right - left + margin * 2);
        var scaleY = Bounds.Height / (bottom - top + margin * 2);

        zoom = Math.Clamp(Math.Min(scaleX, scaleY), MinZoom, 1.4);

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
    /// Every pan goes through here, the one a zoom performs included: zooming out in
    /// a corner walks the view outwards as surely as dragging does. What it holds is
    /// the view rather than its centre, and the reach is a little wider than
    /// <see cref="NodeInstance.Across"/> and <see cref="NodeInstance.Down"/>,
    /// because those hold a corner and the body hangs below and right of it.
    /// <para>
    /// A view wider than the canvas is centred on it instead, which is a real case:
    /// past about two thousand pixels the whole canvas fits at the zoom's floor, and
    /// there is then nowhere to pan to.
    /// </para>
    /// </remarks>
    private void PanTo(Point to)
    {
        pan = new Point(
            Held(to.X, Bounds.Width, ViewReachAcross),
            Held(to.Y, Bounds.Height, ViewReachDown));

        double Held(double offset, double viewport, double reach)
        {
            var edge = reach * zoom;

            return viewport > edge * 2
                ? viewport / 2
                : Math.Clamp(offset, viewport - edge, edge);
        }
    }
}
