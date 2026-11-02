using Flyback.Editor.Canvas;
using Flyback.Editor.Notices;

namespace Flyback.Editor.Hubs;

/// <summary>
/// Undo and redo across the text's stack and the canvas's, and which of the two a
/// press lands on (ADR-0071).
/// </summary>
/// <remarks>
/// Where the text is the document, its stack is the document's history from
/// either view: typing, applying and turning a knob are one run of things
/// somebody did, so the last two go on that stack rather than on a second one
/// to be interleaved later by guessing. Where the graph is the document the two
/// are independent and undo follows the view. Either way the gesture falls
/// through when its stack is empty, or applying a printing — loaded rather than
/// typed — would be the one thing nobody could take back.
/// </remarks>
internal sealed class JointUndo
{
    private readonly Document document;
    private readonly CanvasHistory history;
    private readonly SourceView source;
    private readonly Reactions reactions;

    public JointUndo(Document document, CanvasHistory history, SourceView source, Reactions reactions)
    {
        this.document = document;
        this.history = history;
        this.source = source;
        this.reactions = reactions;

        // While the text is the document its stack is the history, so a step the
        // canvas records is one that stack has to be able to take back.
        history.Recorded += (_, _) =>
        {
            if (document.Owned) unstacked++;

            // A new step empties the canvas's redo stack.
            sinceHandover = null;
        };

        source.Changed += (_, _) =>
        {
            // Typing empties the text's redo stack. A step being walked is not
            // typing, and sets this for itself.
            if (!stepping) sinceHandover = null;
        };
    }

    /// <summary>
    /// Which stack a press of undo or redo lands on, and nothing where it lands on
    /// neither.
    /// </summary>
    /// <remarks>
    /// Here once because two things ask it and they have to agree: the gesture,
    /// which acts on the answer, and the toolbar, which grays the button when there
    /// is none.
    /// </remarks>
    private enum Landing
    {
        Text,
        Canvas,
    }

    /// <summary>
    /// Steps the canvas has recorded that the text's stack has not been told about
    /// yet.
    /// </summary>
    /// <remarks>
    /// Counted rather than put on that stack as they happen, because a knob is
    /// turned before its number is written into the text and the two are one thing
    /// somebody did. One press then takes back the number and the sound together.
    /// </remarks>
    private int unstacked;

    /// <inheritdoc cref="stepping"/>
    public bool Stepping => stepping;

    /// <summary>
    /// Whether a step of the text's stack is being walked right now.
    /// </summary>
    /// <remarks>
    /// Nothing may be put on that stack while it is being read off: walking a step
    /// rebuilds the panel, and a control losing the focus to that looks like a
    /// write-back trying to record itself inside the undo that caused it.
    /// </remarks>
    private bool stepping;

    /// <summary>
    /// How many canvas steps have been taken back since an undo handed the patch to
    /// the canvas, or null where none has.
    /// </summary>
    /// <remarks>
    /// Undoing an evaluation moves the owner and the view to the canvas and leaves
    /// the deed that puts it back on top of the text's redo stack. At nought that
    /// deed is the next thing to redo, though neither the owner nor the view says
    /// so; anything that empties either redo stack ends it.
    /// </remarks>
    private int? sinceHandover;

    /// <summary>Whether an undo would take anything back, from either stack.</summary>
    public bool CanUndo => UndoLandsOn is not null;

    /// <inheritdoc cref="CanUndo"/>
    public bool CanRedo => RedoLandsOn is not null;

    /// <remarks>
    /// The text's stack first wherever the text is in play — it owns the patch, or
    /// it is what is showing — and the canvas's alone otherwise. A canvas that owns
    /// the patch and is showing must not reach into the text: what is on that stack
    /// then is typing into a printing nobody is looking at.
    /// </remarks>
    private Landing? UndoLandsOn =>
        document.TextInPlay && source.CanUndo ? Landing.Text
        : history.CanUndo ? Landing.Canvas
        : null;

    /// <remarks>
    /// As <see cref="UndoLandsOn"/>, and the text's stack besides for the one press
    /// that puts an evaluation back — see <see cref="sinceHandover"/>.
    /// </remarks>
    private Landing? RedoLandsOn =>
        (document.TextInPlay || sinceHandover == 0) && source.CanRedo ? Landing.Text
        : history.CanRedo ? Landing.Canvas
        : null;

    /// <summary>Takes back the last thing done, and whether there was anything to take back.</summary>
    public bool Undo()
    {
        switch (UndoLandsOn)
        {
            case Landing.Text:
                source.Undo();
                return true;

            case Landing.Canvas:
                history.Undo();
                Owed(-1);
                if (sinceHandover is { } behind) sinceHandover = behind + 1;
                document.Stepped();
                return true;

            default:
                return false;
        }
    }

    /// <summary>Puts back the last thing taken back, and whether there was anything to put back.</summary>
    public bool Redo()
    {
        switch (RedoLandsOn)
        {
            case Landing.Text:
                source.Redo();
                return true;

            case Landing.Canvas:
                history.Redo();
                Owed(1);
                if (sinceHandover is { } behind) sinceHandover = Math.Max(0, behind - 1);
                document.Stepped();
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Counts a canvas step that this gesture took back or put again without the
    /// text's stack being involved.
    /// </summary>
    /// <remarks>
    /// Left counted, the next write-back would put it on that stack as well, and
    /// one press there would take back two edits — the second of them one somebody
    /// had already taken back by hand.
    /// </remarks>
    private void Owed(int steps)
    {
        if (document.Owned) unstacked = Math.Max(0, unstacked + steps);
    }

    /// <summary>
    /// Puts the patch steps taken since the last of these on the text's stack, as
    /// one thing to take back.
    /// </summary>
    /// <remarks>
    /// A count rather than the steps themselves, because the canvas is already
    /// keeping them and keeping them twice is how two records of one edit come to
    /// disagree. One history, reached through whichever view somebody is in.
    /// </remarks>
    public void RememberPatchSteps()
    {
        if (unstacked == 0 || stepping) return;

        var steps = unstacked;
        unstacked = 0;

        source.Remember(() => StepPatch(steps, back: true), () => StepPatch(steps, back: false));
    }

    /// <summary>Walks the canvas's history, and who owns the patch, with it.</summary>
    private void StepPatch(int steps, bool back)
    {
        var owned = document.Owned;

        stepping = true;

        try
        {
            for (var step = 0; step < steps; step++)
                if (back ? history.Undo() : history.Redo())
                    document.Handed();
        }
        finally
        {
            stepping = false;
        }

        sinceHandover = back && owned && !document.Owned ? 0 : null;

        reactions.Raise(new EditStateChanged());
    }

    /// <summary>Drops the steps and the handover counted for a document another has replaced.</summary>
    public void Forget()
    {
        unstacked = 0;
        sinceHandover = null;
    }
}
