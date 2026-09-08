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
    /// Whether there is typing here that has not been made into a patch — which
    /// is the one thing that can be lost without the editor's history knowing
    /// about it, since nothing typed reaches the patch until it is applied.
    /// </summary>
    private bool SourceIsUnapplied => sourceOwned && source.Source != sourceOnDisk;

    /// <summary>Called once, as the window is built.</summary>
    private void WireSource()
    {
        source.IsVisible = false;
        source.EvaluateRequested += (_, _) => Evaluate();
        source.Changed += (_, _) => RefreshEditState();
        source.Moved += (_, at) => PointAt(at);

        codeButton.IsCheckedChanged += (_, _) => ShowCode(codeButton.IsChecked == true);

        // Caught on the way up and after whoever handled it, because a slider
        // captures the pointer: letting go halfway across the window is still
        // letting go of the slider, and the value written should be the one the
        // control finished on.
        inspector.AddHandler(
            PointerReleasedEvent,
            (_, _) => WriteBack(),
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        // A number typed rather than dragged has no gesture to wait for. It is
        // finished when the box stops being the thing being typed into.
        inspector.AddHandler(LostFocusEvent, (_, _) => WriteBack(), RoutingStrategies.Bubble);
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

            map = sourceOwned
                ? PatchLanguage.Build(text).Map
                : printed == text
                    ? PatchPrinter.Locate(editor.Patch, text, printedOrder)
                    : SourceMap.Empty;

            mapped = text;

            return map;
        }
    }

    /// <summary>
    /// Points the inspector at the module the caret is standing in.
    /// </summary>
    /// <remarks>
    /// The same panel the canvas points, because it is the same selection: what
    /// a person is looking at in one view is what the other should be about. A
    /// caret on a word that names no module selects none, which is honest — the
    /// space between two statements is not a module.
    /// </remarks>
    private void PointAt(int at)
    {
        if (!showingCode || writingBack) return;

        editor.Select(Map.At(at));
    }

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
        if ((turned.Count == 0 && restated.Count == 0) || writingBack) return;

        var knobs = turned.ToArray();
        var kept = restated.ToArray();
        var lost = 0;

        turned.Clear();
        restated.Clear();
        writingBack = true;

        try
        {
            foreach (var (id, port) in knobs) Write(id, port, ref lost);
            foreach (var (id, key) in kept) Carry(id, key, ref lost);
        }
        finally
        {
            writingBack = false;
        }

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

    /// <summary>Takes back the last thing done to whichever view is showing.</summary>
    private void Undo()
    {
        if (Coding) source.Undo();
        else editor.Undo();

        RefreshEditState();
    }

    private void Redo()
    {
        if (Coding) source.Redo();
        else editor.Redo();

        RefreshEditState();
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

        source.Source = printed;
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

        editor.ApplyEdit(load.Patch);

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

        // The map the build just made, rather than one made by building the
        // same text a second time to answer the first caret move.
        mapped = source.Source;
        map = load.Map;

        RefreshOwnership();

        var total = load.Patch.Nodes.Count;

        source.Show(load, $"Applied — {total} modules, {kept} of them carried over.");

        Report(taken
            ? $"Applied. The text is the document from here on — {total} modules."
            : $"Applied — {total} modules, {kept} carried over.");
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
        RefreshOwnership();
        ShowCode(true);
    }

    /// <summary>
    /// Hands the patch back to the graph, for a document that arrived as one.
    /// </summary>
    /// <remarks>
    /// The buffer is emptied rather than left holding the last document's text,
    /// which would otherwise be printed over on the next look anyway — and until
    /// then would be a piece of some other patch sitting under a notice claiming
    /// to describe this one.
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
        RefreshOwnership();

        if (showingCode) ShowCode(false);
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
        turned.Clear();
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

        ToolTip.SetTip(
            inspector,
            sourceOwned
                ? "The text is the document. A knob turned here is written back into it "
                  + "where it already says it."
                : null);
    }
}
