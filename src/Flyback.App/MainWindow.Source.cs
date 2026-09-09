using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Core.Language;

namespace Flyback.App;

/// <summary>
/// The patch as text, beside the patch as a graph, and which of the two is the
/// document.
/// </summary>
/// <remarks>
/// <para>
/// One patch and two views of it, so something has to own it — and what owns it
/// is the file that was opened rather than the view that happens to be showing
/// (ADR-0068). Open a <c>.fbks</c> and the text is the document: the canvas
/// shows what it builds and is not editable, because the next evaluation would
/// take any edit straight back off. Open a <c>.fbk</c> and the graph is the
/// document: the text view still opens, but what it shows is a printing, which
/// is a reading rather than a round trip.
/// </para>
/// <para>
/// The two are not symmetrical and the design follows that rather than papering
/// over it. Building text into a patch is exact. Printing a patch back out is
/// not — it drops the groups entirely and lays the canvas out afresh (ADR-0065)
/// — so a printing is never adopted behind somebody's back. It is offered,
/// labelled, and becomes the document only when they apply it.
/// </para>
/// </remarks>
public sealed partial class MainWindow
{
    private readonly SourceView source = new();

    private readonly ToggleButton codeButton =
        Toggle("code", "{ }", "Show the patch as text  (F2)");

    /// <summary>
    /// Whether the text is the document. False for a patch that came from a
    /// <c>.fbk</c>, a bundle or a preset, where the graph is.
    /// </summary>
    private bool sourceOwned;

    /// <summary>
    /// The last printing this made of a graph-owned patch, so that opening the
    /// text view twice does not write over what somebody typed into it the first
    /// time and did not apply.
    /// </summary>
    /// <remarks>
    /// Kept in step with a knob written back into a printing, which leaves the
    /// text a printing still: what changed is the number the printing was always
    /// going to say for a knob that has just moved.
    /// </remarks>
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
    /// printing and builds nothing.
    /// </summary>
    /// <remarks>
    /// Kept beside the map because the map's names are this patch's, and the
    /// patch on the canvas is a different one whenever the text has moved on
    /// from what was last applied — see <see cref="Adrift"/>.
    /// </remarks>
    private Patch? means;

    /// <summary>
    /// The text <see cref="map"/> was made from, or null for no map at all.
    /// </summary>
    /// <remarks>
    /// Null rather than empty, because an empty document is a text like any
    /// other and a map of one is a perfectly good answer. What this has to say
    /// is that there is no answer yet.
    /// </remarks>
    private string? mapped;

    /// <summary>
    /// Whether the text is being written to from the panel rather than typed
    /// into.
    /// </summary>
    /// <remarks>
    /// Replacing a knob's number moves everything after it along, the caret
    /// included, and the editor reports that as a caret move — which it is not.
    /// Nobody moved it and the selection must not follow it, or letting go of a
    /// slider would empty the panel that slider is in.
    /// </remarks>
    private bool writingBack;

    /// <summary>
    /// Knobs turned in the panel since the hand last came off one.
    /// </summary>
    /// <remarks>
    /// A drag is one gesture and should be one edit: writing per frame would put
    /// a hundred things on the undo stack and flicker the line under whoever is
    /// reading it. Every frame still reaches the engine — that is the point of
    /// turning a knob while a patch is playing — and only the document waits.
    /// </remarks>
    private readonly HashSet<(Guid Node, int Port)> turned = [];

    /// <summary>
    /// What a module carries that is not a knob, changed since the same moment —
    /// a plugin's field by its key, and the tune, scale or file a module carries
    /// as the one thing it has no key for.
    /// </summary>
    private readonly HashSet<(Guid Node, string? Key)> restated = [];

    /// <summary>The text as it was last opened or written, for the unsaved question.</summary>
    private string sourceOnDisk = string.Empty;

    /// <summary>Whether the text view is the one showing.</summary>
    private bool showingCode;

    /// <summary>
    /// Who owned the patch, and what the text was to it, at one point in the
    /// canvas's history.
    /// </summary>
    /// <remarks>
    /// Applying text is an edit and a handover at once — it puts a patch on the
    /// canvas and it makes the text the document — so taking the edit back has
    /// to take the handover back with it. Otherwise undoing an evaluation
    /// leaves a canvas nobody wrote any text for locked behind text claiming to
    /// describe it, and the only way out is a handover made by hand.
    /// <para>
    /// Kept beside the step rather than worked out afterwards, because a
    /// snapshot says what a patch was and nothing about where it came from.
    /// </para>
    /// </remarks>
    private sealed record Ownership(
        bool Owned,
        string OnDisk,
        string? Printed,
        IReadOnlyList<Guid> Order);

    /// <summary>Who owns the patch as things now stand.</summary>
    private Ownership Owning() => new(sourceOwned, sourceOnDisk, printed, printedOrder);

    /// <summary>
    /// Steps the canvas has recorded that the text's stack has not been told
    /// about yet.
    /// </summary>
    /// <remarks>
    /// Counted rather than put on that stack as they happen, because a knob is
    /// turned before its number is written into the text and the two are one
    /// thing somebody did. The count waits for the write-back and goes on the
    /// stack inside it, so one press takes back the number and the sound
    /// together.
    /// </remarks>
    private int unstacked;

    /// <summary>
    /// Whether a step of the text's stack is being walked right now.
    /// </summary>
    /// <remarks>
    /// Nothing may be put on that stack while it is being read off. Walking a
    /// step rebuilds the panel, and a control losing the focus to that is a
    /// write-back as far as everything downstream can tell — one that would try
    /// to record itself in the middle of the undo it was caused by.
    /// </remarks>
    private bool stepping;

    /// <summary>
    /// Whether there is typing here that has not been made into a patch — which
    /// is the one thing that can be lost without the editor's history knowing
    /// about it, since nothing typed reaches the patch until it is applied.
    /// </summary>
    private bool SourceIsUnapplied => sourceOwned && source.Source != sourceOnDisk;

    /// <summary>Called once, as the window is built.</summary>
    private void WireSource()
    {
        source.IsVisible = false;

        // The canvas opens on a preset, which is a patch the graph owns. Said
        // before the first one reaches it, so the earliest step in its history
        // knows whose the patch was.
        editor.Mark = Owning();

        // While the text is the document its stack is the history, so a step the
        // canvas records is one that stack has to be able to take back.
        editor.Recorded += (_, _) =>
        {
            if (sourceOwned) unstacked++;
        };

        source.EvaluateRequested += (_, _) => Evaluate();
        source.Changed += (_, _) => RefreshEditState();
        source.Moved += (_, at) => PointAt(at);
        source.HandBackRequested += async (_, _) => await HandBackAsync();

        codeButton.IsCheckedChanged += (_, _) => ShowCode(codeButton.IsChecked == true);

        // Caught on the way up and after whoever handled it, because a slider
        // captures the pointer: letting go halfway across the window is still
        // letting go of the slider, and the value written should be the one the
        // control finished on.
        inspector.AddHandler(
            PointerReleasedEvent,
            (_, _) => HandCameOff(),
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        // A number typed rather than dragged has no gesture to wait for. It is
        // finished when the box stops being the thing being typed into.
        inspector.AddHandler(LostFocusEvent, (_, _) => HandCameOff(), RoutingStrategies.Bubble);
    }

    /// <summary>
    /// The hand has come off whatever it was holding in the panel: the gesture
    /// is over, and what it changed goes into the text.
    /// </summary>
    /// <remarks>
    /// Both halves matter whether or not the text view has ever been opened. The
    /// canvas files an edit made here under the control it came from, and every
    /// drag of one slider is that same name — so this is the only thing that can
    /// tell the history one drag from the next, and without it two of them an
    /// afternoon apart come back in a single press.
    /// </remarks>
    private void HandCameOff()
    {
        editor.GestureEnded();
        WriteBack();
    }

    /// <summary>
    /// Where the text says each module it describes, worked out again whenever
    /// the text has moved on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two sources for it and one shape. Text that owns the patch is built, and
    /// the binder notes where everything came from on the way through. A
    /// printing is not built — the patch is already on the canvas and building
    /// the printing would make a second copy of it with different ids — so the
    /// printer says where it put things instead.
    /// </para>
    /// <para>
    /// A printing is only mapped while it is still the printing. Once somebody
    /// types into one it is text about a patch that may no longer be there, and
    /// the honest answer is to point at nothing rather than at whatever used to
    /// be under the caret.
    /// </para>
    /// </remarks>
    private SourceMap Map
    {
        get
        {
            if (mapped == source.Source) return map;

            var text = source.Source;

            if (sourceOwned)
            {
                // The patch this text describes, which is not always the patch
                // on the canvas — see Adrift.
                var load = PatchLanguage.Build(text);

                map = load.Map;
                means = load.Patch;
            }
            else
            {
                map = printed == text
                    ? PatchPrinter.Locate(editor.Patch, text, printedOrder)
                    : SourceMap.Empty;

                // A printing is made from the patch on the canvas, so the two
                // cannot disagree and there is nothing to hold beside it.
                means = null;
            }

            mapped = text;

            return map;
        }
    }

    /// <summary>
    /// Whether the caret is standing on a module the patch on the canvas has
    /// moved on from, so that the panel has nothing honest to show for it.
    /// </summary>
    /// <remarks>
    /// Read by the panel, which says so where it would otherwise sit empty and
    /// leave somebody clicking at a word that answers nothing.
    /// </remarks>
    private bool adrift;

    /// <summary>
    /// Points the inspector at the module the caret is standing in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same panel the canvas points, because it is the same selection: what
    /// a person is looking at in one view is what the other should be about. A
    /// caret on a word that names no module selects none, which is honest — the
    /// space between two statements is not a module.
    /// </para>
    /// <para>
    /// And a word the patch has moved on from selects none either, which is the
    /// same honesty with something to say for itself — see <see cref="Adrift"/>.
    /// </para>
    /// </remarks>
    private void PointAt(int at)
    {
        if (!showingCode || writingBack) return;

        var named = Map.At(at);
        var lost = named is { } id && Adrift(id);

        // Before the selection, because changing it is what rebuilds the panel
        // and the panel reads this on the way past.
        var moved = lost != adrift;

        adrift = lost;

        editor.Select(lost ? null : named);

        // And where the selection did not change — a caret moving between two
        // words the patch has both moved on from — the panel is asked again
        // anyway, since what it has to say has changed even though what is
        // selected has not.
        if (moved && editor.SelectedNode is null) BuildInspector();
    }

    /// <summary>
    /// Whether the module the text means by <paramref name="id"/> is not the one
    /// the canvas has under that name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The binder names a module after where it stands, so a module typed in
    /// ahead of another renames that other one. Between an edit and the apply
    /// that answers for it — and for one press after an undo takes that apply
    /// back, which leaves the text ahead of the patch on purpose — the names in
    /// the text are not the names on the canvas. A name that is simply not there
    /// pointed the panel at nothing; a name that is there and means something
    /// else pointed it at the wrong module, which is worse for being quiet.
    /// </para>
    /// <para>
    /// The type is what is compared, which catches everything but a module
    /// swapped for another of its own kind — and that one costs nothing, since
    /// what the panel would show is the same row of knobs either way.
    /// </para>
    /// </remarks>
    private bool Adrift(Guid id) =>
        means is { } text && text.Find(id)?.TypeId != editor.Patch.Find(id)?.TypeId;

    /// <summary>Notes a knob the panel has just turned, for the next write-back.</summary>
    private void Turned(Guid node, int port) => turned.Add((node, port));

    /// <summary>
    /// Notes something a module carries that the panel has just changed.
    /// </summary>
    /// <param name="key">
    /// A plugin field's key, or null for the one thing a module carries that has
    /// no key — its tune, its scale or the file it names.
    /// </param>
    private void Restated(Guid node, string? key = null) => restated.Add((node, key));

    /// <summary>
    /// What a module carries has been edited: heard now, and written into the
    /// text when the hand comes off it.
    /// </summary>
    private void Edited(NodeInstance node, string? because = null)
    {
        Restated(node.Id);
        editor.NotifyPatchChanged(because);
    }

    /// <summary>
    /// Writes the knobs turned since the last gesture into the text, each where
    /// the text already says it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what lets the panel be used at all while the text is the
    /// document. Without it a knob turned there is heard at once and gone at the
    /// next apply, which is the worst of both. Nothing is rebuilt: the patch
    /// already has the value and the engine already has the patch, and building
    /// here would replace the patch under the very control being dragged.
    /// </para>
    /// <para>
    /// The map is asked again for each one, because the first edit moves
    /// everything after it along.
    /// </para>
    /// </remarks>
    private void WriteBack()
    {
        if ((turned.Count == 0 && restated.Count == 0) || writingBack || stepping) return;

        var knobs = turned.ToArray();
        var kept = restated.ToArray();
        var lost = 0;

        turned.Clear();
        restated.Clear();
        writingBack = true;

        // The numbers written here and the turning of the knobs behind them are
        // one thing somebody did, and come back in one press. Without this the
        // text would go back and the sound would not, which is the two of them
        // saying different things about one patch.
        using (source.Together())
        {
            try
            {
                foreach (var (id, port) in knobs) Write(id, port, ref lost);
                foreach (var (id, key) in kept) Carry(id, key, ref lost);
            }
            finally
            {
                writingBack = false;
            }

            RememberPatchSteps();
        }

        // A printing is a reading and not a document, so what has just been
        // written into it is not an edit anybody made: the canvas's history is
        // the only one there is, and an undo falls through to it and makes the
        // reading afresh. Left on this stack instead, one press would put the
        // old number back over a patch still playing the new one — and leave
        // the text no longer the printing it says it is, which is what stops
        // the caret pointing the panel. Said after the group is closed, since
        // emptying a stack closes what is open on it.
        if (!sourceOwned) source.ForgetSteps();

        if (lost > 0)
            Report($"{lost} value(s) could not be written into the text — "
                + "the code says them in a form this cannot change in place.");
    }

    /// <summary>Puts one knob into the text, or counts it as one that could not go.</summary>
    private void Write(Guid id, int port, ref int lost)
    {
        if (editor.Patch.Find(id) is not { } node
            || NodeCatalog.Get(node.TypeId) is not { } def
            || port >= def.Inputs.Count
            || port >= node.InputValues.Length)
        {
            return;
        }

        var spec = def.Inputs[port];
        var value = PatchPrinter.Knob(node.InputValues[port], spec.Display);

        Put(Map.Knob(id, spec.Name.Replace(' ', '_'), value), id, ref lost);
    }

    /// <summary>
    /// Puts something a module carries that is not a knob back into the text.
    /// </summary>
    /// <remarks>
    /// Each kind where the language already says it: a plugin's field as a named
    /// argument, exactly as a knob is; a tune or a scale as the block after the
    /// call; and a file as the one string a call carries. Only what changed, so
    /// touching one field does not restate every other one the module has.
    /// </remarks>
    private void Carry(Guid id, string? key, ref int lost)
    {
        if (editor.Patch.Find(id) is not { } node || NodeCatalog.Get(node.TypeId) is not { } def) return;

        if (key is not null)
        {
            foreach (var extra in def.Extras)
                foreach (var field in extra.Fields)
                    if (field.Key == key && PatchPrinter.Field(node, extra, field) is { } value)
                        Put(Map.Knob(id, field.Key, value), id, ref lost);

            return;
        }

        // A block that says nothing rather than no block at all: a tune emptied
        // in the panel has to empty in the text too, and a call with nothing
        // after it is a call that leaves whatever is there alone.
        if (def.Extra<StepsExtra>() is not null || def.Extra<ScaleExtra>() is not null)
        {
            Put(Map.Carried(id, PatchPrinter.Carried(node, def) ?? "[ ]"), id, ref lost);
            return;
        }

        if (PatchPrinter.Held(node, def) is { Length: > 0 } path) Put(Map.File(id, path), id, ref lost);
    }

    /// <summary>
    /// Makes one edit, or counts it as one the text had nowhere to take.
    /// </summary>
    /// <remarks>
    /// Nothing written where something should have been is said out loud rather
    /// than dropped quietly: the value is about to be lost and whoever changed it
    /// can still do something else about it.
    /// </remarks>
    private void Put(Change? change, Guid id, ref int lost)
    {
        if (change is not { } edit)
        {
            if (Map.Where(id) is not null) lost++;
            return;
        }

        if (!source.Apply(edit)) return;

        // Still a printing, and now a true one — said before anything can ask
        // for the map again, since what makes this text a printing is the two
        // of them agreeing.
        if (!sourceOwned) printed = source.Source;

        mapped = null;
    }

    /// <summary>
    /// Whether the three gestures every editor has — take it back, put it back,
    /// tidy it up — are the text's rather than the canvas's.
    /// </summary>
    /// <remarks>
    /// The view that is showing, rather than the one that owns the patch. All
    /// three act on what somebody is looking at, and looking at the text is
    /// what makes Ctrl+Z mean the last thing typed. Switching over hands them
    /// back, and neither stack is disturbed by the other — a run of evaluations
    /// is still there to be undone on the canvas after an afternoon of typing.
    /// </remarks>
    private bool Coding => showingCode;

    /// <summary>
    /// Takes back the last thing done to whichever view is showing — or, where
    /// that view has nothing left to take back, the last thing done to the
    /// patch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Where the text is the document, its stack is the document's history from
    /// either view. Typing, applying and turning a knob are one run of things
    /// somebody did, in the order they did them, so the last two go on that
    /// stack beside the typing rather than on a second one that would afterwards
    /// have to be interleaved with it by guessing. The canvas is a view of that
    /// document, so Ctrl+Z there means what it means at the text.
    /// </para>
    /// <para>
    /// Where the graph is the document the two are independent again and undo
    /// follows the view: the modules' steps on the canvas, and whatever has been
    /// typed into a printing at the text. Either way the gesture falls through
    /// when the stack it lands on has nothing left — otherwise applying a
    /// printing, which is loaded rather than typed and so leaves an empty stack
    /// behind it, would be the one thing nobody could take back.
    /// </para>
    /// </remarks>
    private void Undo()
    {
        if (Gesturing) return;

        if (Documenting && source.CanUndo) source.Undo();
        else if (editor.Undo())
        {
            Owed(-1);
            Stepped();
        }

        RefreshEditState();
    }

    private void Redo()
    {
        if (Gesturing) return;

        if (Documenting && source.CanRedo) source.Redo();
        else if (editor.Redo())
        {
            Owed(1);
            Stepped();
        }

        RefreshEditState();
    }

    /// <summary>
    /// Counts a canvas step that this gesture took back or put again without the
    /// text's stack being involved.
    /// </summary>
    /// <remarks>
    /// A step reached this way is one that stack had not been told about, or the
    /// gesture would have landed there instead of falling through to the canvas.
    /// Left counted, the next write-back would put it on that stack as well —
    /// and one press there would then take back two edits, the second of them
    /// one somebody had already taken back by hand.
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
    /// Makes the printing say what the canvas now says, for a patch that has
    /// moved under it.
    /// </summary>
    /// <remarks>
    /// A printing is a reading of the canvas, and one that stopped agreeing with
    /// it the moment anything moved would be a reading of nothing — the same
    /// reason a knob turned in the panel is written into it in place. An undo is
    /// the one thing that moves a graph-owned patch while the text is up, since
    /// the canvas itself is not on screen to be dragged.
    /// <para>
    /// Only over a buffer that is still the printing. Typing is never printed
    /// over: somebody who wrote something here keeps it, and what they have is
    /// text about a patch that has moved on, which is what the map already stops
    /// answering for.
    /// </para>
    /// </remarks>
    private void Reprint()
    {
        if (sourceOwned || source.Source != printed) return;

        var at = source.Caret;

        PrintForReading();

        // As near to where they were reading as the new text has room for.
        source.Caret = Math.Min(at, source.Source.Length);
    }

    /// <summary>
    /// Whether the text's stack is the one these gestures land on first — see
    /// <see cref="Undo"/>.
    /// </summary>
    private bool Documenting => sourceOwned || Coding;

    /// <summary>
    /// Whether a hand is in the middle of something, so that taking an edit back
    /// would be taking it out from under that hand.
    /// </summary>
    /// <remarks>
    /// The pointer is captured for the length of a drag and the keyboard is not,
    /// so Ctrl+Z arrives in the middle of one perfectly well. The canvas drops
    /// the drag whenever it is shown a different patch, so what somebody
    /// dragging a module got for the press was that module
    /// jumping out from under the pointer to wherever the step it landed on had
    /// put it, with the drag over and no sign of why. Ignored rather than
    /// answered: letting go finishes the gesture and leaves the press to be made
    /// again, against a patch nobody is holding on to.
    /// </remarks>
    private bool Gesturing => editor.Gesturing;

    /// <summary>
    /// Puts the patch steps taken since the last of these on the text's stack,
    /// as one thing to take back.
    /// </summary>
    /// <remarks>
    /// A count rather than the steps themselves, because the canvas is already
    /// keeping them and keeping them twice is how two records of one edit come
    /// to disagree. What goes on the text's stack is how many of them to walk
    /// back, so an undo there is an undo here: one history, reached through
    /// whichever view somebody is working in.
    /// </remarks>
    private void RememberPatchSteps()
    {
        if (unstacked == 0 || stepping) return;

        var steps = unstacked;
        unstacked = 0;

        source.Remember(() => StepPatch(steps, back: true), () => StepPatch(steps, back: false));
    }

    /// <summary>Walks the canvas's history, and who owns the patch, with it.</summary>
    private void StepPatch(int steps, bool back)
    {
        stepping = true;

        try
        {
            for (var step = 0; step < steps; step++)
                if (back ? editor.Undo() : editor.Redo())
                    Handed();
        }
        finally
        {
            stepping = false;
        }

        RefreshEditState();
    }

    /// <summary>
    /// Puts back who owned the patch at the step the canvas has just arrived at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The only edit that changes hands is an evaluation, so this does nothing
    /// at all across the run of drags and wires either side of one. Where it
    /// does something it moves the view with it, because the handover is the
    /// half of that edit somebody can see: taking an adoption back puts the
    /// canvas up, which is where the modules they wanted back are, and putting
    /// it back shows the text that is the document again.
    /// </para>
    /// <para>
    /// Who owns the patch is the whole of the question, and the rest of the
    /// note is only what to put back once the answer has changed. A step
    /// recorded before anybody opened the text view remembers there being no
    /// printing, because at the time there was none — and taking that step back
    /// is not a handover, so nothing about the printing somebody is reading now
    /// is any of its business.
    /// </para>
    /// </remarks>
    private void Handed()
    {
        if (editor.Mark is not Ownership was || was.Owned == sourceOwned) return;

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

        Report(sourceOwned
            ? "Put back — the text is the document again."
            : "Taken back — the canvas is the document again, so its modules can be "
              + "dragged, wired and grouped.");
    }

    /// <summary>
    /// Lays out what is showing: the modules across the canvas, or the lines
    /// down the page.
    /// </summary>
    /// <remarks>
    /// The same button and the same key for both, because they are the same
    /// thing done to the two views of one patch — and the pass behind each is
    /// the other's counterpart besides (<see cref="Core.Language.SourceLayout"/>
    /// and <see cref="Core.Graph.PatchLayout"/>).
    /// </remarks>
    private void Tidy()
    {
        if (Coding) source.Tidy();
        else editor.Tidy();
    }

    /// <summary>
    /// Puts the text view over the canvas, or takes it off.
    /// </summary>
    /// <remarks>
    /// Two children of one row rather than a third panel stacked under the
    /// assistant: they are two views of one patch, and showing both would ask
    /// the question this design exists to answer — which of them is being
    /// edited. Visibility rather than reparenting, for the reason the fullscreen
    /// preview does not reparent either.
    /// </remarks>
    private void ShowCode(bool shown)
    {
        showingCode = shown;

        if (shown && !sourceOwned) PrintForReading();

        source.IsVisible = shown;
        editor.IsVisible = !shown;

        if (codeButton.IsChecked != shown) codeButton.IsChecked = shown;

        if (shown)
        {
            source.Focus();

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
    /// Writes the patch on the canvas out as text, for somebody to read.
    /// </summary>
    /// <remarks>
    /// Only over a buffer nobody has touched. Somebody who typed here, switched
    /// to the canvas to look something up and came back would otherwise find
    /// their work replaced by a printing of a patch they had not changed, which
    /// is the worst thing this feature could do.
    /// </remarks>
    private void PrintForReading()
    {
        if (source.Source.Length != 0 && source.Source != printed) return;

        var writing = PatchPrinter.Written(editor.Patch);

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
    }

    /// <summary>
    /// What a printing has to say for itself: where it came from, what it left
    /// behind, and what applying it would do.
    /// </summary>
    private string Reading()
    {
        var groups = editor.Patch.Groups?.Count ?? 0;

        var lost = groups == 0
            ? string.Empty
            : $" Text has no place to keep groups, so its {groups} of them are not here.";

        return $"Printed from the canvas.{lost} The patch on the canvas is still the document — "
            + "applying this makes the text the document instead.";
    }

    /// <summary>
    /// Builds the text and puts the patch it describes on the canvas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An edit rather than a new document, so one press of Ctrl+Z takes the
    /// evaluation back and the canvas history becomes a history of evaluations.
    /// Nothing is rewound: the point of applying a patch while it plays is that
    /// it goes on playing, and everything the edit did not touch keeps its
    /// accumulator and its delay line (ADR-0067).
    /// </para>
    /// <para>
    /// A text that does not read changes nothing at all. The language builds a
    /// patch or refuses to, so there is no half-applied state to be left in —
    /// which is what makes an evaluation safe to try rather than something to
    /// be sure about first.
    /// </para>
    /// </remarks>
    private void Evaluate()
    {
        var load = PatchLanguage.Build(source.Source);

        if (!load.Ok)
        {
            source.Show(load);
            Report(
                $"The text does not read — {load.Issues.Count} thing(s) to fix. Nothing has changed.",
                load.Report);

            return;
        }

        // Before the patch is replaced, so what is counted is how much of the
        // one that was playing is still here. It is the honest measure of an
        // edit: everything named on both sides kept whatever it was carrying.
        var was = editor.Patch;
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
        editor.Mark = Owning();

        editor.ApplyEdit(load.Patch);

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

        source.Show(load, $"Applied — {total} modules, {kept} of them carried over.");

        Report(taken
            ? $"Applied. The text is the document from here on — {total} modules."
            : $"Applied — {total} modules, {kept} carried over.");
    }

    /// <summary>
    /// Makes the printing of a patch that has just arrived the document, for one
    /// that arrived while the text was showing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two steps somebody would otherwise take by hand — look at the
    /// printing, press Apply — done for them, because picking a preset from the
    /// text view has already said which of the two views they mean to work in.
    /// Leaving them in the text over a canvas they can still drag modules
    /// around on is the one answer nobody wants: it is the question ADR-0068
    /// exists to settle, put back on the screen.
    /// </para>
    /// <para>
    /// Only for a preset, and the reason is what printing loses. A preset holds
    /// no groups, so its printing is the same instrument written another way and
    /// adopting it costs a layout that the next apply would redo anyway. A
    /// <c>.fbk</c> is somebody's own patch and its groups are their work, so it
    /// stays the graph's and its printing stays offered rather than taken.
    /// </para>
    /// </remarks>
    private void ReadIntoText()
    {
        Evaluate();

        // A printing that will not read is this program's fault rather than
        // anybody's: Evaluate has said what is wrong with it and left the patch
        // where it was, which is on a canvas that still owns it.
        if (!sourceOwned) return;

        // Nothing has been typed and nothing has been drawn, so there is nothing
        // to lose yet and nothing to take back — the same state that picking a
        // preset on the canvas leaves. The evaluation above is how the patch
        // arrived rather than an edit anybody made to it, so both stacks forget
        // it: left on the canvas's, one press would undo the arrival and hand
        // back the patch that was open before the preset was picked, and left
        // on the text's it would be a lit button that walks a step no longer
        // there.
        editor.MarkOpened();
        source.ForgetSteps();
        MarkSourceSaved();
        RefreshEditState();

        // And no word under the text about what the build made of it. What
        // Evaluate leaves there answers "how much of what was playing survived",
        // which is a question about an edit somebody made — and nobody made this
        // one, so the answer would be a nought against a patch a moment old.
        source.Clear();

        Report($"Read into text — {editor.Patch.Nodes.Count} modules. The text is the "
            + "document, so the canvas is a view of it until you hand it back.");
    }

    /// <summary>
    /// Gives the patch back to the canvas, which is what applying does in
    /// reverse.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Applying is how a patch is taken into text and this is how it comes back
    /// out, because a gesture that can only be made in one direction is a trap
    /// however well it is labelled. Without it the canvas is a view until some
    /// other document happens to arrive, and somebody who applied a printing to
    /// try something and then wanted to drag one wire would have to save the
    /// patch as a <c>.fbk</c> to be allowed to — the way out of an adoption
    /// should not be a file operation.
    /// </para>
    /// <para>
    /// Nothing is built and nothing is rewound. The patch on the canvas is
    /// already what the text made, and what changes hands is who owns it — the
    /// same change a <c>.fbk</c> arriving would make, through the same door, so
    /// the canvas that comes back is the one that comes back from opening a
    /// patch file.
    /// </para>
    /// </remarks>
    private async Task HandBackAsync()
    {
        if (!sourceOwned) return;

        // The buffer is emptied by the handover and is written nowhere on the
        // way, so typing that is not on disk yet is asked about here exactly as
        // it is asked about when a document is closed over.
        if (!await MayLoseTheTextAsync()) return;

        // To the canvas, which is the one thing this gesture is asked for: the
        // button is under the text and pressing it means somebody wants to draw.
        // Before the handover rather than after, since a handover prints into
        // whichever view is showing and this one is on its way out.
        ShowCode(false);
        DropSource();

        Report("The canvas is the document from here on. The text view prints it afresh on "
            + "the next look, and applying that printing takes it back into text.");
    }

    /// <summary>
    /// Takes text that has just been opened as the document.
    /// </summary>
    private void TakeSource(string text)
    {
        sourceOwned = true;
        sourceOnDisk = text;
        printed = null;
        printedOrder = [];

        source.Source = text;
        source.Clear();

        Forget();

        // The steps behind this belong to the document that has just arrived,
        // not to the one it replaced — and no edit was made to bring it, so
        // there is no step for an undo to find the change on.
        editor.Remark(Owning());

        RefreshOwnership();
        ShowCode(true);
    }

    /// <summary>
    /// Hands the patch back to the graph, for a document that arrived as one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The buffer is emptied rather than left holding the last document's text,
    /// which would otherwise be printed over on the next look anyway — and until
    /// then would be a piece of some other patch sitting under a notice claiming
    /// to describe this one.
    /// </para>
    /// <para>
    /// The view is not moved. Which of the two is showing is where somebody is
    /// looking, and a document arriving answers a different question — who owns
    /// the patch. So a preset picked while the text is up is read as text, in
    /// the view it was picked from, and what changes is that the text is now a
    /// printing and says so. The one document that does move the view is a
    /// <c>.fbks</c>, which arrives as text and locks the canvas besides
    /// (<see cref="TakeSource"/>).
    /// </para>
    /// </remarks>
    private void DropSource()
    {
        sourceOwned = false;
        sourceOnDisk = string.Empty;
        printed = null;
        printedOrder = [];

        source.Source = string.Empty;
        source.Clear();

        Forget();
        editor.Remark(Owning());
        RefreshOwnership();

        // Straight away rather than on the next look, because this is the look:
        // an emptied buffer left in front of somebody is a text view saying the
        // new patch is nothing at all.
        if (showingCode) PrintForReading();
    }

    /// <summary>
    /// Drops what was known about the text, for a document arriving in place of
    /// another.
    /// </summary>
    /// <remarks>
    /// A map of the last patch would point the panel at modules this one has
    /// never had, and a knob left waiting to be written would be written into
    /// somebody else's file.
    /// </remarks>
    private void Forget()
    {
        map = SourceMap.Empty;
        mapped = null;
        means = null;
        turned.Clear();
        unstacked = 0;
    }

    /// <summary>Marks the text as written, so closing stops asking about it.</summary>
    private void MarkSourceSaved() => sourceOnDisk = source.Source;

    /// <summary>
    /// Puts every control that edits the patch in step with who owns it.
    /// </summary>
    /// <remarks>
    /// The canvas keeps everything that looks — selecting, panning, framing,
    /// copying — and loses everything that changes, so it is still how somebody
    /// reads a source-built patch and picks the module the inspector is about.
    /// The inspector stays live and is the one thing on a locked canvas that
    /// does: everything a module carries — its knobs, its tune, its file, a
    /// plugin's own fields — is written back into the text, so the next apply
    /// builds what is already being heard. What it loses is what the graph is
    /// made of: the buttons that add, group and delete, and the title that
    /// renames, none of which the file would let stand.
    /// <para>
    /// Undo and redo are deliberately left alone. On a source-owned patch the
    /// history is a history of evaluations, and taking one back is exactly what
    /// somebody wants after applying something that turned out worse.
    /// </para>
    /// </remarks>
    private void RefreshOwnership()
    {
        editor.Locked = sourceOwned;

        // And what the canvas notes beside every step it records from here on,
        // so that an undo across an evaluation hands the patch back.
        editor.Mark = Owning();

        // Laying out is off only where it would not last: a locked canvas is
        // re-laid on the next evaluation, so tidying one is work thrown away.
        // Showing the text, the same button folds the lines instead.
        if (tidyButton is not null)
        {
            tidyButton.IsEnabled = Coding || !sourceOwned;

            ToolTip.SetTip(tidyButton, Coding
                ? "Fold the long lines so the patch reads down the page  (Ctrl+L)"
                : sourceOwned
                    ? "The text is the document, so the canvas is laid out from it on every "
                      + "apply. Fold the text instead."
                    : TidyTip);
        }

        // What the empty panel says is a list of gestures, and half of them
        // have just been switched off or back on.
        BuildInspector();
        RefreshEditState();

        source.Notice = sourceOwned ? null : Reading();
        source.Editable = true;

        // Offered only where it would change something. Over a printing the
        // canvas is the document already, and a button saying so would be a
        // button that does nothing.
        source.Owns = sourceOwned;

        ToolTip.SetTip(
            inspector,
            sourceOwned
                ? "The text is the document. A knob turned here is written back into it "
                  + "where it already says it."
                : null);
    }
}
