namespace Flyback.Core.Graph;

/// <summary>
/// What the patch looked like before each edit, so that an edit can be taken
/// back and put again.
/// </summary>
/// <remarks>
/// Snapshots rather than commands. A step here is the whole document as JSON
/// and undoing is loading one, where the alternative is an inverse for every
/// edit the program can make — each one a chance for the two to disagree, and
/// each new edit a chance to forget writing it. A patch is small enough that
/// the trade is not close: the largest preset in the box is twenty-six modules
/// and a few kilobytes, and the serialiser is the one files already use, so
/// anything that survives being saved survives being undone and nothing needs
/// a second opinion about what an edit was.
/// <para>
/// Taking a snapshot stamps the patch's requirements exactly as saving one
/// does, because it is the same call: a snapshot and a file are the same text.
/// </para>
/// <para>
/// Nothing here knows what any edit did. A step is a comparison against the
/// last one, so an edit that changed nothing is not a step at all and a caller
/// that records too eagerly pays only for the compare — which is what lets the
/// canvas record from one place rather than at each of the things it can do.
/// </para>
/// </remarks>
/// <param name="modules">
/// Which catalogue a restored patch's type ids mean, defaulting to the
/// installed one. Named explicitly, a history can be exercised against a
/// catalogue that is not the running program's.
/// </param>
public sealed class PatchHistory(ModuleCatalog? modules = null)
{
    /// <summary>
    /// How many edits back one may go. Deep enough not to be met while working,
    /// and shallow enough that the cost stays in the low megabytes rather than
    /// growing with the length of a session.
    /// </summary>
    public const int Depth = 200;

    private readonly List<string> past = [];
    private readonly List<string> future = [];

    private string current = string.Empty;
    private string saved = string.Empty;
    private string? gesture;

    /// <summary>
    /// Whether the patch differs from the one last opened or written out.
    /// </summary>
    /// <remarks>
    /// A comparison of the two snapshots rather than a flag set by editing,
    /// which is what makes undoing back to where you started stop counting as a
    /// change: it is the same document again, and the whole document is what a
    /// step here already is.
    /// </remarks>
    public bool IsModified => current != saved;

    public bool CanUndo => past.Count > 0;

    public bool CanRedo => future.Count > 0;

    /// <summary>
    /// Begin from this document, with nothing behind it.
    /// </summary>
    /// <remarks>
    /// A patch that was opened, built from a preset or handed over by the
    /// assistant is a new document rather than an edit to the last one. Undoing
    /// back into whatever somebody had open before they opened a file would not
    /// be undo; it would be losing the file they just opened.
    /// </remarks>
    public void Opened(Patch patch)
    {
        past.Clear();
        future.Clear();
        gesture = null;
        current = Snapshot(patch);
        saved = current;
    }

    /// <summary>
    /// The patch as it now stands has been written out, so this is what there is
    /// nothing to lose from. The steps behind it are left alone: saving is not
    /// an edit, and it is no reason to stop being able to take one back.
    /// </summary>
    public void Saved(Patch patch)
    {
        current = Snapshot(patch);
        saved = current;
    }

    /// <summary>
    /// Take note of an edit that has already happened. The patch is read as it
    /// now stands, and what is kept is how it stood before.
    /// </summary>
    /// <param name="patch">The document as it now stands, read rather than kept: what is stored is a snapshot of the text, so the caller may go on editing this.</param>
    /// <param name="coalesce">
    /// Names the gesture an edit came from, when it is one a hand holds down: a
    /// slider being dragged makes an edit a frame, and a hundred of those are
    /// one thing somebody did. Consecutive edits sharing a name are one step, so
    /// undoing a drag returns to before it started rather than to halfway
    /// through it. Null for anything discrete, which is most of it.
    /// </param>
    public void Record(Patch patch, string? coalesce = null)
    {
        var now = Snapshot(patch);

        // Recorded before anything was opened, which is a caller that built a
        // patch by hand. Take it as the document rather than as an edit to a
        // document that does not exist.
        if (current.Length == 0)
        {
            current = now;
            return;
        }

        if (now == current) return;

        // Still inside the gesture that made the last step. That step's
        // starting point is the one worth keeping, so this edit moves where it
        // ends rather than adding one of its own.
        if (coalesce is null || coalesce != gesture)
        {
            past.Add(current);
            if (past.Count > Depth) past.RemoveAt(0);
        }

        current = now;
        gesture = coalesce;
        future.Clear();
    }

    /// <summary>The patch as it stood before the last edit, or null where there is none.</summary>
    public Patch? Undo() => Step(past, future);

    /// <summary>The patch as it stood before the last undo, or null where there is none.</summary>
    public Patch? Redo() => Step(future, past);

    private Patch? Step(List<string> from, List<string> to)
    {
        if (from.Count == 0) return null;

        to.Add(current);
        current = from[^1];
        from.RemoveAt(from.Count - 1);

        // Whatever gesture was in progress is over. The next edit starts a step
        // of its own rather than folding into one that has been stepped past —
        // which would otherwise let a second drag of the same slider quietly
        // rewrite the step an undo had just arrived at.
        gesture = null;

        return PatchIO.Read(current, modules).Patch;
    }

    private string Snapshot(Patch patch) => PatchIO.ToJson(patch, modules);
}
