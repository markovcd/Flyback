namespace Flyback.Core.Graph;

/// <summary>
/// What the patch looked like before each edit, so an edit can be taken back and
/// put again.
/// </summary>
/// <remarks>
/// Snapshots rather than commands: a step is the whole document as JSON and
/// undoing is loading one, where the alternative is an inverse per edit for the
/// two to disagree about. A patch is small enough that the trade is not close,
/// and the serialiser is the one files already use.
/// <para>
/// Nothing here knows what any edit did. A step is a comparison against the last
/// one, so an edit that changed nothing is not a step and a caller that records
/// too eagerly pays only for the compare.
/// </para>
/// </remarks>
/// <param name="modules">
/// Which catalogue a restored patch's type ids mean, defaulting to the installed
/// one.
/// </param>
public sealed class PatchHistory(ModuleCatalog? modules = null)
{
    /// <summary>
    /// How many edits back one may go. Deep enough not to be met while working,
    /// and shallow enough that the cost stays in the low megabytes rather than
    /// growing with the length of a session.
    /// </summary>
    public const int Depth = 200;

    private readonly List<(string Snapshot, object? Mark)> past = [];
    private readonly List<(string Snapshot, object? Mark)> future = [];

    private string current = string.Empty;
    private string saved = string.Empty;
    private string? gesture;
    private object? mark;

    /// <summary>
    /// Whether the patch differs from the one last opened or written out. A
    /// comparison of two snapshots rather than a flag set by editing, which is
    /// what makes undoing back to where you started stop counting as a change.
    /// </summary>
    public bool IsModified => current != saved;

    public bool CanUndo => past.Count > 0;

    public bool CanRedo => future.Count > 0;

    /// <summary>
    /// What was noted beside the state the history now stands at — see the
    /// <c>mark</c> argument to <see cref="Record"/>.
    /// </summary>
    public object? Mark => mark;

    /// <summary>
    /// Begin from this document, with nothing behind it. A patch that was opened,
    /// built from a preset or handed over by the assistant is a new document:
    /// undoing back into whatever was open before would be losing the file
    /// somebody just opened.
    /// </summary>
    public void Opened(Patch patch, object? mark = null)
    {
        past.Clear();
        future.Clear();
        gesture = null;
        this.mark = mark;
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
    /// Take note of an edit that has already happened. The patch is read as it now
    /// stands, and what is kept is how it stood before.
    /// </summary>
    /// <param name="patch">The document as it now stands, read rather than kept, so the caller may go on editing this.</param>
    /// <param name="coalesce">
    /// Names the gesture an edit came from, when it is one a hand holds down: a
    /// slider being dragged makes an edit a frame, and consecutive edits sharing a
    /// name are one step. Null for anything discrete.
    /// <para>
    /// A name cannot say when a gesture is over — <see cref="GestureEnded"/> does
    /// that — so a caller naming its gestures after the control they came from has
    /// to say it, or every drag of that control is one step.
    /// </para>
    /// </param>
    /// <param name="mark">
    /// Anything the caller keeps beside the patch that this edit also changed, so
    /// stepping back through the edit steps back through that too. Held opaquely:
    /// an undo hands back whatever was passed with the step it arrives at.
    /// </param>
    /// <returns>
    /// Whether this made a step — false for an edit that changed nothing, for a
    /// frame of a gesture folded into the step before it, and for the first patch
    /// a caller records without opening one.
    /// </returns>
    public bool Record(Patch patch, string? coalesce = null, object? mark = null)
    {
        var now = Snapshot(patch);

        // Recorded before anything was opened, which is a caller that built a
        // patch by hand. Take it as the document rather than as an edit to a
        // document that does not exist.
        if (current.Length == 0)
        {
            current = now;
            this.mark = mark;
            return false;
        }

        // An edit that changed nothing is not a step, and the mark still stands
        // for where the history is: a caller whose own state moved without the
        // patch moving has said so, and there is nothing to step back through.
        if (now == current)
        {
            this.mark = mark;
            return false;
        }

        // Still inside the gesture that made the last step. That step's
        // starting point is the one worth keeping, so this edit moves where it
        // ends rather than adding one of its own.
        var stepped = coalesce is null || coalesce != gesture;

        if (stepped)
        {
            past.Add((current, this.mark));
            if (past.Count > Depth) past.RemoveAt(0);
        }

        current = now;
        this.mark = mark;
        gesture = coalesce;
        future.Clear();

        return stepped;
    }

    /// <summary>
    /// Says the same thing beside every step there is, for something the caller
    /// keeps beside the patch that has changed with no edit to change it.
    /// </summary>
    /// <remarks>
    /// Without this a mark would outlive what it was true of: the patch can change
    /// hands with nothing to record, and undoing an edit must not also take back a
    /// handover that no edit performed.
    /// </remarks>
    public void Remark(object? mark)
    {
        for (var i = 0; i < past.Count; i++) past[i] = (past[i].Snapshot, mark);
        for (var i = 0; i < future.Count; i++) future[i] = (future[i].Snapshot, mark);

        this.mark = mark;
    }

    /// <summary>
    /// The gesture named in the last <see cref="Record"/> is over, so the next
    /// edit starts a step of its own however it is named.
    /// </summary>
    /// <remarks>
    /// Said by whoever can see the hand come off the control, because nothing here
    /// can: a caller that files its edits under the slider they came from files
    /// every drag of it under one name, and without this the second folds into the
    /// first however long ago it was.
    /// </remarks>
    public void GestureEnded() => gesture = null;

    /// <summary>The patch as it stood before the last edit, or null where there is none.</summary>
    public Patch? Undo() => Step(past, future);

    /// <summary>The patch as it stood before the last undo, or null where there is none.</summary>
    public Patch? Redo() => Step(future, past);

    private Patch? Step(
        List<(string Snapshot, object? Mark)> from,
        List<(string Snapshot, object? Mark)> to)
    {
        if (from.Count == 0) return null;

        to.Add((current, mark));
        (current, mark) = from[^1];
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
