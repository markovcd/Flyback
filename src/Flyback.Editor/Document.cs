using Flyback.Editor.Canvas;
using Flyback.Editor.Controls;
using Flyback.Editor.Notices;
using Flyback.Editor.Statistics;
using Flyback.Core.Graph;
using Flyback.Engine.Language;

namespace Flyback.Editor;

/// <summary>
/// The patch as text, beside the patch as a graph, and which of the two is the
/// document (ADR-0148).
/// </summary>
/// <remarks>
/// What owns the patch is the file that was opened rather than the view that
/// happens to be showing (ADR-0068). Open a <c>.fbks</c> and the text is the
/// document, so the canvas shows what it builds and is not editable; open a
/// <c>.fbk</c> and the graph is, so the text view shows a printing.
/// <para>
/// The two are not symmetrical: building text into a patch is exact, printing a
/// patch back out lays the canvas out afresh and shuts every box (ADR-0065). So
/// a printing is offered and labeled, never adopted behind somebody's back.
/// </para>
/// </remarks>
internal sealed class Document
    : IReactTo<UndoAsked>,
        IReactTo<RedoAsked>,
        IReactTo<TidyAsked>,
        IReactTo<CodeAsked>
{
    private readonly NodeEditor editor;
    private readonly SourceView source;
    private readonly ReportLine report;
    private readonly Usage usage;
    private readonly Reactions reactions;
    private readonly LastPress lastPress;
    private readonly CaretFollow caret;

    public Document(NodeEditor editor, SourceView source, ReportLine report, Usage usage, Reactions reactions, LastPress lastPress, CaretFollow caret)
    {
        this.editor = editor;
        this.source = source;
        this.report = report;
        this.usage = usage;
        this.reactions = reactions;
        this.lastPress = lastPress;
        this.caret = caret;

        source.IsVisible = false;

        // The canvas opens on a preset, which is a patch the graph owns. Said
        // before the first one reaches it, so the earliest step in its history
        // knows whose the patch was.
        editor.History.Mark = Owning();

        // While the text is the document its stack is the history, so a step the
        // canvas records is one that stack has to be able to take back.
        editor.History.Recorded += (_, _) =>
        {
            if (sourceOwned) unstacked++;

            // A new step empties the canvas's redo stack.
            sinceHandover = null;
        };

        source.EvaluateRequested += (_, _) =>
        {
            usage.Count(Used.Applied);
            Evaluate();
        };
        source.Changed += (_, _) =>
        {
            // Typing empties the text's redo stack. A step being walked is not
            // typing, and sets this for itself.
            if (!stepping) sinceHandover = null;

            reactions.Raise(new EditStateChanged());
        };
        source.Moved += (_, at) => PointAt(at);
        source.Pasting = Pasted;

        // Asked rather than done: what it settles is which view is the document,
        // and typing not yet applied is asked about first, by whoever owns that question.
        source.HandBackRequested += (_, _) => reactions.Raise(new HandBackAsked());
    }

    public Task On(UndoAsked notice)
    {
        Undo();
        return Task.CompletedTask;
    }

    public Task On(RedoAsked notice)
    {
        Redo();
        return Task.CompletedTask;
    }

    public Task On(TidyAsked notice)
    {
        Tidy(notice.OnlySelected);
        return Task.CompletedTask;
    }

    public Task On(CodeAsked notice)
    {
        ShowCode(notice.Shown);
        return Task.CompletedTask;
    }

    /// <summary>Whether the text is the document.</summary>
    public bool Owned => sourceOwned;

    /// <summary>Whether the text view is the one showing.</summary>
    public bool ShowingCode => showingCode;

    /// <summary>What the text view holds.</summary>
    public string Text => source.Source;

    /// <summary>The patch in the language: the document's own text where the text owns it, else a printing of the canvas.</summary>
    public string AsText() => sourceOwned ? source.Source : PatchPrinter.Print(editor.History.Patch);

    /// <summary>
    /// Writes <paramref name="text"/> into the text view and applies it, as its Apply
    /// button does: one edit, taken back by one undo.
    /// </summary>
    /// <returns>Null once it is applied, or what is wrong with the text, which then leaves the text view and the patch as they were.</returns>
    public string? Apply(string text)
    {
        LanguageLoad load;

        using (source.Together())
        {
            source.Rewrite(text);
            load = Evaluate();
        }

        if (load.Ok) return null;

        // The complaints are about text that is no longer in the view.
        source.Undo();
        source.Clear();

        return load.Report;
    }

    /// <summary>Whether an undo would take anything back, from either stack.</summary>
    public bool CanUndo => UndoLandsOn is not null;

    /// <inheritdoc cref="CanUndo"/>
    public bool CanRedo => RedoLandsOn is not null;

    /// <summary>
    /// Whether the text is the document. False for a patch that came from a
    /// <c>.fbk</c>, a bundle or a preset, where the graph is.
    /// </summary>
    private bool sourceOwned;

    /// <summary>
    /// The last printing this made of a graph-owned patch, so opening the text
    /// view twice does not write over what somebody typed the first time and did
    /// not apply. Kept in step with a knob written back into a printing, which
    /// leaves the text a printing still.
    /// </summary>
    private string? printed;

    /// <summary>
    /// The modules a printing writes, in the order it writes them, which is how
    /// the words in it are pointed back at the patch they came from.
    /// </summary>
    private IReadOnlyList<Guid> printedOrder = [];

    /// <summary>Where the text says each module and each of its knobs.</summary>
    private SourceMap map = SourceMap.Empty;

    /// <summary>
    /// The patch the text builds as it now stands, or null where the text is a
    /// printing and builds nothing. Kept beside the map because the map's names
    /// are this patch's — see <see cref="CaretFollow.IsAdrift"/>.
    /// </summary>
    private Patch? means;

    /// <summary>
    /// The text <see cref="map"/> was made from, or null for no map at all. Null
    /// rather than empty, because an empty document has a perfectly good map and
    /// what this says is that there is no answer yet.
    /// </summary>
    private string? mapped;

    /// <summary>The text as it was last opened or written, for the unsaved question.</summary>
    private string sourceOnDisk = string.Empty;

    /// <summary>Whether the text view is the one showing.</summary>
    private bool showingCode;

    /// <summary>
    /// Who owned the patch, and what the text was to it, at one point in the
    /// canvas's history.
    /// </summary>
    /// <remarks>
    /// Applying text is an edit and a handover at once, so taking the edit back has
    /// to take the handover with it — otherwise undoing an evaluation leaves a
    /// canvas locked behind text claiming to describe it. Kept beside the step,
    /// because a snapshot says what a patch was and nothing about where it came
    /// from.
    /// </remarks>
    private sealed record Ownership(
        bool Owned,
        string OnDisk,
        string? Printed,
        IReadOnlyList<Guid> Order);

    /// <summary>Who owns the patch as things now stand.</summary>
    private Ownership Owning() => new(sourceOwned, sourceOnDisk, printed, printedOrder);

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
    /// Whether there is typing here that has not been made into a patch — which
    /// is the one thing that can be lost without the editor's history knowing
    /// about it, since nothing typed reaches the patch until it is applied.
    /// </summary>
    public bool IsUnapplied => sourceOwned && source.Source != sourceOnDisk;

    /// <summary>
    /// Where the text says each module it describes, worked out again whenever the
    /// text has moved on.
    /// </summary>
    /// <remarks>
    /// Two sources and one shape: text that owns the patch is built, and the
    /// binder notes where everything came from; a printing is not built — that
    /// would make a second copy with different ids — so the printer says where it
    /// put things. A printing is only mapped while it is still the printing, since
    /// once somebody types into one it is text about a patch that may not be there.
    /// </remarks>
    public SourceMap Map
    {
        get
        {
            if (mapped == source.Source) return map;

            var text = source.Source;

            if (sourceOwned)
            {
                // The patch this text describes, which is not always the patch
                // on the canvas — see CaretFollow.
                var load = PatchLanguage.Build(text);

                map = load.Map;
                means = load.Patch;
            }
            else
            {
                map = printed == text
                    ? PatchPrinter.Locate(editor.History.Patch, text, printedOrder)
                    : SourceMap.Empty;

                // A printing is made from the patch on the canvas, so the two
                // cannot disagree and there is nothing to hold beside it.
                means = null;
            }

            mapped = text;

            return map;
        }
    }

    /// <summary>Points the inspector at the module the caret is standing in, while the text is showing.</summary>
    private void PointAt(int at)
    {
        if (!showingCode || caret.Held) return;

        caret.Follow(at, Map, means);
    }

    /// <summary>What a paste writes into the text, where it is a patch file: see <see cref="PastedPatch"/>.</summary>
    private string? Pasted(string pasted)
    {
        var written = PastedPatch.Written(pasted, source.Source, out var refused);

        if (refused is not null) report.Say(refused);
        else if (written is { Length: > 0 }) usage.Count(Used.Pasted);

        return written;
    }

    /// <summary>Whether the text is the printing this last made, untouched.</summary>
    public bool IsPrinting => !sourceOwned && source.Source == printed;

    /// <summary>
    /// The text has just been written to in place, so it is mapped afresh, and a
    /// printing is still one.
    /// </summary>
    /// <remarks>
    /// Said before anything can ask for the map again, since what makes this text
    /// a printing is the two of them agreeing.
    /// </remarks>
    public void Rewritten()
    {
        if (!sourceOwned) printed = source.Source;

        mapped = null;
    }

    /// <summary>
    /// Raised when the text comes to mean something else: a document arriving in
    /// place of another, or an undo handing the patch between the views.
    /// </summary>
    public event EventHandler? Forgot;

    /// <summary>
    /// Whether the three gestures every editor has — take it back, put it back,
    /// tidy it up — are the text's rather than the canvas's.
    /// </summary>
    /// <remarks>
    /// The view that is showing rather than the one that owns the patch: all three
    /// act on what somebody is looking at. Neither stack is disturbed by the
    /// other, so a run of evaluations is still there to be undone on the canvas
    /// after an afternoon of typing.
    /// </remarks>
    private bool Coding => showingCode;

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

    /// <remarks>
    /// The text's stack first wherever the text is in play — it owns the patch, or
    /// it is what is showing — and the canvas's alone otherwise. A canvas that owns
    /// the patch and is showing must not reach into the text: what is on that stack
    /// then is typing into a printing nobody is looking at.
    /// </remarks>
    private Landing? UndoLandsOn =>
        TextInPlay && source.CanUndo ? Landing.Text
        : editor.History.CanUndo ? Landing.Canvas
        : null;

    /// <remarks>
    /// As <see cref="UndoLandsOn"/>, and the text's stack besides for the one press
    /// that puts an evaluation back — see <see cref="sinceHandover"/>.
    /// </remarks>
    private Landing? RedoLandsOn =>
        (TextInPlay || sinceHandover == 0) && source.CanRedo ? Landing.Text
        : editor.History.CanRedo ? Landing.Canvas
        : null;

    /// <summary>Whether the text owns the patch or is the view showing.</summary>
    private bool TextInPlay => sourceOwned || showingCode;

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

    /// <summary>
    /// Takes back the last thing done to whichever view is showing — or, where that
    /// view has nothing left, the last thing done to the patch.
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
    public void Undo()
    {
        if (Gesturing) return;

        var landing = UndoLandsOn;

        if (landing is not null) usage.Count(Used.Undone);

        switch (landing)
        {
            case Landing.Text:
                source.Undo();
                break;

            case Landing.Canvas:
                editor.History.Undo();
                Owed(-1);
                if (sinceHandover is { } behind) sinceHandover = behind + 1;
                Stepped();
                break;
        }

        reactions.Raise(new EditStateChanged());
    }

    public void Redo()
    {
        if (Gesturing) return;

        var landing = RedoLandsOn;

        if (landing is not null) usage.Count(Used.Redone);

        switch (landing)
        {
            case Landing.Text:
                source.Redo();
                break;

            case Landing.Canvas:
                editor.History.Redo();
                Owed(1);
                if (sinceHandover is { } behind) sinceHandover = Math.Max(0, behind - 1);
                Stepped();
                break;
        }

        reactions.Raise(new EditStateChanged());
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
        if (sourceOwned) unstacked = Math.Max(0, unstacked + steps);
    }

    /// <summary>
    /// The patch has just come back out of the canvas's history: two things to
    /// put in step with it, where either is out of step at all.
    /// </summary>
    private void Stepped()
    {
        Handed();
        Reprint();
    }

    /// <summary>
    /// Makes the printing say what the canvas now says, for a patch that has moved
    /// under it.
    /// </summary>
    /// <remarks>
    /// A printing that stopped agreeing with the canvas would be a reading of
    /// nothing; an undo is the one thing that moves a graph-owned patch while the
    /// text is up. Only over a buffer that is still the printing — somebody who
    /// wrote something here keeps it, and what they have is text about a patch that
    /// has moved on.
    /// </remarks>
    public void Reprint()
    {
        if (sourceOwned || source.Source != printed) return;

        var at = source.Caret;

        PrintForReading();

        // As near to where they were reading as the new text has room for.
        source.Caret = Math.Min(at, source.Source.Length);
    }

    /// <summary>
    /// Whether a hand is in the middle of something, so taking an edit back would
    /// be taking it out from under that hand.
    /// </summary>
    /// <remarks>
    /// The pointer is captured for a drag and the keyboard is not, so Ctrl+Z
    /// arrives mid-drag perfectly well — and the canvas drops the drag whenever it
    /// is shown a different patch, which jumps the module out from under the
    /// pointer with no sign of why. Ignored rather than answered: letting go
    /// leaves the press to be made again.
    /// </remarks>
    private bool Gesturing => editor.Gestures.Gesturing;

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
        var owned = sourceOwned;

        stepping = true;

        try
        {
            for (var step = 0; step < steps; step++)
                if (back ? editor.History.Undo() : editor.History.Redo())
                    Handed();
        }
        finally
        {
            stepping = false;
        }

        sinceHandover = back && owned && !sourceOwned ? 0 : null;

        reactions.Raise(new EditStateChanged());
    }

    /// <summary>
    /// Puts back who owned the patch at the step the canvas has just arrived at.
    /// </summary>
    /// <remarks>
    /// The only edit that changes hands is an evaluation, so this does nothing
    /// across the run of drags either side of one. Where it does something it moves
    /// the view with it, because the handover is the half somebody can see. A step
    /// recorded before anybody opened the text view remembers there being no
    /// printing, and taking it back is not a handover.
    /// </remarks>
    private void Handed()
    {
        if (editor.History.Mark is not Ownership was || was.Owned == sourceOwned) return;

        sourceOwned = was.Owned;
        sourceOnDisk = was.OnDisk;
        printed = was.Printed;
        printedOrder = was.Order;

        // The text has not moved but what it is has, and a map is read one way
        // for a document and another for a printing.
        Forget();

        // Which also refreshes what a view can do, so the one that is already
        // showing needs nothing further.
        if (showingCode != sourceOwned) ShowCode(sourceOwned);
        else RefreshOwnership();

        report.Say(sourceOwned
            ? "Put back — the text is the document again."
            : "Taken back — the canvas is the document again, so its modules can be "
              + "dragged, wired and grouped.");
    }

    /// <summary>
    /// Lays out what is showing: the modules across the canvas, or the lines down
    /// the page. The same button and key for both, and the pass behind each is the
    /// other's counterpart (<see cref="Engine.Language.SourceLayout"/> and
    /// <see cref="Core.Graph.PatchLayout"/>).
    /// </summary>
    /// <param name="onlySelected">
    /// Lay out only the selected modules. A canvas gesture: the text has no selected
    /// modules to lay out, so it folds whole either way.
    /// </param>
    public void Tidy(bool onlySelected = false)
    {
        if (Gesturing) return;

        usage.Count(Used.Tidied);

        if (Coding) source.Tidy();
        else editor.Edits.Tidy(onlySelected);
    }

    /// <summary>
    /// Puts the text view over the canvas, or takes it off.
    /// </summary>
    /// <remarks>
    /// Two children of one row rather than a third panel: they are two views of one
    /// patch, and showing both would ask the question this design exists to answer.
    /// Visibility rather than reparenting, as with the fullscreen preview.
    /// </remarks>
    public void ShowCode(bool shown)
    {
        showingCode = shown;

        if (shown) usage.Count(Used.Text);

        if (shown && !sourceOwned) PrintForReading();

        source.IsVisible = shown;
        editor.IsVisible = !shown;

        reactions.Raise(new ViewChanged(showingCode));

        if (shown)
        {
            // Under a finger the text waits to be tapped, rather than throwing up the on-screen keyboard.
            if (!lastPress.ByFinger) source.Focus();

            // Where the caret already is, said once. The panel follows the caret
            // as it moves, and a view that had just opened would otherwise show
            // nothing at all until somebody pressed an arrow.
            PointAt(source.Caret);
        }
        else
        {
            editor.Focus();
        }

        // Undo, redo and tidy all follow the view, so all three have to be asked
        // again about what they can do the moment it changes.
        RefreshOwnership();
    }

    /// <summary>
    /// Writes the patch on the canvas out as text, for somebody to read. Only over
    /// a buffer nobody has touched: somebody who typed here and went to look
    /// something up on the canvas must not come back to a printing over their work.
    /// </summary>
    private void PrintForReading()
    {
        if (source.Source.Length != 0 && source.Source != printed) return;

        var writing = PatchPrinter.Written(editor.History.Patch);

        // Everything about the new text before the text itself, because putting
        // it in the view is a change the view reports at once — and what it
        // reports to is the caret handling, which asks for the map.
        printed = writing.Source;
        printedOrder = writing.Order;
        mapped = writing.Source;
        map = writing.Map;

        // Only where it would say something else. Replacing the document empties
        // the undo stack and moves the caret to the top, which is a great deal
        // to spend on writing the same text a second time.
        if (source.Source != printed) source.Source = printed;

        source.Clear();
        source.Notice = Reading();

        // Beside the patch as it now stands, and not only beside the next step:
        // applying this printing is recorded as a step back to here, and what
        // that step hands back on Ctrl+Z has to include the printing — or the
        // text would come back as somebody's typing, never to be printed again.
        editor.History.Note(Owning());
    }

    /// <summary>
    /// What a printing has to say for itself: where it came from, what it left
    /// behind, and what applying it would do.
    /// </summary>
    private static string Reading()
    {
        return "Printed from the canvas. The patch on the canvas is still the document — "
            + "applying this makes the text the document instead.";
    }

    /// <summary>
    /// Builds the text and puts the patch it describes on the canvas.
    /// </summary>
    /// <remarks>
    /// An edit rather than a new document, so one press of Ctrl+Z takes the
    /// evaluation back. Nothing is rewound: everything the edit did not touch keeps
    /// its accumulator and its delay line (ADR-0067). A text that does not read
    /// changes nothing at all, so there is no half-applied state to be left in.
    /// </remarks>
    /// <param name="applied">What to say under the text once it is built, where the usual count would not do.</param>
    /// <returns>What the text built, which changed something only where it is <see cref="LanguageLoad.Ok"/>.</returns>
    private LanguageLoad Evaluate(string? applied = null)
    {
        var load = PatchLanguage.Build(source.Source);

        if (!load.Ok)
        {
            source.Show(load);
            report.Say(
                $"The text does not read — {load.Errors} thing(s) to fix. Nothing has changed.",
                load.Report);

            return load;
        }

        // Before the patch is replaced, so what is counted is how much of the
        // one that was playing is still here. It is the honest measure of an
        // edit: everything named on both sides kept whatever it was carrying.
        var was = editor.History.Patch;
        var kept = load.Patch.Nodes.Count(node => was.Find(node.Id) is not null);

        // Applying a printing is how somebody takes a patch into text. Said
        // rather than done quietly, because it changes what saving will write.
        var taken = !sourceOwned;

        if (taken)
        {
            sourceOwned = true;
            sourceOnDisk = string.Empty;
            printed = null;
            printedOrder = [];
        }

        // Who owns the patch is settled before the edit is recorded, so the
        // step that edit makes is one the text owns — and taking the step back
        // hands the patch to the canvas along with it.
        editor.History.Mark = Owning();

        CarryControls(was, load.Patch);
        editor.History.Apply(load.Patch);

        // And on the text's stack, where the typing that led to it already is:
        // applying is the last thing somebody did, so it is the first thing that
        // comes back.
        RememberPatchSteps();

        // The map the build just made, rather than one made by building the
        // same text a second time to answer the first caret move.
        mapped = source.Source;
        map = load.Map;

        // The patch those names are of, which is now also the patch on the
        // canvas: applying is what puts the two back in step.
        means = load.Patch;

        RefreshOwnership();

        // And the panel is pointed afresh, because what it could not show a
        // moment ago it can show now. Without this it would go on saying the
        // text had moved on from the patch until somebody moved the caret to
        // ask again — over a patch this text had just been built into.
        PointAt(source.Caret);

        var total = load.Patch.Nodes.Count;

        source.Show(load, applied ?? $"Applied — {total} modules, {kept} of them carried over.");

        report.Say(taken
            ? $"Applied. The text is the document from here on — {total} modules."
            : $"Applied — {total} modules, {kept} carried over.");

        return load;
    }

    /// <summary>
    /// Puts a patch an assistant built on the canvas, as an edit, and keeps the
    /// text in step with it.
    /// </summary>
    /// <remarks>
    /// An assistant builds a graph, and no text describes one. Over a printing that
    /// is an edit like any other and the printing is made afresh. Where the text is
    /// the document it has to go on saying what the canvas holds — it is what a
    /// save writes and what the next apply builds — so the patch is printed into it
    /// and built from there, the way a preset picked from the text view is. The
    /// printing and the build are one thing to take back, and taking them back
    /// returns the text as it was written, comments and all.
    /// </remarks>
    public void TakeFromAssistant(Patch patch)
    {
        if (!sourceOwned)
        {
            editor.History.Apply(patch);
            Reprint();

            return;
        }

        bool read;

        // Said under the text rather than on the status bar, which the assistant
        // panel clears as the turn ends.
        using (source.Together())
        {
            source.Rewrite(PatchPrinter.Print(patch));

            read = Evaluate("The assistant's patch, written as text. Ctrl+Z puts back the text "
                + "as it was, and the patch with it.").Ok;
        }

        // A printing that will not read is this program's fault, and the text it
        // replaced is somebody's work: back it comes, with nothing applied.
        if (!read) source.Undo();
    }

    /// <summary>
    /// Makes the printing of a patch that has just arrived the document, for one
    /// that arrived while the text was showing.
    /// </summary>
    /// <remarks>
    /// The two steps somebody would otherwise take by hand, because picking a
    /// preset from the text view has already said which view they mean to work in.
    /// Only for a preset: it holds no groups, so its printing is the same
    /// instrument written another way, where a <c>.fbk</c> is somebody's own patch
    /// and its groups are their work.
    /// </remarks>
    public void ReadIntoText()
    {
        Evaluate();

        // A printing that will not read is this program's fault rather than
        // anybody's: Evaluate has said what is wrong with it and left the patch
        // where it was, which is on a canvas that still owns it.
        if (!sourceOwned) return;

        // Nothing has been typed and nothing drawn, so there is nothing to lose and
        // nothing to take back. The evaluation above is how the patch arrived
        // rather than an edit anybody made, so both stacks forget it: left on the
        // canvas's, one press would hand back the patch that was open before the
        // preset was picked.
        editor.History.MarkOpened();
        source.ForgetSteps();
        MarkSourceSaved();
        reactions.Raise(new EditStateChanged());

        // And no word under the text about what the build made of it. What
        // Evaluate leaves there answers "how much of what was playing survived",
        // which is a question about an edit somebody made — and nobody made this
        // one, so the answer would be a nought against a patch a moment old.
        source.Clear();

        report.Say($"Read into text — {editor.History.Patch.Nodes.Count} modules. The text is the "
            + "document, so the canvas is a view of it until you hand it back.");
    }

    /// <summary>
    /// Gives the patch back to the canvas, which is what applying does in reverse.
    /// </summary>
    /// <remarks>
    /// A gesture that can only be made in one direction is a trap however well it
    /// is labeled: without this, somebody who applied a printing to try something
    /// and then wanted to drag one wire would have to save the patch as a
    /// <c>.fbk</c> to be allowed to. Nothing is built and nothing rewound — the
    /// canvas already holds what the text made, and what changes hands is who owns
    /// it, through the same door a <c>.fbk</c> arriving uses. The buffer is emptied
    /// and written nowhere, so the caller asks about unsaved typing first.
    /// </remarks>
    public void HandBack()
    {
        if (!sourceOwned) return;

        usage.Count(Used.HandedBack);

        // To the canvas, which is the one thing this gesture is asked for: the
        // button is under the text and pressing it means somebody wants to draw.
        // Before the handover rather than after, since a handover prints into
        // whichever view is showing and this one is on its way out.
        ShowCode(false);
        DropSource();

        report.Say("The canvas is the document from here on. The text view prints it afresh on "
            + "the next look, and applying that printing takes it back into text.");
    }

    /// <summary>
    /// Takes text that has just been opened as the document.
    /// </summary>
    /// <param name="saved">Whether the text is on disk as it stands, which recovered work is not.</param>
    public void TakeSource(string text, bool saved = true)
    {
        sourceOwned = true;
        sourceOnDisk = saved ? text : string.Empty;
        printed = null;
        printedOrder = [];

        source.Source = text;
        source.Clear();

        Forget();

        // The steps behind this belong to the document that has just arrived,
        // not to the one it replaced — and no edit was made to bring it, so
        // there is no step for an undo to find the change on.
        editor.History.Remark(Owning());

        RefreshOwnership();
        ShowCode(true);
    }

    /// <summary>
    /// Hands the patch back to the graph, for a document that arrived as one.
    /// </summary>
    /// <remarks>
    /// The buffer is emptied rather than left holding the last document's text,
    /// which would be a piece of some other patch under a notice claiming to
    /// describe this one. The view is not moved: which of the two is showing is
    /// where somebody is looking, and a document arriving answers who owns the
    /// patch. The one document that does move the view is a <c>.fbks</c>, which
    /// arrives as text and locks the canvas besides (<see cref="TakeSource"/>).
    /// </remarks>
    public void DropSource()
    {
        sourceOwned = false;
        sourceOnDisk = string.Empty;
        printed = null;
        printedOrder = [];

        source.Source = string.Empty;
        source.Clear();

        Forget();
        editor.History.Remark(Owning());
        RefreshOwnership();

        // Straight away rather than on the next look, because this is the look:
        // an emptied buffer left in front of somebody is a text view saying the
        // new patch is nothing at all.
        if (showingCode) PrintForReading();
    }

    /// <summary>
    /// Drops what was known about the text, for a document arriving in place of
    /// another. A map of the last patch would point the panel at modules this one
    /// never had.
    /// </summary>
    private void Forget()
    {
        map = SourceMap.Empty;
        mapped = null;
        means = null;
        unstacked = 0;
        sinceHandover = null;

        Forgot?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Marks the text as written, so closing stops asking about it.</summary>
    /// <remarks>
    /// Beside every step the text owns as well as here. What is on disk is a fact
    /// about the disk, and a step noted before the save would otherwise hand back
    /// an unsaved document over a file that has it to the letter — Ctrl+Z and back
    /// across the apply that made the text the document is all it took.
    /// </remarks>
    public void MarkSourceSaved()
    {
        sourceOnDisk = source.Source;

        var saved = sourceOnDisk;

        editor.History.Remark(mark => mark is Ownership { Owned: true } was ? was with { OnDisk = saved } : mark);
    }

    /// <summary>
    /// Puts the canvas and the text in step with who owns the patch, and says so.
    /// </summary>
    /// <remarks>
    /// The canvas keeps everything that looks — selecting, panning, framing,
    /// copying — and loses everything that changes. The inspector stays live and is
    /// the one thing on a locked canvas that does, because everything a module
    /// carries is written back into the text.
    /// </remarks>
    private void RefreshOwnership()
    {
        editor.History.Locked = sourceOwned;

        // And what the canvas notes beside every step it records from here on,
        // so that an undo across an evaluation hands the patch back.
        editor.History.Mark = Owning();

        source.Notice = sourceOwned ? null : Reading();
        source.Editable = true;

        // Offered only where it would change something. Over a printing the
        // canvas is the document already, and a button saying so would be a
        // button that does nothing.
        source.Owns = sourceOwned;

        reactions.Raise(new OwnershipChanged());
    }

    /// <summary>
    /// Hands the knobs of a patch to the one built from its text, with the links of
    /// every module the text kept. The text has no way to write either.
    /// </summary>
    private static void CarryControls(Patch from, Patch to)
    {
        if (from.Controls is null || to.Controls is not null) return;

        to.Controls = [.. from.Controls.Select(c => c.Clone())];

        foreach (var node in to.Nodes)
        {
            if (from.Find(node.Id) is not { } was || node.StateOf(ControlMap.StateKey) is not null) continue;

            foreach (var (port, link) in ControlMap.All(was))
                if (port < node.InputValues.Length)
                    ControlMap.Link(node, port, link);
        }
    }
}
