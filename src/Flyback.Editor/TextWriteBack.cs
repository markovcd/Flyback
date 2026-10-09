using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Editor.Canvas;
using Flyback.Editor.Controls;
using Flyback.Editor.Notices;
using Flyback.Engine.Language;

namespace Flyback.Editor;

/// <summary>
/// Writes what the panel changes into the text, each value where the text already
/// says it (ADR-0148).
/// </summary>
/// <remarks>
/// What lets the panel be used at all while the text is the document: without it a
/// knob turned there is heard at once and gone at the next apply. Nothing is
/// rebuilt — the patch already has the value — since building here would replace
/// the patch under the control being dragged.
/// </remarks>
internal sealed class TextWriteBack : IReactTo<InputTurned>, IReactTo<InputLetGo>, IReactTo<TextForgotten>
{
    private readonly Document document;
    private readonly CaretFollow caret;
    private readonly NodeEditor editor;
    private readonly SourceView source;
    private readonly ReportLine report;

    public TextWriteBack(Document document, CaretFollow caret, NodeEditor editor, SourceView source, ReportLine report)
    {
        this.document = document;
        this.caret = caret;
        this.editor = editor;
        this.source = source;
        this.report = report;
    }

    public Task On(InputTurned notice)
    {
        Turned(notice.Pick.Node, notice.Pick.Port);
        return Task.CompletedTask;
    }

    /// <summary>Before the panel is rebuilt, so it reads the value already written back.</summary>
    int IReactTo<InputLetGo>.Priority => -10;

    public Task On(InputLetGo notice)
    {
        HandCameOff();
        return Task.CompletedTask;
    }

    public Task On(TextForgotten notice)
    {
        Forget();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Knobs turned in the panel since the hand last came off one.
    /// </summary>
    /// <remarks>
    /// A drag is one gesture and should be one edit: writing per frame would put a
    /// hundred things on the undo stack. Every frame still reaches the engine, and
    /// only the document waits.
    /// </remarks>
    private readonly HashSet<(Guid Node, int Port)> turned = [];

    /// <summary>
    /// What a module carries that is not a knob, changed since the same moment —
    /// a plugin's field by its key, and the tune, scale or file a module carries
    /// as the one thing it has no key for.
    /// </summary>
    private readonly HashSet<(Guid Node, string? Key)> restated = [];

    /// <summary>
    /// Panel knobs turned by hand since the same moment. One turned by a controller
    /// is left out: it would rewrite its line on every message it sent.
    /// </summary>
    private readonly HashSet<Guid> dialed = [];

    /// <summary>
    /// The word each knob added from the panel was written under, since the id
    /// the canvas gave it is not the one the text would give it until it is applied.
    /// </summary>
    private readonly Dictionary<Guid, string> given = [];

    /// <summary>
    /// Whether something about the whole patch rather than a module has changed
    /// since the same moment.
    /// </summary>
    private bool relaid;

    /// <summary>
    /// Whether this write-back met a value the printing had nowhere to put in
    /// place — a formula printed as the sum it is has no argument to write into.
    /// </summary>
    private bool reprint;

    /// <summary>
    /// The hand has come off whatever it was holding in the panel: the gesture is
    /// over, and what it changed goes into the text.
    /// </summary>
    /// <remarks>
    /// The canvas files an edit under the control it came from and every drag of
    /// one slider is that same name, so this is the only thing that can tell the
    /// history one drag from the next.
    /// </remarks>
    public void HandCameOff()
    {
        editor.History.GestureEnded();
        WriteBack();
    }

    /// <summary>Notes a knob the panel has just turned, for the next write-back.</summary>
    public void Turned(Guid node, int port) => turned.Add((node, port));

    /// <summary>The hand has come off a panel knob: where it rests goes into its <c>panel</c> line.</summary>
    public void LetGoOfKnob(Guid control)
    {
        dialed.Add(control);
        HandCameOff();
    }

    /// <summary>
    /// Notes something a module carries that the panel has just changed.
    /// </summary>
    /// <param name="key">
    /// A plugin field's key, or null for the one thing a module carries that has
    /// no key — its tune, its scale or the file it names.
    /// </param>
    public void Restated(Guid node, string? key = null) => restated.Add((node, key));

    /// <summary>
    /// Notes that something about the whole patch rather than a module has changed,
    /// the keyboard's layout or the length, for the next write-back.
    /// </summary>
    public void Relaid() => relaid = true;

    /// <summary>
    /// What a module carries has been edited: heard now, and written into the
    /// text when the hand comes off it.
    /// </summary>
    public void Edited(NodeInstance node, string? because = null)
    {
        Restated(node.Id);
        editor.History.Record(because);
    }

    /// <summary>
    /// Writes the knobs turned since the last gesture into the text.
    /// </summary>
    /// <remarks>
    /// The map is asked again for each one, because the first edit moves
    /// everything after it along.
    /// </remarks>
    private void WriteBack()
    {
        if ((turned.Count == 0 && restated.Count == 0 && dialed.Count == 0 && !relaid) || caret.Held || document.Stepping) return;

        var knobs = turned.ToArray();
        var kept = restated.ToArray();
        var panel = dialed.ToArray();
        var lost = 0;

        // Asked before anything is written, which is what makes the text differ
        // from its printing when it is one.
        var reading = document.IsPrinting;

        var keyboard = relaid;

        reprint = false;

        turned.Clear();
        restated.Clear();
        dialed.Clear();
        relaid = false;

        // The numbers written here and the turning of the knobs behind them are
        // one thing somebody did, and come back in one press. Without this the
        // text would go back and the sound would not, which is the two of them
        // saying different things about one patch.
        using (source.Together())
        {
            using (caret.Hold())
            {
                foreach (var (id, port) in knobs) Write(id, port, ref lost);
                foreach (var (id, key) in kept) Carry(id, key, ref lost);
                foreach (var id in panel) Rest(id);

                if (keyboard) Lay();
            }

            document.RememberPatchSteps();
        }

        // A printing is a reading and not a document, so what has just been written
        // into it is not an edit anybody made: an undo falls through to the canvas
        // and makes the reading afresh. Left on this stack, one press would put the
        // old number back over a patch still playing the new one. Said after the
        // group is closed, since emptying a stack closes what is open on it.
        //
        // Only a printing, though. One that has been typed into is somebody's
        // work that nothing here could write to, and the steps on its stack are
        // the typing — theirs to take back in the view they did it in.
        if (reading) source.ForgetSteps();

        // A printing that could not take a value in place is printed again,
        // which is what a printing is for.
        if (reprint) document.Reprint();

        if (lost > 0)
            report.Say($"{lost} value(s) could not be written into the text — "
                + "the code says them in a form this cannot change in place.");
    }

    /// <summary>
    /// The panel's knobs have changed in a way their <c>panel</c> lines say: moved,
    /// renamed, bound to a controller or let go of one, added or taken away.
    /// </summary>
    /// <remarks>
    /// A printing is printed again. Text somebody wrote has its <c>panel</c> lines
    /// rewritten where they stand, and nothing else touched.
    /// </remarks>
    public void PanelEdited()
    {
        if (!document.Owned)
        {
            document.Reprint();
            return;
        }

        var words = document.Map.PanelWords;
        var taken = new HashSet<string>(words.Values.Concat(given.Values), StringComparer.Ordinal);
        var lines = new List<string>();

        foreach (var control in editor.History.Patch.Controls ?? [])
        {
            if (!words.TryGetValue(control.Id, out var word) && !given.TryGetValue(control.Id, out word))
            {
                var wanted = PatchPrinter.PanelWord(control.Name);

                word = wanted;
                for (var n = 2; !taken.Add(word); n++) word = wanted + n.ToString(System.Globalization.CultureInfo.InvariantCulture);

                given[control.Id] = word;
            }

            lines.Add(PatchPrinter.PanelLine(control, word));
        }

        if (document.Map.Panel(lines) is not { } change) return;

        using (source.Together())
        {
            using (caret.Hold())
                if (source.Apply(change)) document.Rewritten();

            document.RememberPatchSteps();
        }
    }

    /// <summary>Puts where a panel knob rests into its <c>panel</c> line, where the text has one.</summary>
    private void Rest(Guid id)
    {
        if (editor.History.Patch.Control(id) is not { } control) return;

        var change = document.Map.Knob(id, PatchPrinter.PanelKnob, PatchPrinter.Knob(control.Value, PortDisplay.Number));

        if (change is { } edit && source.Apply(edit)) document.Rewritten();
    }

    /// <summary>Puts one knob into the text, or counts it as one that could not go.</summary>
    private void Write(Guid id, int port, ref int lost)
    {
        if (editor.History.Patch.Find(id) is not { } node
            || NodeCatalog.Get(node.TypeId) is not { } def
            || port >= def.Inputs.Count
            || port >= node.InputValues.Length)
        {
            return;
        }

        var spec = def.Inputs[port];
        var value = PatchPrinter.Knob(node.InputValues[port], spec.Display);

        Put(document.Map.Knob(id, spec.Name.Replace(' ', '_'), value), id, ref lost);
    }

    /// <summary>
    /// Puts something a module carries that is not a knob back into the text: a
    /// plugin's field as a named argument, a tune or a scale as the block after the
    /// call, a file as the one string a call carries. Only what changed, so
    /// touching one field does not restate the others.
    /// </summary>
    private void Carry(Guid id, string? key, ref int lost)
    {
        if (editor.History.Patch.Find(id) is not { } node || NodeCatalog.Get(node.TypeId) is not { } def) return;

        if (key is not null)
        {
            foreach (var extra in def.Extras)
                foreach (var field in extra.Fields)
                {
                    if (field.Key != key || PatchPrinter.Field(node, extra, field) is not { } value) continue;

                    var change = document.Map.Knob(id, field.Key, value);

                    if (change is null && document.IsPrinting)
                    {
                        reprint = true;
                        continue;
                    }

                    Put(change, id, ref lost);
                }

            return;
        }

        // A block that says nothing rather than no block at all: a tune emptied
        // in the panel has to empty in the text too, and a call with nothing
        // after it is a call that leaves whatever is there alone.
        if (def.Extra<StepsExtra>() is not null || def.Extra<ScaleExtra>() is not null || def.Extra<ArrangementExtra>() is not null)
        {
            Put(document.Map.Carried(id, PatchPrinter.Carried(node, def) ?? "[ ]"), id, ref lost);
            return;
        }

        if (PatchPrinter.Held(node, def) is { Length: > 0 } path) Put(document.Map.File(id, path), id, ref lost);
    }

    /// <summary>
    /// Puts what belongs to the whole patch into the text: its description, author,
    /// tags, length and the keyboard's layout, each as the one line that says it.
    /// </summary>
    private void Lay()
    {
        var patch = editor.History.Patch;

        Put(document.Map.Description(PatchPrinter.Description(patch.Description)));
        Put(document.Map.Author(PatchPrinter.Author(patch.Author)));
        Put(document.Map.Tags(PatchPrinter.Tags(patch.Tags)));
        Put(document.Map.Length(PatchPrinter.Length(patch.Length)));
        Put(document.Map.Keyboard(PatchPrinter.Keyboard(patch.Keyboard)));

        void Put(Change? change)
        {
            if (change is { } edit && source.Apply(edit)) document.Rewritten();
        }
    }

    /// <summary>
    /// Makes one edit, or counts it as one the text had nowhere to take. Nothing
    /// written where something should have been is said out loud: the value is
    /// about to be lost, and whoever changed it can still do something about it.
    /// </summary>
    private void Put(Change? change, Guid id, ref int lost)
    {
        if (change is not { } edit)
        {
            if (document.Map.Where(id) is not null) lost++;
            return;
        }

        if (source.Apply(edit)) document.Rewritten();
    }

    /// <summary>
    /// Drops what was waiting to be written, for a text that now means something
    /// else: a knob left waiting would be written into somebody else's file.
    /// </summary>
    private void Forget()
    {
        turned.Clear();
        restated.Clear();
        dialed.Clear();
        given.Clear();
        relaid = false;
    }
}
