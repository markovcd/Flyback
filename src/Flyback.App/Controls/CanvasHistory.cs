using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>How the patch on the canvas came to be a different object.</summary>
internal enum Replacement
{
    /// <summary>A document arrived: nothing is behind it, and the view is framed on it.</summary>
    Opened,

    /// <summary>A step came back out of the history, which leaves the view where it was.</summary>
    Restored,

    /// <summary>The assistant rebuilt the patch, which is an edit and is framed like an arrival.</summary>
    Applied,
}

/// <summary>
/// The patch the canvas shows and the steps behind it: which gestures earn a step,
/// what an undo puts back, and what is noted beside each step.
/// </summary>
/// <remarks>
/// The history itself is the engine's and holds JSON snapshots. Every patch that
/// arrives passes one gate here, so a step can never be undone into a module standing
/// half off the canvas and every patch shown has an Output.
/// </remarks>
internal sealed class CanvasHistory
{
    private readonly PatchHistory history = new();

    /// <summary>The patch on the canvas. Replaced whole by an open, an undo, a redo or the assistant.</summary>
    public Patch Patch { get; private set; } = new();

    /// <summary>
    /// Whether the patch belongs to somebody else, and the canvas is a view of it
    /// (ADR-0068): selecting, panning and copying stay, and every gesture that
    /// changes it goes.
    /// </summary>
    public bool Locked { get; set; }

    /// <summary>Whether the <see cref="PatchChanged"/> being raised is a patch opened rather than edited.</summary>
    public bool Opening { get; private set; }

    /// <summary>
    /// What the owner of this canvas keeps beside the patch, noted with every step so
    /// an undo hands back the state that step was taken in. Opaque, so the canvas need
    /// not know what outside it an edit can change. Set it before making the edit.
    /// </summary>
    public object? Mark { get; set; }

    public bool CanUndo => history.CanUndo;

    public bool CanRedo => history.CanRedo;

    /// <summary>Whether the patch differs from the one opened or last written out.</summary>
    public bool IsModified => history.IsModified;

    /// <summary>The patch is a different object, before anybody is told it changed.</summary>
    public event EventHandler<Replacement>? Replaced;

    /// <summary>The graph itself changed and needs recompiling.</summary>
    public event EventHandler? PatchChanged;

    /// <summary>
    /// What can be undone or redone changed. Apart from <see cref="PatchChanged"/>
    /// because moving a module is a step and nothing the program can hear.
    /// </summary>
    public event EventHandler? HistoryChanged;

    /// <summary>A step was added. Not raised for an undo or a redo, nor for an edit that made no step.</summary>
    public event EventHandler? Recorded;

    /// <summary>Shows a document arriving from outside, with nothing behind it to undo into.</summary>
    public void Open(Patch patch)
    {
        Take(patch);
        history.Opened(Patch, Mark);

        Replaced?.Invoke(this, Replacement.Opened);

        Opening = true;
        try
        {
            PatchChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            Opening = false;
        }

        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Records the patch as it now stands, after an edit made to it in place.
    /// </summary>
    /// <param name="coalesce">
    /// Names the gesture, where the edit is one frame of a control held down (see
    /// <see cref="PatchHistory.Record"/>): a slider dragged across its range is one
    /// thing somebody did and wants back in one press.
    /// </param>
    public void Record(string? coalesce = null)
    {
        // Before the step, so what can be undone into has every module on the canvas.
        new CanvasScene(Patch).HoldInside();

        var stepped = history.Record(Patch, coalesce, Mark);

        PatchChanged?.Invoke(this, EventArgs.Empty);
        HistoryChanged?.Invoke(this, EventArgs.Empty);

        if (stepped) Recorded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Records modules having moved, which is a step and nothing the program can hear,
    /// and says whether a step was made.
    /// </summary>
    public bool RecordMove()
    {
        if (!history.Record(Patch, mark: Mark)) return false;

        HistoryChanged?.Invoke(this, EventArgs.Empty);
        Recorded?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <summary>The patch sounds different for a moment, and no step is made for it.</summary>
    public void Sounded() => PatchChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Puts the patch back as it was before the last edit, and says whether there was one.</summary>
    public bool Undo() => Restore(history.Undo());

    /// <summary>Puts back the edit the last undo took away.</summary>
    public bool Redo() => Restore(history.Redo());

    /// <summary>
    /// Shows a patch built from the one that was open rather than opened in its place:
    /// the assistant's work, which is a large edit and not a new document.
    /// </summary>
    public void Apply(Patch edited)
    {
        Take(edited);

        var stepped = history.Record(Patch, mark: Mark);

        Replaced?.Invoke(this, Replacement.Applied);
        Announce();

        if (stepped) Recorded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The patch as it stands has been written to a file, so there is nothing in it left to lose.</summary>
    public void MarkSaved()
    {
        history.Saved(Patch);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The patch as it stands is a document that has just arrived, for a caller that
    /// had to build it through an edit before it could know what it was.
    /// </summary>
    public void MarkOpened()
    {
        history.Opened(Patch, Mark);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The patch arrived from somewhere that is not a file, so all of it is unsaved.</summary>
    public void MarkUnsaved()
    {
        history.Unsaved();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The hand has come off whatever it was holding, so the next edit starts a step
    /// of its own: for gestures made outside the canvas, which the history cannot see end.
    /// </summary>
    public void GestureEnded() => history.GestureEnded();

    /// <summary>Says the same thing beside every step, for a <see cref="Mark"/> that changed with no edit made.</summary>
    public void Remark(object? mark)
    {
        Mark = mark;
        history.Remark(mark);
    }

    /// <summary>Restates what is beside each step, for a fact that has moved under some of them.</summary>
    public void Remark(Func<object?, object?> restated)
    {
        history.Remark(restated);
        Mark = history.Mark;
    }

    /// <summary>
    /// Says what stands beside the patch as it now is: what the next step hands back
    /// when undone, as well as what that step is noted with.
    /// </summary>
    public void Note(object? mark)
    {
        Mark = mark;
        history.Note(mark);
    }

    /// <summary>
    /// Shows a patch that came out of the history. The view stays where it was,
    /// because what is being looked at is the edit that came back.
    /// </summary>
    private bool Restore(Patch? restored)
    {
        if (restored is null) return false;

        Take(restored);

        // What the owner had beside the patch when this step was taken is what it
        // has again, before anybody hears of the step.
        Mark = history.Mark;

        Replaced?.Invoke(this, Replacement.Restored);
        Announce();

        return true;
    }

    /// <summary>The gate every arriving patch passes: an Output, and every module on the canvas.</summary>
    private void Take(Patch next)
    {
        Patch = next;
        Patch.EnsureOutput();
        new CanvasScene(Patch).HoldInside();
    }

    private void Announce()
    {
        PatchChanged?.Invoke(this, EventArgs.Empty);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }
}
