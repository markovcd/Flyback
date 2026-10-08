using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using static Flyback.Plugins.Assist.ToolArguments;

namespace Flyback.Plugins.Assist;

/// <summary>
/// The tools that place a module or change what one holds: its knobs, its notes,
/// its scale, its parts, its file and a plugin's fields.
/// </summary>
/// <remarks>
/// Ports are named by name rather than by index: a model asked to count a
/// Sequencer's twenty-one inputs will get it wrong.
/// </remarks>
internal sealed class ModuleEdits(WorkingPatch bench, PatchReports reports, CatalogReference catalog)
{
    public ToolOutcome AddModule(JsonElement arguments)
    {
        if (!Text(arguments, "type_id", out var typeId))
            return ToolOutcome.Refused("'type_id' is required and must be a string.");

        if (bench.Modules.Get(typeId) is not { } def)
            return ToolOutcome.Refused($"there is no module with type id '{typeId}'. {catalog.Nearest(typeId)}");

        // Every patch already has its Output and cannot have a second. The
        // second sink is the mistake that hides itself — compilation roots at
        // the first one and never reaches the other, so the patch compiles, says
        // nothing, and half of what was wired up is simply not there.
        if (!bench.Patch.CanAdd(typeId) && bench.Patch.FirstOf(typeId) is { } sink)
            return ToolOutcome.Refused(
                $"every patch already has its {def.Name}, as {bench.Handle(sink)}, and cannot have a second. "
                + "Wire into that one — 'color' for the picture, 'left' for the sound.");

        string handle;

        if (Text(arguments, "handle", out var wanted))
        {
            if (bench.ByHandle.ContainsKey(wanted))
                return ToolOutcome.Refused($"'{wanted}' is already the handle of another module.");

            // describe_patch prints a handle as the name itself only where the language can read it back.
            if (!PatchPrinter.Usable(wanted))
                return ToolOutcome.Refused(
                    $"'{wanted}' cannot be a handle: use letters, digits and _, starting with a letter, "
                    + "and not a note name or a word the language keeps (out, in, let, x, y, t…).");

            handle = wanted;
        }
        else
        {
            handle = bench.Available(typeId);
        }

        // A Maths module an Expression stands for arrives as the Expression, with
        // its knobs as they rest (ADR-0109), and the reply says so: what is wired
        // next goes into a and b.
        var standing = ExpressionFusion.Standing(def, bench.Modules, 0, 0);
        var node = standing ?? NodeInstance.Create(def, 0, 0);

        if (standing is not null) def = bench.Modules.Require(NodeCatalog.ExpressionTypeId);

        // Every knob is checked before the module is placed, so a refusal leaves
        // nothing behind for a retry to trip over.
        List<(int Port, float Value)> settings = [];

        if (arguments.TryGetProperty("knobs", out var knobs) && Knobs(handle, def, knobs, settings) is { } refused)
            return ToolOutcome.Refused($"{refused} So {handle} was not added; add it again without that knob.");

        bench.Place(node, handle);
        bench.Edits++;

        Set(node, def, settings);

        var report = new StringBuilder(standing is null
            ? $"added {handle} ({typeId})."
            : $"added {handle}, an Expression for {typeId}: {NodeCatalog.FormulaOf(node)}.");

        report.Append(' ').Append(CatalogReference.Sockets(def)).Append(' ').Append(reports.Issues());
        return ToolOutcome.Fine(report.ToString());
    }

    public ToolOutcome SetKnobs(JsonElement arguments)
    {
        if (!bench.Node(arguments, "handle", out var node, out var def, out var refusal))
            return ToolOutcome.Refused(refusal);

        if (!arguments.TryGetProperty("knobs", out var knobs))
            return ToolOutcome.Refused("'knobs' is required: a list of {port, value}.");

        List<(int Port, float Value)> settings = [];

        if (Knobs(bench.Handle(node), def, knobs, settings) is { } bad) return ToolOutcome.Refused(bad);

        Set(node, def, settings);

        return ToolOutcome.Fine($"set. {reports.Issues()}");
    }

    /// <summary>
    /// Replaces a sequencer's tune outright rather than editing it a note at a
    /// time. A model rewriting eight notes in one call cannot get them into the
    /// wrong order, and eight calls that each have to land correctly is eight
    /// chances to end up with a tune nobody asked for.
    /// </summary>
    public ToolOutcome SetSteps(JsonElement arguments)
    {
        if (!bench.Node(arguments, "handle", out var node, out var def, out var refusal))
            return ToolOutcome.Refused(refusal);

        if (def.Extra<StepsExtra>() is not { } carries)
            return ToolOutcome.Refused(
                $"{bench.Handle(node)} is a {def.Name}, which has no notes. Only the sequencers do.");

        if (!arguments.TryGetProperty("notes", out var given) || given.ValueKind != JsonValueKind.Array)
            return ToolOutcome.Refused("'notes' is required: a list of {value, length, volume}.");

        if (given.GetArrayLength() > NodeCatalog.MaxSteps)
            return ToolOutcome.Refused(
                $"a sequence holds at most {NodeCatalog.MaxSteps} notes, and that is "
                + $"{given.GetArrayLength()}.");

        var notes = new List<Step>();

        foreach (var note in given.EnumerateArray())
        {
            if (note.ValueKind != JsonValueKind.Object)
                return ToolOutcome.Refused("every note has to be an object of {value, length, volume}.");

            if (!Real(note, "value", out var value))
                return ToolOutcome.Refused("every note needs a 'value'.");

            // Both optional, because the ordinary note is one step long and
            // fully open, and saying so on every note of a tune is noise.
            notes.Add(new Step(
                value,
                Real(note, "length", out var length) ? length : 1f,
                Real(note, "volume", out var volume) ? volume : 1f).Sane());
        }

        StepsExtra.Set(node, notes);
        bench.Edits++;

        return ToolOutcome.Fine($"set {notes.Count} notes on {bench.Handle(node)}. {carries.Report(node)} {reports.Issues()}");
    }

    /// <summary>
    /// Every part of an Arrangement at once, each written as the text language writes
    /// one, for the reason a tune is sent whole.
    /// </summary>
    public ToolOutcome SetArrangement(JsonElement arguments)
    {
        if (!bench.Node(arguments, "handle", out var node, out var def, out var refusal))
            return ToolOutcome.Refused(refusal);

        if (def.Extra<ArrangementExtra>() is not { } carries)
            return ToolOutcome.Refused($"{bench.Handle(node)} is a {def.Name}, which has no parts. Only an Arrangement does.");

        if (!arguments.TryGetProperty("parts", out var given) || given.ValueKind != JsonValueKind.Array)
            return ToolOutcome.Refused("'parts' is required: a list of strings, one a part, like \"0 1 >1 0.5\".");

        if (given.GetArrayLength() > NodeCatalog.MaxParts)
            return ToolOutcome.Refused(
                $"an Arrangement holds at most {NodeCatalog.MaxParts} parts, and that is {given.GetArrayLength()}.");

        var parts = new List<List<PartLevel>>();

        foreach (var part in given.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.String || part.GetString() is not { } written || written.Contains('|'))
                return ToolOutcome.Refused("every part is one string of levels, with no '|'.");

            var issues = new List<LanguageIssue>();
            var read = ArrangementNotation.Read(written, 1, 1, issues);

            if (issues.Count > 0) return ToolOutcome.Refused($"part {parts.Count + 1}: {issues[0].Message}");

            parts.Add(read.Count == 0 ? [] : read[0]);
        }

        ArrangementExtra.Set(node, ArrangementExtra.Tidy(parts));
        bench.Edits++;

        return ToolOutcome.Fine($"set {parts.Count} parts on {bench.Handle(node)}. {carries.Report(node)} {reports.Issues()}");
    }

    /// <summary>
    /// Replaces a quantiser's scale outright, for the reason a tune is replaced
    /// outright: a set sent whole cannot come out in the wrong order or half
    /// applied, and twelve calls to switch twelve notes is twelve chances to end
    /// up with a scale nobody asked for.
    /// </summary>
    public ToolOutcome SetScale(JsonElement arguments)
    {
        if (!bench.Node(arguments, "handle", out var node, out var def, out var refusal))
            return ToolOutcome.Refused(refusal);

        if (def.Extra<ScaleExtra>() is not { } carries)
            return ToolOutcome.Refused(
                $"{bench.Handle(node)} is a {def.Name}, which has no scale. Only the Quantiser has one.");

        if (!arguments.TryGetProperty("notes", out var given) || given.ValueKind != JsonValueKind.Array)
            return ToolOutcome.Refused(
                "'notes' is required: a list of pitch classes, 0 to 11, where 0 is C and 9 is A. "
                + "Send the whole scale — this replaces what was there.");

        var classes = new List<int>();

        foreach (var note in given.EnumerateArray())
        {
            if (note.ValueKind != JsonValueKind.Number || !note.TryGetInt32(out var pitchClass))
                return ToolOutcome.Refused("every note has to be a whole number from 0 to 11.");

            if (pitchClass is < 0 or >= Pitch.Classes)
                return ToolOutcome.Refused(
                    $"{pitchClass} is not a pitch class. They run 0 to 11, C to B, and a scale "
                    + "names every octave of a note at once rather than one of them — so a C is "
                    + "0 whichever octave you were thinking of.");

            classes.Add(pitchClass);
        }

        ScaleExtra.Set(node, Pitch.Scale(classes));
        bench.Edits++;

        return ToolOutcome.Fine($"set the scale on {bench.Handle(node)}. {carries.Report(node)} {reports.Issues()}");
    }

    /// <summary>
    /// Points a player at a sound file. A path is neither a knob nor a wire, so this
    /// is the only way to set one.
    /// </summary>
    /// <remarks>
    /// The answer carries what the compiler makes of it rather than taking the path
    /// on trust: a file that is not there is the one mistake this tool can make, and
    /// an assistant would otherwise go on building around a silent player.
    /// </remarks>
    public ToolOutcome SetSample(JsonElement arguments)
    {
        if (!bench.Node(arguments, "handle", out var node, out var def, out var refusal))
            return ToolOutcome.Refused(refusal);

        if (def.Extras.OfType<FileExtra>().FirstOrDefault(file => !file.Kind.Picture) is not { } file)
        {
            return ToolOutcome.Refused(
                $"{bench.Handle(node)} is a {def.Name}, which reads no file. Only the Sample, MIDI File and Path modules do.");
        }

        if (!Text(arguments, "path", out var path))
            return ToolOutcome.Refused($"'path' is required: where the file is ({file.Kind.Described}).");

        file.Point(node, path);
        bench.Edits++;

        return ToolOutcome.Fine($"{bench.Handle(node)} now reads {path}. {reports.Issues()}");
    }

    /// <summary>
    /// Points an Image module at a picture. <see cref="SetSample"/> for the other
    /// kind of file, written out again rather than shared with it for the reason
    /// the two libraries are two classes: what they have in common is four lines,
    /// and what they do not is every sentence a person reads.
    /// </summary>
    public ToolOutcome SetPicture(JsonElement arguments)
    {
        if (!bench.Node(arguments, "handle", out var node, out var def, out var refusal))
            return ToolOutcome.Refused(refusal);

        if (def.Extra<PictureExtra>() is null)
        {
            return ToolOutcome.Refused(
                $"{bench.Handle(node)} is a {def.Name}, which shows no picture. Only the Image module does.");
        }

        if (!Text(arguments, "path", out var path))
            return ToolOutcome.Refused("'path' is required: where the PNG file is.");

        PictureExtra.Set(node, path);
        bench.Edits++;

        return ToolOutcome.Fine($"{bench.Handle(node)} now shows {path}. {reports.Issues()}");
    }

    /// <summary>
    /// Sets one field of an extra a plugin defined.
    /// </summary>
    /// <remarks>
    /// One tool for every kind a plugin will ever add, which is the return on
    /// declaring a schema rather than shipping a control (ADR-0055). A field at a
    /// time rather than the whole object, unlike <c>set_steps</c>: a tune is a list
    /// whose order is the point, where these are named values that do not depend on
    /// each other.
    /// </remarks>
    public ToolOutcome SetExtra(JsonElement arguments)
    {
        if (!bench.Node(arguments, "handle", out var node, out var def, out var refusal))
            return ToolOutcome.Refused(refusal);

        if (!Text(arguments, "extra", out var key))
            return ToolOutcome.Refused("'extra' is required: which of the module's extras to set.");

        if (def.Extras.FirstOrDefault(e => e.Key == key) is not { } extra)
        {
            var carries = def.Extras.Count == 0
                ? "it carries none"
                : $"it carries {string.Join(", ", def.Extras.Select(e => e.Key))}";

            return ToolOutcome.Refused($"{bench.Handle(node)} has no '{key}' — {carries}.");
        }

        // A kind with no declared fields is one this tool cannot write, and every
        // one of them is an engine kind with a tool of its own. Named from the
        // same place the module listing names it, so the refusal cannot send a
        // model somewhere the listing did not.
        if (extra.Fields.Count == 0)
        {
            return ToolOutcome.Refused(
                $"'{key}' on {bench.Handle(node)} is not set this way — use "
                + $"{Vocabulary.ToolFor(extra)}.");
        }

        if (!Text(arguments, "field", out var name))
            return ToolOutcome.Refused("'field' is required: which of the extra's values to set.");

        if (extra.Fields.FirstOrDefault(f => f.Key == name) is not { } field)
        {
            return ToolOutcome.Refused(
                $"'{key}' has no '{name}' — it holds "
                + $"{string.Join(", ", extra.Fields.Select(f => f.Key))}.");
        }

        if (!arguments.TryGetProperty("value", out var given))
            return ToolOutcome.Refused("'value' is required.");

        // Checked against the shape the field declared rather than taken on
        // trust, so that a model sending a string where a number belongs is told
        // so here instead of having it quietly become the default.
        JsonNode value;

        switch (field)
        {
            case ExtraField.Number when given.ValueKind == JsonValueKind.Number:
                value = JsonValue.Create(given.GetSingle());
                break;

            case ExtraField.Number number:
                return ToolOutcome.Refused(
                    $"'{name}' is a number from {number.Spec.Min} to {number.Spec.Max}.");

            case ExtraField.Toggle when given.ValueKind is JsonValueKind.True or JsonValueKind.False:
                value = JsonValue.Create(given.ValueKind == JsonValueKind.True);
                break;

            case ExtraField.Toggle:
                return ToolOutcome.Refused($"'{name}' is a switch: true or false.");

            // The id rather than the name, and the list of ids in the refusal —
            // what a device is called is for a person to read and is not stable
            // enough to be written into a patch.
            case ExtraField.Choice when given.ValueKind == JsonValueKind.String:
                value = JsonValue.Create(given.GetString() ?? string.Empty);
                break;

            case ExtraField.Choice choice:
                return ToolOutcome.Refused(
                    $"'{name}' is one of {Offered(choice)}, as a string.");

            case ExtraField.Text when given.ValueKind == JsonValueKind.String:
                value = JsonValue.Create(given.GetString() ?? string.Empty);
                break;

            case ExtraField.Text text:
                return ToolOutcome.Refused(
                    text.Multiline
                        ? $"'{name}' is text, as a string, with a line break (\\n) between lines."
                        : $"'{name}' is text, as a string.");

            default:
                return ToolOutcome.Refused(
                    $"'{name}' is a kind of value this build cannot set. It was added by a "
                    + "newer one.");
        }

        // The new value through the field's own tidying, not just the ones that
        // were already there: a number outside the declared range is held to it
        // as it is stored, so what a later listing reads back is what the module
        // will actually compile with.
        var held = extra.Stored(node.StateOf(key));
        held[name] = field.Sane(value);
        node.SetState(key, held);

        bench.Edits++;

        return ToolOutcome.Fine($"set {key}.{name} on {bench.Handle(node)}. {extra.Report(node)} {reports.Issues()}");
    }

    /// <summary>
    /// What a choice currently offers, for saying so in a refusal. Empty is a
    /// real answer rather than an omission: a picker for something that is not
    /// plugged in has nothing in it, and saying "one of nothing" is more use than
    /// an empty list would be.
    /// </summary>
    private static string Offered(ExtraField.Choice choice) =>
        choice.Options.Count == 0
            ? "nothing — there is none of that here at the moment"
            : string.Join(", ", choice.Options.Select(option => $"'{option.Id}'"));

    /// <summary>Reads a knob list into <paramref name="settings"/>, or says why it could not. Null means every entry reads.</summary>
    private string? Knobs(string handle, NodeDef def, JsonElement knobs, List<(int Port, float Value)> settings)
    {
        if (knobs.ValueKind != JsonValueKind.Array) return "'knobs' must be a list of {port, value}.";

        foreach (var knob in knobs.EnumerateArray())
        {
            if (knob.ValueKind != JsonValueKind.Object) return "every entry in 'knobs' must be {port, value}.";

            if (!Text(knob, "port", out var portName))
                return "every entry in 'knobs' needs a 'port' naming an input.";

            if (!knob.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Number)
                return $"'{portName}' needs a numeric 'value'.";

            if (!Port(def.Inputs, portName, out var port))
                return $"{handle} has no input called '{portName}'. Its inputs are: {CatalogReference.List(def.Inputs)}.";

            // A normalled socket has no knob to turn: it compiles to the module
            // it is normalled to, and the value stored against it is never read.
            // Refused rather than stored quietly, because storing it would look
            // like it worked and the patch would not change — which is the kind
            // of thing an assistant can spend a whole run failing to notice.
            if (bench.Modules.Normalled(def.Inputs[port]) is { } source)
            {
                return $"{handle}'s '{portName}' is normalled to {source} and has no knob: "
                    + "it is already reading that, with no wire, and a value set here would not be "
                    + $"read. Patch something into '{portName}' to drive it with that instead — a "
                    + "Value module if what you want there really is a constant.";
            }

            if (value.GetDouble() is var number && !float.IsFinite((float)number))
                return $"{handle}'s '{portName}' needs a 'value' a float can hold, and {number} is not one.";

            settings.Add((port, (float)number));
        }

        return null;
    }

    private void Set(NodeInstance node, NodeDef def, List<(int Port, float Value)> settings)
    {
        if (settings.Count == 0) return;

        Grow(node, def);

        foreach (var (port, value) in settings) node.InputValues[port] = value;

        bench.Edits += settings.Count;
    }

    /// <summary>
    /// Widens a node's stored values to cover every input it has. A patch saved
    /// before a module gained one is short (ADR-0020), and an assistant should
    /// not have to know that.
    /// </summary>
    private static void Grow(NodeInstance node, NodeDef def)
    {
        if (node.InputValues.Length >= def.Inputs.Count) return;

        var widened = new float[def.Inputs.Count];

        for (var i = 0; i < widened.Length; i++)
            widened[i] = i < node.InputValues.Length ? node.InputValues[i] : def.Inputs[i].Default;

        node.InputValues = widened;
    }
}
