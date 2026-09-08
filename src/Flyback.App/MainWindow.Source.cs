using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
/// not — it lays the canvas out afresh and opens every box (ADR-0065)
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
    private string? printed;

    /// <summary>The text as it was last opened or written, for the unsaved question.</summary>
    private string sourceOnDisk = string.Empty;

    /// <summary>
    /// Every knob as the text last built it, so a knob turned somewhere else can
    /// be told from one the text already says.
    /// </summary>
    /// <remarks>
    /// Kept rather than worked out by building the text again: this is asked on
    /// every frame of a drag, and rebuilding a patch to answer it would be a
    /// slider that stuttered. What it costs is a float per socket, which for the
    /// largest patch in the box is a few hundred of them.
    /// </remarks>
    private readonly Dictionary<(Guid Node, int Port), float> written = [];

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
        source.Reading += (_, statement) => Reading(statement);

        codeButton.IsCheckedChanged += (_, _) => ShowCode(codeButton.IsChecked == true);
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
    /// Points the inspector at whatever the caret is standing in.
    /// </summary>
    /// <remarks>
    /// A statement names a module when it declares one or turns one of its
    /// knobs, and that name is on the module itself — the binder gives a node
    /// the name it was bound to, so the canvas and the text call the same thing
    /// the same thing. Anything else selects nothing, which is honest: a
    /// pipeline that ends at the Output is about the patch rather than about a
    /// module.
    /// </remarks>
    private void Reading(string statement)
    {
        if (!showingCode) return;

        editor.Select(Named(statement) is { } name
            ? editor.Patch.Nodes.FirstOrDefault(node => node.Name == name)?.Id
            : null);
    }

    /// <summary>The module a statement is about, or null where it is about none.</summary>
    private static string? Named(string statement)
    {
        var said = statement.AsSpan().TrimStart();

        if (said.StartsWith("let ")) said = said[4..].TrimStart();

        var end = 0;

        while (end < said.Length && (char.IsAsciiLetterOrDigit(said[end]) || said[end] == '_')) end++;

        if (end == 0) return null;

        var name = said[..end].ToString();
        var rest = said[end..].TrimStart();

        // A name on its own is only a module's when something is being said
        // about it: bound, or one of its knobs turned, or a wire run backwards
        // into it. A bare name at the head of a pipeline is being read.
        return rest.StartsWith('=') || rest.StartsWith('.') ? name : null;
    }

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

        if (shown) source.Focus();
        else editor.Focus();

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

        printed = PatchPrinter.Print(editor.Patch);
        source.Source = printed;
        source.Clear();
        source.Notice = Reading();
    }

    /// <summary>
    /// What a printing has to say for itself: where it came from, what it left
    /// behind, and what applying it would do.
    /// </summary>
    /// <remarks>
    /// The boxes come through now, so what is left to warn about is where the
    /// modules sit — a printing is laid out afresh on the way back in, and a
    /// canvas somebody arranged by hand does not survive being applied.
    /// </remarks>
    private static string Reading() =>
        "Printed from the canvas. The patch on the canvas is still the document — applying this "
        + "makes the text the document instead, and lays the modules out afresh.";

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
        }

        RefreshOwnership();

        // Every knob as the text now has it. Anything that differs from this
        // afterwards was turned somewhere else and has to go back in.
        Remember();

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

        source.Source = text;
        source.Clear();

        RefreshOwnership();
        Remember();
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

        written.Clear();

        source.Source = string.Empty;
        source.Clear();

        RefreshOwnership();

        if (showingCode) ShowCode(false);
    }

    /// <summary>Marks the text as written, so closing stops asking about it.</summary>
    private void MarkSourceSaved() => sourceOnDisk = source.Source;

    /// <summary>
    /// Takes every knob as the text now has it, which is what a later change is
    /// measured against.
    /// </summary>
    private void Remember()
    {
        written.Clear();

        foreach (var node in editor.Patch.Nodes)
            for (var port = 0; port < node.InputValues.Length; port++)
                written[(node.Id, port)] = node.InputValues[port];
    }

    /// <summary>
    /// Writes a knob turned in the inspector back into the text that owns the
    /// patch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what lets the panel be used at all while the text is the
    /// document. Without it a knob turned there is heard immediately and gone at
    /// the next apply, which is the worst of both — so the value goes into the
    /// source as the one statement the language has for saying it, and the next
    /// apply builds what is already being heard.
    /// </para>
    /// <para>
    /// Nothing is rebuilt. The patch already has the new value and the engine
    /// already has the patch; what this keeps in step is the document. Building
    /// here would replace the patch under the very control being dragged.
    /// </para>
    /// <para>
    /// A knob this cannot address — on a module the text never named, or one
    /// whose value is not a knob at all, like a tune or a file — is said out
    /// loud rather than dropped quietly. The person is about to lose it and
    /// should hear so while they can still do something else.
    /// </para>
    /// </remarks>
    private void WriteBack()
    {
        if (!sourceOwned || written.Count == 0) return;

        var lost = 0;

        foreach (var node in editor.Patch.Nodes)
        {
            if (NodeCatalog.Get(node.TypeId) is not { } def) continue;

            for (var port = 0; port < node.InputValues.Length && port < def.Inputs.Count; port++)
            {
                var value = node.InputValues[port];

                if (written.TryGetValue((node.Id, port), out var was) && Same(was, value)) continue;

                written[(node.Id, port)] = value;

                if (string.IsNullOrEmpty(node.Name))
                {
                    lost++;
                    continue;
                }

                source.Set(
                    node.Name,
                    def.Inputs[port].Name.Replace(' ', '_'),
                    PatchPrinter.Knob(value, def.Inputs[port].Display));
            }
        }

        if (lost > 0)
            Report($"{lost} of those cannot be written into the text — the module it is on has no "
                + "name there, so the value goes at the next apply.");
    }

    private static bool Same(float a, float b) => Math.Abs(a - b) < 1e-7f;

    /// <summary>
    /// Puts every control that edits the patch in step with who owns it.
    /// </summary>
    /// <remarks>
    /// The canvas keeps everything that looks — selecting, panning, framing,
    /// copying — and loses everything that changes, so it is still how somebody
    /// reads a source-built patch and picks the module the inspector is about.
    /// The inspector itself goes dim rather than away: a knob turned there would
    /// be wiped by the next evaluation, and a value nobody can read is worse
    /// than one nobody can turn.
    /// <para>
    /// Undo and redo are deliberately left alone. On a source-owned patch the
    /// history is a history of evaluations, and taking one back is exactly what
    /// somebody wants after applying something that turned out worse.
    /// </para>
    /// </remarks>
    private void RefreshOwnership()
    {
        editor.Locked = sourceOwned;

        // The inspector stays live even where the text owns the patch, because a
        // knob turned there is written back into it — see WriteBack. It is the
        // one thing on a locked canvas somebody can still change, and the reason
        // is that turning a knob while a patch plays is the whole point of
        // having it in front of you.
        inspector.IsEnabled = true;

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
                ? "The text is the document, and a knob turned here is written into it."
                : null);
    }
}
