using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// Copy, cut and paste, and where a pasted selection is put down.
/// </summary>
/// <remarks>
/// What travels is a patch file rather than a private format (ADR-0045), so a
/// selection copied here can be pasted into a text editor and back. This is also
/// the one place the canvas reaches outside itself and can fail in a way
/// somebody has to be told about, which is what <see cref="Reported"/> is for: a
/// control that draws has nowhere to put a sentence.
/// </remarks>
public sealed partial class NodeEditor
{
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
}
