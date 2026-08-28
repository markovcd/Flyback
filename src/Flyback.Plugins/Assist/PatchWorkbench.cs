using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.Plugins.Assist;

/// <summary>
/// A patch an assistant may edit, and the vocabulary it edits it in. Everything
/// here is provider-neutral: naming a module, wiring a port, reading the
/// compiler's complaints and looking at a frame are knowledge of the graph, not
/// of any particular model's API.
/// </summary>
/// <remarks>
/// <para>
/// The working patch is the workbench's own copy. Whatever was open in the
/// editor is never touched, which is what makes accepting a proposal a single
/// assignment and rejecting one free.
/// </para>
/// <para>
/// Nodes are named by short handles rather than by <see cref="Guid"/>, and
/// ports by name rather than by index. Both are storage detail: a model asked
/// to invent twenty consistent guids, or to count a Sequencer's twenty-one
/// inputs to find the right index, will get it wrong — and a patch is small
/// enough that handles never run out of room.
/// </para>
/// <para>
/// The catalogue arrives explicitly and <see cref="NodeCatalog.Current"/> is
/// never read, the rule ADR-0026 set for the compiler and for file I/O. It is
/// also what lets the tests run against <see cref="NodeCatalog.BuiltIn"/>
/// rather than against whatever happens to be installed.
/// </para>
/// </remarks>
public sealed class PatchWorkbench
{
    private readonly ModuleCatalog modules;
    private readonly ISampleLibrary? samples;

    /// <summary>Where an Image module.s file is looked up, and null where nothing can.</summary>
    private readonly IImageLibrary? pictures;
    private readonly WorkbenchLimits limits;
    private readonly string startingPoint;
    private readonly Dictionary<string, NodeInstance> byHandle = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, string> handleOf = [];

    private Patch working = new();
    private string? proposal;

    /// <param name="vision">Whether the model may be shown a frame, which offers <c>render</c>.</param>
    /// <param name="hearing">
    /// Whether the model may be played the sound, which offers <c>listen</c>.
    /// Off by default, unlike <paramref name="vision"/>: every model worth
    /// pointing this at can see, and only a few can hear.
    /// </param>
    /// <param name="samples">
    /// Where a Sample module's file is looked up, and null where nothing can
    /// look one up — which makes every player silent and every path a complaint.
    /// </param>
    public PatchWorkbench(
        ModuleCatalog modules,
        Patch startingPoint,
        bool vision = true,
        bool hearing = false,
        WorkbenchLimits? limits = null,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null)
    {
        this.modules = modules;
        this.samples = samples;
        this.pictures = pictures;
        this.limits = limits ?? new WorkbenchLimits();

        // Kept as text so Reset cannot hand back something an earlier edit
        // reached into, and so the starting point is provably reloadable.
        this.startingPoint = PatchIo.ToJson(startingPoint, modules);

        Adopt(PatchIo.Read(this.startingPoint, modules).Patch);

        var prose = Handbook.Render(modules, prose: true, hearing);
        var large = prose.Length > Handbook.ProseBudget;

        Briefing = large ? Handbook.Render(modules, prose: false, hearing) : prose;
        Tools = BuildTools(vision, hearing, lookups: large);
    }

    /// <summary>The conventions and the catalogue, as a model should be told them.</summary>
    public string Briefing { get; }

    public IReadOnlyList<PatchTool> Tools { get; }

    public bool HasProposal => proposal is not null;

    /// <summary>
    /// Takes the proposal back down, leaving everything built so far in place.
    /// </summary>
    /// <remarks>
    /// What a conversation does after a proposal is carry on from it — "now make
    /// it slower" is the second thing anybody says — so the working patch stays
    /// exactly as it was and only the offer is withdrawn. An adapter calls this
    /// as a turn begins. One that did not would find a proposal already standing
    /// before the model had said anything, and would hand the same patch over
    /// again as this turn's answer.
    /// </remarks>
    public void Reopen() => proposal = null;

    public string ProposalSummary => proposal ?? string.Empty;

    public int Edits { get; private set; }

    public int ToolCalls { get; private set; }

    /// <summary>
    /// The working patch, laid out and deep-copied by writing it out and reading
    /// it back. The round trip is the copy, and it is also the last gate: what
    /// comes out is exactly what would have been saved, stamped with the plugins
    /// it needs, or it does not come out at all.
    /// </summary>
    public Patch Snapshot()
    {
        Arrange();
        return PatchIo.Read(PatchIo.ToJson(working, modules), modules).Patch;
    }

    // --- dispatch -----------------------------------------------------------

    /// <summary>
    /// Runs one tool call. Never throws, whatever it is handed — see
    /// <see cref="ToolOutcome"/> for why that is a rule rather than a courtesy.
    /// </summary>
    public async Task<ToolOutcome> InvokeAsync(string tool, JsonElement arguments, CancellationToken cancel)
    {
        if (ToolCalls >= limits.MaxToolCalls)
            return ToolOutcome.Refused(
                $"you have used all {limits.MaxToolCalls} tool calls for this run. "
                + "Finish with 'propose' if the patch is usable, or say what is left undone.");

        ToolCalls++;

        try
        {
            return tool switch
            {
                "describe_patch" => Fine(DescribePatch()),
                "add_module" => AddModule(arguments),
                "set_knobs" => SetKnobs(arguments),
                Vocabulary.SetSteps => SetSteps(arguments),
                Vocabulary.SetScale => SetScale(arguments),
                Vocabulary.SetSample => SetSample(arguments),
                Vocabulary.SetPicture => SetPicture(arguments),
                Vocabulary.SetExtra => SetExtra(arguments),
                "connect" => Connect(arguments),
                "disconnect" => Disconnect(arguments),
                "remove_module" => RemoveModule(arguments),
                "reset" => Reset(),
                "propose" => Propose(arguments),
                "render" => await RenderAsync(arguments, cancel).ConfigureAwait(false),
                "listen" => await ListenAsync(arguments, cancel).ConfigureAwait(false),
                "describe_module" => DescribeModule(arguments),
                "find_modules" => FindModules(arguments),
                _ => ToolOutcome.Refused($"there is no tool called '{tool}'."),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToolOutcome.Refused($"'{tool}' failed: {ex.Message}");
        }
    }

    // --- editing ------------------------------------------------------------

    private ToolOutcome AddModule(JsonElement arguments)
    {
        if (!Text(arguments, "type_id", out var typeId))
            return ToolOutcome.Refused("'type_id' is required and must be a string.");

        if (modules.Get(typeId) is not { } def)
            return ToolOutcome.Refused($"there is no module with type id '{typeId}'. {Nearest(typeId)}");

        if (working.Nodes.Count >= limits.MaxNodes)
            return ToolOutcome.Refused(
                $"this patch already has {limits.MaxNodes} modules, which is as many as a patch may have.");

        // Every patch already has its Output and cannot have a second. The
        // second sink is the mistake that hides itself — compilation roots at
        // the first one and never reaches the other, so the patch compiles, says
        // nothing, and half of what was wired up is simply not there.
        if (!working.CanAdd(typeId) && working.FirstOf(typeId) is { } sink)
            return ToolOutcome.Refused(
                $"every patch already has its {def.Name}, as {Handle(sink)}, and cannot have a second. "
                + "Wire into that one — 'color' for the picture, 'left' for the sound.");

        string handle;

        if (Text(arguments, "handle", out var wanted))
        {
            if (byHandle.ContainsKey(wanted))
                return ToolOutcome.Refused($"'{wanted}' is already the handle of another module.");

            handle = wanted;
        }
        else
        {
            handle = Available(typeId);
        }

        var node = NodeInstance.Create(def, 0, 0);

        working.Nodes.Add(node);
        byHandle[handle] = node;
        handleOf[node.Id] = handle;
        Edits++;

        var report = new StringBuilder($"added {handle} ({typeId}).");

        if (arguments.TryGetProperty("knobs", out var knobs) && Turn(node, def, knobs) is { } refused)
            return ToolOutcome.Refused(refused);

        report.Append(' ').Append(Sockets(def)).Append(' ').Append(Issues());
        return Fine(report.ToString());
    }

    private ToolOutcome SetKnobs(JsonElement arguments)
    {
        if (!Node(arguments, "handle", out var node, out var def, out var refusal))
            return ToolOutcome.Refused(refusal);

        if (!arguments.TryGetProperty("knobs", out var knobs))
            return ToolOutcome.Refused("'knobs' is required: a list of {port, value}.");

        if (Turn(node, def, knobs) is { } bad) return ToolOutcome.Refused(bad);

        return Fine($"set. {Sockets(def)} {Issues()}");
    }

    /// <summary>
    /// Replaces a sequencer's tune outright rather than editing it a note at a
    /// time. A model rewriting eight notes in one call cannot get them into the
    /// wrong order, and eight calls that each have to land correctly is eight
    /// chances to end up with a tune nobody asked for.
    /// </summary>
    private ToolOutcome SetSteps(JsonElement arguments)
    {
        if (!Node(arguments, "handle", out var node, out var def, out var refusal))
            return ToolOutcome.Refused(refusal);

        if (def.Extra<StepsExtra>() is not { } carries)
            return ToolOutcome.Refused(
                $"{Handle(node)} is a {def.Name}, which has no notes. Only the sequencers do.");

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
        Edits++;

        return Fine($"set {notes.Count} notes on {Handle(node)}. {carries.Report(node)} {Issues()}");
    }

    private static bool Real(JsonElement element, string name, out float value)
    {
        value = 0f;

        return element.TryGetProperty(name, out var found)
            && found.ValueKind == JsonValueKind.Number
            && found.TryGetSingle(out value);
    }

    /// <summary>
    /// Replaces a quantiser's scale outright, for the reason a tune is replaced
    /// outright: a set sent whole cannot come out in the wrong order or half
    /// applied, and twelve calls to switch twelve notes is twelve chances to end
    /// up with a scale nobody asked for.
    /// </summary>
    private ToolOutcome SetScale(JsonElement arguments)
    {
        if (!Node(arguments, "handle", out var node, out var def, out var refusal))
            return ToolOutcome.Refused(refusal);

        if (def.Extra<ScaleExtra>() is not { } carries)
            return ToolOutcome.Refused(
                $"{Handle(node)} is a {def.Name}, which has no scale. Only the Quantiser has one.");

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
        Edits++;

        return Fine($"set the scale on {Handle(node)}. {carries.Report(node)} {Issues()}");
    }

    /// <summary>
    /// Points a player at a sound file. A path is neither a knob nor a wire, so
    /// this is the only way to set one.
    /// </summary>
    /// <remarks>
    /// The answer carries what the compiler makes of it rather than taking the
    /// path on trust. A file that is not there is the one mistake this tool can
    /// make, and an assistant that will not find out until something else
    /// complains would go on building around a player that is silent.
    /// </remarks>
    private ToolOutcome SetSample(JsonElement arguments)
    {
        if (!Node(arguments, "handle", out var node, out var def, out var refusal))
            return ToolOutcome.Refused(refusal);

        if (def.Extra<SampleExtra>() is null)
        {
            return ToolOutcome.Refused(
                $"{Handle(node)} is a {def.Name}, which reads no file. Only the Sample module does.");
        }

        if (!Text(arguments, "path", out var path))
            return ToolOutcome.Refused("'path' is required: where the WAV file is.");

        SampleExtra.Set(node, path);
        Edits++;

        return Fine($"{Handle(node)} now reads {path}. {Issues()}");
    }

    /// <summary>
    /// Points an Image module at a picture. <see cref="SetSample"/> for the other
    /// kind of file, written out again rather than shared with it for the reason
    /// the two libraries are two classes: what they have in common is four lines,
    /// and what they do not is every sentence a person reads.
    /// </summary>
    private ToolOutcome SetPicture(JsonElement arguments)
    {
        if (!Node(arguments, "handle", out var node, out var def, out var refusal))
            return ToolOutcome.Refused(refusal);

        if (def.Extra<PictureExtra>() is null)
        {
            return ToolOutcome.Refused(
                $"{Handle(node)} is a {def.Name}, which shows no picture. Only the Image module does.");
        }

        if (!Text(arguments, "path", out var path))
            return ToolOutcome.Refused("'path' is required: where the PNG file is.");

        PictureExtra.Set(node, path);
        Edits++;

        return Fine($"{Handle(node)} now shows {path}. {Issues()}");
    }

    /// <summary>
    /// Sets one field of an extra a plugin defined.
    /// </summary>
    /// <remarks>
    /// One tool for every kind a plugin will ever add, which is the whole return
    /// on declaring a schema rather than shipping a control
    /// ([0055](0055-a-plugins-extra-declares-its-editor.md)): the three built-in
    /// kinds each needed a tool written for them, and no plugin's will.
    /// <para>
    /// A field at a time rather than the whole object, which is the opposite call
    /// to <c>set_steps</c>'s and made for the opposite reason. A tune is a list
    /// whose order is the point, so half of one applied is a tune nobody asked
    /// for; these are named values that do not depend on each other, and setting
    /// one is exactly as safe as setting a knob.
    /// </para>
    /// </remarks>
    private ToolOutcome SetExtra(JsonElement arguments)
    {
        if (!Node(arguments, "handle", out var node, out var def, out var refusal))
            return ToolOutcome.Refused(refusal);

        if (!Text(arguments, "extra", out var key))
            return ToolOutcome.Refused("'extra' is required: which of the module's extras to set.");

        if (def.Extras.FirstOrDefault(e => e.Key == key) is not { } extra)
        {
            var carries = def.Extras.Count == 0
                ? "it carries none"
                : $"it carries {string.Join(", ", def.Extras.Select(e => e.Key))}";

            return ToolOutcome.Refused($"{Handle(node)} has no '{key}' — {carries}.");
        }

        // A kind with no declared fields is one this tool cannot write, and every
        // one of them is an engine kind with a tool of its own. Named from the
        // same place the module listing names it, so the refusal cannot send a
        // model somewhere the listing did not.
        if (extra.Fields.Count == 0)
        {
            return ToolOutcome.Refused(
                $"'{key}' on {Handle(node)} is not set this way — use "
                + $"{Vocabulary.ToolFor(key)}.");
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

        Edits++;

        return Fine($"set {key}.{name} on {Handle(node)}. {extra.Report(node)} {Issues()}");
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

    private ToolOutcome Connect(JsonElement arguments)
    {
        if (!Node(arguments, "from", out var source, out var sourceDef, out var refusal))
            return ToolOutcome.Refused(refusal);

        if (!Node(arguments, "to", out var target, out var targetDef, out refusal))
            return ToolOutcome.Refused(refusal);

        if (source.Id == target.Id)
            return ToolOutcome.Refused(
                "a module cannot be wired to itself — a pixel cannot depend on itself. "
                + "Use a 'feedback' module to read the previous frame.");

        int fromPort;

        if (Text(arguments, "from_port", out var fromName))
        {
            if (!Port(sourceDef.Outputs, fromName, out fromPort))
                return ToolOutcome.Refused(
                    $"{Handle(source)} has no output called '{fromName}'. Its outputs are: {List(sourceDef.Outputs)}.");
        }
        else if (sourceDef.Outputs.Count == 1)
        {
            fromPort = 0;
        }
        else
        {
            return ToolOutcome.Refused(
                $"{Handle(source)} has more than one output, so 'from_port' is needed. "
                + $"Its outputs are: {List(sourceDef.Outputs)}.");
        }

        if (!Text(arguments, "to_port", out var toName))
            return ToolOutcome.Refused("'to_port' is required and must be a string.");

        if (!Port(targetDef.Inputs, toName, out var toPort))
            return ToolOutcome.Refused(
                $"{Handle(target)} has no input called '{toName}'. Its inputs are: {List(targetDef.Inputs)}.");

        var replaced = working.IncomingTo(target.Id, toPort) is { } existing
            ? $" (replacing {Handle(working.Find(existing.SourceNode))}.{Name(existing, sourceOf: true)})"
            : string.Empty;

        working.Connect(source.Id, fromPort, target.Id, toPort);
        Edits++;

        return Fine(
            $"wired {Handle(source)}.{sourceDef.Outputs[fromPort].Name} -> "
            + $"{Handle(target)}.{targetDef.Inputs[toPort].Name}{replaced}. {Issues()}");
    }

    private ToolOutcome Disconnect(JsonElement arguments)
    {
        if (!Node(arguments, "handle", out var node, out var def, out var refusal))
            return ToolOutcome.Refused(refusal);

        if (!Text(arguments, "port", out var portName))
            return ToolOutcome.Refused("'port' is required and must be a string.");

        if (!Port(def.Inputs, portName, out var port))
            return ToolOutcome.Refused(
                $"{Handle(node)} has no input called '{portName}'. Its inputs are: {List(def.Inputs)}.");

        // What a socket falls back to when nothing is patched into it: the module
        // it is normalled to where there is one, and its knob otherwise. Both
        // messages below need it, and neither is worth being wrong about — an
        // assistant told a socket is on a knob will go looking for the knob.
        var resting = modules.Normalled(def.Inputs[port]) is { } source
            ? $"{source}, which it is normalled to"
            : $"its knob at {def.Inputs[port].Format(Knob(node, port, def))}";

        if (working.IncomingTo(node.Id, port) is null)
            return Fine($"nothing was wired to {Handle(node)}.{def.Inputs[port].Name}; it is on {resting}.");

        working.Disconnect(node.Id, port);
        Edits++;

        return Fine(
            $"unwired {Handle(node)}.{def.Inputs[port].Name}, which is back on {resting}. {Issues()}");
    }

    private ToolOutcome RemoveModule(JsonElement arguments)
    {
        if (!Node(arguments, "handle", out var node, out _, out var refusal))
            return ToolOutcome.Refused(refusal);

        var handle = Handle(node);
        var lost = working.Connections.Count(c => c.SourceNode == node.Id || c.TargetNode == node.Id);

        working.Remove(node.Id);
        byHandle.Remove(handle);
        handleOf.Remove(node.Id);
        Edits++;

        var wires = lost switch
        {
            0 => "It was wired to nothing.",
            1 => "One wire went with it.",
            _ => $"{lost} wires went with it.",
        };

        return Fine($"removed {handle}. {wires} {Issues()}");
    }

    private ToolOutcome Reset()
    {
        Adopt(PatchIo.Read(startingPoint, modules).Patch);
        proposal = null;
        Edits++;

        return Fine($"back to the patch as it was when this started. {DescribePatch()}");
    }

    private ToolOutcome Propose(JsonElement arguments)
    {
        if (!Text(arguments, "summary", out var summary))
            return ToolOutcome.Refused("'summary' is required: one line saying what this patch does.");

        // Both programs, because either may be the one that was built for. A
        // patch offered for its sound still has to have compiled its sound, and
        // the video pass never reaches a node only the ear does.
        var errors = working.CompileForVideo(modules, samples, pictures).Issues
            .Concat(working.CompileForAudio(modules, samples).Issues)
            .Where(i => i.Severity == IssueSeverity.Error)
            .Select(i => i.Message)
            .Distinct()
            .ToArray();

        if (errors.Length > 0)
            return ToolOutcome.Refused(
                "this patch does not compile cleanly yet, so it is not worth proposing: "
                + string.Join(" | ", errors));

        // Warnings do not block, but an Output nothing reaches is not a patch:
        // nothing is watching and nothing is listening, so there is nothing to
        // offer whatever the modules add up to. The sink itself is always there,
        // so what has to be checked is whether anything arrives at it.
        if (!working.Connections.Any(c => c.TargetNode == working.Output.Id))
        {
            return ToolOutcome.Refused(
                "nothing is wired into the Output, so nothing this patch does comes out anywhere. "
                + "Patch something into its 'color' or its 'left' before proposing.");
        }

        proposal = summary;
        return Fine($"proposed. {summary}");
    }

    // --- looking ------------------------------------------------------------

    private string DescribePatch()
    {
        if (working.Nodes.Count == 0) return "the patch is empty. " + Issues();

        var text = new StringBuilder();

        foreach (var node in working.Nodes)
        {
            if (modules.Get(node.TypeId) is not { } def)
            {
                text.Append(Handle(node)).Append(" = ").Append(node.TypeId).AppendLine(" (unknown module)");
                continue;
            }

            text.Append(Handle(node)).Append(" = ").AppendLine(node.TypeId);
            text.Append("  in ").AppendLine(Wiring(node, def));

            if (def.Outputs.Count > 0)
                text.Append("  out").AppendLine(FanOut(node, def));

            // What it carries that is not a knob. None of it is wiring, so it
            // shows up in neither of the two lines above — a sequencer would
            // otherwise read as a module with nothing set on it at all.
            foreach (var extra in def.Extras)
                text.Append("  ").AppendLine(extra.Report(node));
        }

        text.Append(working.Nodes.Count).Append(" modules, ")
            .Append(working.Connections.Count).AppendLine(" wires.");

        var video = working.CompileForVideo(modules, samples, pictures);
        var audio = working.CompileForAudio(modules, samples);

        text.Append("video: ").Append(video.Program.Ops.Length).Append(" ops");
        if (video.Issues.Count > 0)
            text.Append(", ").Append(string.Join(" | ", video.Issues.Select(i => i.Message)));

        text.Append(". audio: ").Append(audio.Program.Ops.Length).Append(" ops");
        if (audio.Issues.Count > 0)
            text.Append(", ").Append(string.Join(" | ", audio.Issues.Select(i => i.Message)));

        return text.Append('.').ToString();
    }

    private string Wiring(NodeInstance node, NodeDef def)
    {
        if (def.Inputs.Count == 0) return " (none)";

        var parts = new List<string>();

        for (var i = 0; i < def.Inputs.Count; i++)
        {
            var port = def.Inputs[i];

            if (working.IncomingTo(node.Id, i) is { } wire && working.Find(wire.SourceNode) is { } from)
            {
                parts.Add($"{port.Name} <- {Handle(from)}.{Name(wire, sourceOf: true)}");
            }
            else if (port.NormalledFrom >= 0)
            {
                parts.Add($"{port.Name} = {port.Format(Knob(node, i, def))} (or {def.Inputs[port.NormalledFrom].Name})");
            }
            else if (modules.Normalled(port) is { } source)
            {
                // No knob is printed here, because none is read: a normalled
                // socket compiles to the module it is normalled to and not to
                // the value stored against it. Printing both would read as a
                // knob that could be turned, which is the one thing this is not.
                parts.Add($"{port.Name} <- {source} (normalled, no wire)");
            }
            else
            {
                parts.Add($"{port.Name} = {port.Format(Knob(node, i, def))}");
            }
        }

        return " " + string.Join(" | ", parts);
    }

    private string FanOut(NodeInstance node, NodeDef def)
    {
        var parts = new List<string>();

        for (var i = 0; i < def.Outputs.Count; i++)
        {
            var index = i;

            var goes = working.Connections
                .Where(c => c.SourceNode == node.Id && c.SourcePort == index)
                .Select(c => working.Find(c.TargetNode) is { } to && modules.Get(to.TypeId) is { } toDef
                    ? $"{Handle(to)}.{PortName(toDef.Inputs, c.TargetPort)}"
                    : "?")
                .ToArray();

            parts.Add(goes.Length == 0
                ? $"{def.Outputs[i].Name} (unused)"
                : $"{def.Outputs[i].Name} -> {string.Join(", ", goes)}");
        }

        return " " + string.Join(" | ", parts);
    }

    private ToolOutcome DescribeModule(JsonElement arguments)
    {
        if (!Text(arguments, "type_id", out var typeId))
            return ToolOutcome.Refused("'type_id' is required and must be a string.");

        if (modules.Get(typeId) is null)
            return ToolOutcome.Refused($"there is no module with type id '{typeId}'. {Nearest(typeId)}");

        var text = new StringBuilder();
        Describe(text, typeId);
        return Fine(text.ToString());
    }

    private ToolOutcome FindModules(JsonElement arguments)
    {
        if (!Text(arguments, "query", out var query))
            return ToolOutcome.Refused("'query' is required and must be a string.");

        var hits = modules.All
            .Where(d => d.TypeId.Contains(query, StringComparison.OrdinalIgnoreCase)
                || d.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || d.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                || d.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(30)
            .ToArray();

        if (hits.Length == 0) return Fine($"nothing matches '{query}'.");

        return Fine(string.Join(
            Environment.NewLine,
            hits.Select(d => $"{d.TypeId} | {d.Name} | {d.Category}")));
    }

    private void Describe(StringBuilder text, string typeId)
    {
        var def = modules.Require(typeId);

        text.Append(def.TypeId).Append(" | ").Append(def.Name).Append(" | ").AppendLine(def.Category);

        for (var i = 0; i < def.Inputs.Count; i++)
        {
            var port = def.Inputs[i];
            text.Append("  in  ").Append(i).Append(' ').Append(port.Name)
                .Append(" = ").Append(port.Format(port.Default))
                .Append(" [").Append(Number(port.Min)).Append("..").Append(Number(port.Max)).Append(']')
                .AppendLine(port.Kind == PortKind.Color ? " color" : port.Kind == PortKind.Any ? " any" : "");
        }

        for (var i = 0; i < def.Outputs.Count; i++)
            text.Append("  out ").Append(i).Append(' ').AppendLine(def.Outputs[i].Name);

        // What the module carries that is neither a socket nor a knob, which
        // the two loops above cannot show — the whole reason a model asking
        // about a Sequencer or a Quantiser would otherwise miss half of it.
        foreach (var extra in def.Extras) text.AppendLine(Vocabulary.Announce(extra));

        if (def.Description.Length > 0) text.AppendLine(def.Description);
    }

    // --- rendering ----------------------------------------------------------

    /// <summary>
    /// Draws the patch and hands back a strip of frames.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The work happens on a pool thread rather than wherever the caller
    /// happened to be. An assistant's loop is consumed with <c>await foreach</c>
    /// on the dispatcher, so a render performed inline would land on the UI
    /// thread — which ADR-0018 forbids and which deadlocks besides, because the
    /// renderer's <c>Parallel.For</c> meets a dispatcher that is pumping a
    /// re-entrant paint. Doing it here rather than in each adapter makes that
    /// impossible for a plugin to get wrong.
    /// </para>
    /// <para>
    /// Frames are stepped from zero rather than jumped to, because the renderer
    /// owns the history that <c>feedback</c> reads and a patch shown without its
    /// warm-up is a patch shown black. Several frames rather than one because a
    /// still cannot show motion, which is most of what this instrument is.
    /// </para>
    /// </remarks>
    private Task<ToolOutcome> RenderAsync(JsonElement arguments, CancellationToken cancel)
    {
        var requested = Times(arguments);

        // Asked for directly, because the compiler does not remark on a color
        // socket left empty while the sound is wired — a patch built for the ear
        // is a deliberate thing, not a complaint waiting to happen. It is still
        // nothing to look at: what would come back is a black rectangle, and an
        // assistant shown black goes and "fixes" a patch that was working.
        if (working.IncomingTo(working.Output.Id, NodeCatalog.OutputColorPort) is null)
        {
            return Task.FromResult(ToolOutcome.Refused(
                "nothing is wired into the Output's 'color', so this patch draws nothing and "
                + "there is nothing to look at. Patch something in if it is meant to be seen."));
        }

        var patch = working.CompileForVideo(modules, samples, pictures);

        if (patch.HasIssues)
        {
            var why = patch.HasErrors
                ? "this patch does not compile, so there is nothing to look at: "
                : "there may be nothing to look at: ";

            return Task.FromResult(ToolOutcome.Refused(
                why + string.Join(" | ", patch.Issues.Select(i => i.Message))));
        }

        return Task.Run(
            () =>
            {
                var (width, height) = (limits.FrameWidth, limits.FrameHeight);
                var frameStride = width * 4;
                var sheetStride = frameStride * requested.Length;

                var frame = new byte[frameStride * height];
                var sheet = new byte[sheetStride * height];

                var capture = requested.Select(t => (int)Math.Round(t / limits.WarmUpStep)).ToArray();
                var renderer = new SynthRenderer();

                for (var step = 0; step <= capture[^1]; step++)
                {
                    cancel.ThrowIfCancellationRequested();

                    renderer.Render(patch.Program, step * limits.WarmUpStep, width, height, frame, frameStride);

                    for (var i = 0; i < capture.Length; i++)
                    {
                        if (capture[i] != step) continue;

                        for (var y = 0; y < height; y++)
                        {
                            Array.Copy(
                                frame,
                                y * frameStride,
                                sheet,
                                y * sheetStride + i * frameStride,
                                frameStride);
                        }
                    }
                }

                var png = new MemoryStream();
                PngWriter.WriteBgra(png, sheet, width * requested.Length, height, sheetStride);

                var when = string.Join(", ", requested.Select(t => Number(t) + "s"));

                return ToolOutcome.Looked(
                    png.ToArray(),
                    $"{requested.Length} frames left to right at {when}, {width} by {height} each. "
                    + "The renderer was warmed from zero at thirty frames a second, so anything "
                    + "reading the previous frame shows the history it would really have.");
            },
            cancel);
    }

    private double[] Times(JsonElement arguments)
    {
        // Not from zero by default. A patch that reads the previous frame is
        // legitimately black on its first one, and an assistant shown a black
        // panel tends to go and "fix" a patch that was working.
        double[] fallback = [0.5d, 1.5d, 3.5d];

        if (!arguments.TryGetProperty("times", out var times) || times.ValueKind != JsonValueKind.Array)
            return fallback;

        var asked = times.EnumerateArray()
            .Where(t => t.ValueKind == JsonValueKind.Number)
            .Select(t => Math.Clamp(t.GetDouble(), 0d, limits.LatestTime))
            .Take(limits.MaxFrames)
            .Order()
            .ToArray();

        return asked.Length == 0 ? fallback : asked;
    }

    // --- listening ----------------------------------------------------------

    /// <summary>
    /// Renders a stretch of the patch's sound and hands it back as a WAV.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On a pool thread for the same reason <see cref="RenderAsync"/> is, though
    /// the cost is nothing like the same: the audio program is evaluated about
    /// 100k times a second against the video program's 31M, so a couple of
    /// seconds of sound is cheaper than a single frame. What it is not cheap in
    /// is the request body it becomes, which is why
    /// <see cref="WorkbenchLimits.LongestListen"/> is short and
    /// <see cref="WorkbenchLimits.ListenRate"/> is half what the speakers use.
    /// </para>
    /// <para>
    /// Warmed from zero rather than sought to, exactly as a render is. The audio
    /// path is the one with delay lines and <c>feedback.unit</c> behind it
    /// (ADR-0027), so a patch started halfway along would be handed empty
    /// memory and would sound like something nobody would ever hear.
    /// </para>
    /// <para>
    /// Silence comes back as words rather than as a WAV. It is the failure this
    /// instrument produces most — an oscillator whose <c>in</c> nothing drives
    /// is legal, compiles without a word and does not move — and a model played
    /// two seconds of nothing tends to conclude the tool is broken. Saying so,
    /// and saying where to look, costs a sentence instead of a payload.
    /// </para>
    /// </remarks>
    private Task<ToolOutcome> ListenAsync(JsonElement arguments, CancellationToken cancel)
    {
        // Asked of the graph rather than of the compiler, which is content with
        // an unwired sink: silence is a legal program, and what would come back
        // is a WAV full of zeroes rather than a complaint.
        if (!working.Reaches().Sound)
        {
            return Task.FromResult(ToolOutcome.Refused(
                "nothing is wired into the Output's 'left' or 'right', so this patch makes no "
                + "sound and there is nothing to hear. Patch something in if it is meant to be heard."));
        }

        var patch = working.CompileForAudio(modules, samples);

        if (patch.HasIssues)
        {
            var why = patch.HasErrors
                ? "this patch does not compile, so there is nothing to hear: "
                : "there may be nothing to hear: ";

            return Task.FromResult(ToolOutcome.Refused(
                why + string.Join(" | ", patch.Issues.Select(i => i.Message))));
        }

        var (from, seconds) = Window(arguments);

        return Task.Run(
            () =>
            {
                var renderer = new AudioRenderer(limits.ListenRate);
                var scan = AudioScan.For(
                    working,
                    SynthRenderer.AspectOf(limits.FrameWidth, limits.FrameHeight),
                    modules);

                // Thrown away, but not skipped: this is the warm-up, and what it
                // leaves behind in the delay lines is the whole point of it.
                if (from > 0)
                {
                    var skipped = new float[Samples(from)];
                    renderer.Render(patch.Program, skipped, scan);
                }

                cancel.ThrowIfCancellationRequested();

                var samples = new float[Samples(seconds)];
                renderer.Render(patch.Program, samples, scan);

                var (peak, rms) = Levels(samples);

                if (peak < SilenceFloor)
                {
                    return ToolOutcome.Fine(
                        $"{Number(seconds)}s from {Number(from)}s is silence — nothing above "
                        + "-66 dBFS came out, so there is no point playing it to you. The "
                        + "compiler has already said whatever it can see, so look at what it "
                        + "cannot: 'gain' on the Output sitting at zero, or an 'in' that is wired "
                        + "but never moves — a knob, or anything else holding one value, drives a "
                        + "phase exactly as far as nothing does. Only a signal that changes with "
                        + "'t' makes an oscillator oscillate. A constant reaching 'left' is "
                        + "silent too: it is pure DC, and the DC blocker removes it.");
                }

                var wav = new MemoryStream();
                WavWriter.Write(wav, samples, renderer.SampleRate, NodeCatalog.AudioChannels);

                var caption = new StringBuilder(
                    $"{Number(seconds)}s of sound from {Number(from)}s, in stereo at "
                    + $"{limits.ListenRate / 1000} kHz. It was rendered from zero, so anything with "
                    + "a delay in it has the tail it would really have.");

                caption.Append("\n\n").Append(Measured(samples, peak, rms));

                // Worth saying, because it changes what the sound even is: a
                // scanning patch is being swept across its own picture, so what
                // is heard is the image and editing the picture edits the sound.
                if (scan.Scan)
                    caption.Append($" The Output is scanning at {Number(scan.Rate)} sweeps a second, "
                        + "so this is the picture being heard rather than a patch running on time.");

                return ToolOutcome.Played(wav.ToArray(), caption.ToString());
            },
            cancel);
    }

    /// <summary>Below this a buffer is called silence: -66 dBFS, and nothing a speaker would utter.</summary>
    private const float SilenceFloor = 0.0005f;

    /// <summary>How many slices the level is reported over. Enough to see a beat in a second or two.</summary>
    private const int Slices = 16;

    /// <summary>
    /// What the samples say about themselves, as against what a listener says
    /// about them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because a listener was believed once too often. A model asked
    /// to describe a patch built from three steady tones reported a thumping
    /// kickdrum and a crisp hihat — the words it had been given rather than the
    /// sound it was played — and nothing in the reply contradicted it, though
    /// the levels sitting beside the prose already did.
    /// </para>
    /// <para>
    /// Crest is the measurement that catches exactly that. It is the distance
    /// between the loudest sample and the average one, so it says whether
    /// anything in the clip is a *hit*: a steady tone has almost none and
    /// percussion has a great deal, and no description can talk its way out of
    /// the number. The slices are the same question over time — a rhythm shows
    /// as a level that moves, and a drone as a row of near-identical figures.
    /// </para>
    /// <para>
    /// Reported as numbers with the yardstick beside them rather than as a
    /// verdict. What counts as percussive enough is the reader's to judge; what
    /// is not the reader's to judge is what the samples measure.
    /// </para>
    /// </remarks>
    private static string Measured(ReadOnlySpan<float> samples, float peak, float rms)
    {
        var text = new StringBuilder("Measured from the samples, not heard: peak ")
            .Append(Decibels(peak))
            .Append(", rms ")
            .Append(Decibels(rms))
            .Append(", crest ")
            .Append(Gap(peak, rms))
            .Append(". Crest is peak above rms: a steady tone sits near 3 dB, a mix with drum "
                + "hits in it 12 dB or more. Level in ")
            .Append(Slices)
            .Append(" slices across the clip, in dBFS:");

        var frames = samples.Length / NodeCatalog.AudioChannels;
        var slice = Math.Max(1, frames / Slices);

        for (var i = 0; i < Slices; i++)
        {
            var start = i * slice * NodeCatalog.AudioChannels;
            if (start >= samples.Length) break;

            var length = Math.Min(slice * NodeCatalog.AudioChannels, samples.Length - start);

            text.Append(' ').Append(Decibels(Levels(samples.Slice(start, length)).Rms).Replace(" dBFS", ""));
        }

        text.Append(". A row of near-identical figures is something continuous; a rhythm moves.");

        return text.ToString();
    }

    /// <summary>The distance between two levels, which is a ratio rather than a level.</summary>
    private static string Gap(float above, float below) =>
        above <= 0f || below <= 0f
            ? "n/a"
            : (20 * Math.Log10(above / below)).ToString("0.0", CultureInfo.InvariantCulture) + " dB";

    private int Samples(double seconds) =>
        (int)Math.Round(limits.ListenRate * seconds) * NodeCatalog.AudioChannels;

    /// <summary>Which stretch of the timeline to render, clamped to what one call may spend.</summary>
    private (double From, double Seconds) Window(JsonElement arguments)
    {
        var from = arguments.TryGetProperty("from", out var start) && start.ValueKind == JsonValueKind.Number
            ? Math.Clamp(start.GetDouble(), 0d, limits.LatestTime)
            : 0d;

        var seconds = arguments.TryGetProperty("seconds", out var length)
            && length.ValueKind == JsonValueKind.Number
                ? Math.Clamp(length.GetDouble(), 0.25d, limits.LongestListen)
                : Math.Min(2d, limits.LongestListen);

        return (from, seconds);
    }

    /// <summary>Peak and rms of an interleaved buffer, over both channels at once.</summary>
    private static (float Peak, float Rms) Levels(ReadOnlySpan<float> samples)
    {
        var peak = 0f;
        var sum = 0d;

        foreach (var sample in samples)
        {
            var size = Math.Abs(sample);
            if (size > peak) peak = size;
            sum += (double)sample * sample;
        }

        return (peak, samples.Length == 0 ? 0f : (float)Math.Sqrt(sum / samples.Length));
    }

    private static string Decibels(float level) => level <= 0f
        ? "-inf dBFS"
        : (20 * Math.Log10(level)).ToString("0.0", CultureInfo.InvariantCulture) + " dBFS";

    // --- layout -------------------------------------------------------------

    /// <summary>
    /// Places the nodes so the patch reads left to right, sinks on the right.
    /// The assistant never thinks about coordinates; without this every node
    /// would arrive stacked at the origin.
    /// </summary>
    /// <remarks>
    /// The same routine the editor's own tidy button runs, which is the point of
    /// it being shared: a patch that arrives from here is laid out exactly as one
    /// the user has just tidied, so there is nothing to clean up after. What it
    /// replaced counted columns and stacked them at a fixed pitch, and the pitch
    /// was shorter than a tall module — see ADR-0044.
    /// </remarks>
    private void Arrange() => PatchLayout.Arrange(working, modules);

    // --- the vocabulary itself ----------------------------------------------

    private IReadOnlyList<PatchTool> BuildTools(bool vision, bool hearing, bool lookups)
    {
        List<PatchTool> tools =
        [
            new("describe_patch",
                "Every module in the working patch with its handle, what each input is set to or "
                + "wired from, where each output goes, and what the compiler currently says. Call "
                + "this first.",
                "{}"),

            new("add_module",
                "Places a module. 'type_id' comes from the module list. 'handle' is optional — one "
                + "is made up from the type id if you leave it out. 'knobs' sets inputs by name as "
                + "you add it, which saves a second call.",
                """
                {
                  "properties": {
                    "type_id": { "type": "string", "description": "The module's type id, e.g. osc.sine." },
                    "handle": { "type": "string", "description": "What to call it, e.g. sine1." },
                    "knobs": {
                      "type": "array",
                      "description": "Inputs to set as it is placed.",
                      "items": {
                        "type": "object",
                        "properties": {
                          "port": { "type": "string", "description": "The input's name." },
                          "value": { "type": "number" }
                        },
                        "required": ["port", "value"]
                      }
                    }
                  },
                  "required": ["type_id"]
                }
                """),

            new("set_knobs",
                "Sets one or more inputs on a module that is already placed. An input with a wire "
                + "into it keeps its knob value but ignores it until the wire is removed.",
                """
                {
                  "properties": {
                    "handle": { "type": "string" },
                    "knobs": {
                      "type": "array",
                      "items": {
                        "type": "object",
                        "properties": {
                          "port": { "type": "string" },
                          "value": { "type": "number" }
                        },
                        "required": ["port", "value"]
                      }
                    }
                  },
                  "required": ["handle", "knobs"]
                }
                """),

            new(Vocabulary.SetSteps,
                "Replaces the whole tune on a sequencer. Its notes are a list on the module rather "
                + "than knobs, so this is the only way to write one — send every note in order, "
                + "because this replaces what was there. 'value' is a note number on a Note "
                + "Sequencer (57 is A3) and an ordinary signal on a Sequencer. 'length' is how "
                + "many steps the note lasts and defaults to 1; 'volume' is 0 to 1, a level rather "
                + "than a switch, and defaults to 1 — a note at 0 is a rest that still holds its "
                + "value.",
                """
                {
                  "properties": {
                    "handle": { "type": "string" },
                    "notes": {
                      "type": "array",
                      "items": {
                        "type": "object",
                        "properties": {
                          "value": { "type": "number" },
                          "length": { "type": "number" },
                          "volume": { "type": "number" }
                        },
                        "required": ["value"]
                      }
                    }
                  },
                  "required": ["handle", "notes"]
                }
                """),

            new(Vocabulary.SetScale,
                "Replaces the whole scale on a Quantiser. Its notes are a list on the module "
                + "rather than knobs, so this is the only way to set one — send every note you "
                + "want, because this replaces what was there. They are pitch classes, 0 to 11, "
                + "where 0 is C, 2 is D, 4 is E and 9 is A: naming one puts every octave of it "
                + "in the scale rather than a single note, which is what makes a scale repeat up "
                + "the keyboard. Order and repeats do not matter. C major is [0,2,4,5,7,9,11] "
                + "and a minor pentatonic on A is [0,3,5,7,10]. All twelve snaps to the nearest "
                + "semitone, which is what a Note module already does; an empty list is a wire.",
                """
                {
                  "properties": {
                    "handle": { "type": "string" },
                    "notes": {
                      "type": "array",
                      "items": { "type": "integer", "minimum": 0, "maximum": 11 }
                    }
                  },
                  "required": ["handle", "notes"]
                }
                """),

            new(Vocabulary.SetSample,
                "Points a Sample module at a sound file. The path is neither a knob nor a wire, "
                + "so this is the only way to set one — and it is the one thing in a patch that "
                + "refers to something outside it, so the file has to exist where you say it "
                + "does. A WAV: mono or stereo, 8 to 32 bit or float, no other format. The "
                + "answer says whether it could be read, so a path that is wrong is answered "
                + "now rather than by silence later. Ask the person for a path rather than "
                + "guessing at one — nothing here can list what is on their machine.",
                """
                {
                  "properties": {
                    "handle": { "type": "string" },
                    "path": { "type": "string", "description": "Where the WAV is, absolute or beside the patch." }
                  },
                  "required": ["handle", "path"]
                }
                """),

            new(Vocabulary.SetPicture,
                "Points an Image module at a picture file. The path is neither a knob nor a wire, "
                + "so this is the only way to set one, and like a sample it refers to something "
                + "outside the patch — the file has to exist where you say it does. A PNG: 8 or 16 "
                + "bit, not interlaced. The answer says whether it could be read, so a path that is "
                + "wrong is answered now rather than by a black picture later. Ask the person for a "
                + "path rather than guessing at one — nothing here can list what is on their "
                + "machine.",
                """
                {
                  "properties": {
                    "handle": { "type": "string" },
                    "path": { "type": "string", "description": "Where the PNG is, absolute or beside the patch." }
                  },
                  "required": ["handle", "path"]
                }
                """),

            new(Vocabulary.SetExtra,
                "Sets one named value on a module that carries something a plugin defined — the "
                + "rows a module listing writes under a name of their own, like 'notes' or "
                + "'chord', rather than as 'in N'. Take the extra's name and the field's from "
                + "that listing; describe_module says what a given module carries and what each "
                + "field may hold. A number is clamped to the field's range, a switch takes "
                + "true or false, and a choice takes the id of one of the things it offers — "
                + "describe_module lists them, and a refusal names them too. The built-in "
                + "notes, scale, file and picture are not set this way: they have set_steps, "
                + "set_scale, set_sample and set_picture.",
                """
                {
                  "properties": {
                    "handle": { "type": "string" },
                    "extra": { "type": "string", "description": "Which extra, as the listing names it." },
                    "field": { "type": "string", "description": "Which of its values." },
                    "value": { "description": "A number, a boolean, or a choice's id as a string — matching the field." }
                  },
                  "required": ["handle", "extra", "field", "value"]
                }
                """),

            new("connect",
                "Wires an output to an input. 'from_port' may be left out when the source has only "
                + "one output, which most modules do. An input takes one wire, so this replaces "
                + "whatever was there and tells you what it replaced.",
                """
                {
                  "properties": {
                    "from": { "type": "string", "description": "Handle of the module the signal leaves." },
                    "from_port": { "type": "string", "description": "Output name. Optional when there is only one." },
                    "to": { "type": "string", "description": "Handle of the module the signal arrives at." },
                    "to_port": { "type": "string", "description": "Input name." }
                  },
                  "required": ["from", "to", "to_port"]
                }
                """),

            new("disconnect",
                "Removes the wire feeding an input, which puts that input back on its own knob.",
                """
                {
                  "properties": {
                    "handle": { "type": "string" },
                    "port": { "type": "string", "description": "The input's name." }
                  },
                  "required": ["handle", "port"]
                }
                """),

            new("remove_module",
                "Deletes a module and every wire attached to it.",
                """
                { "properties": { "handle": { "type": "string" } }, "required": ["handle"] }
                """),

            new("reset",
                "Throws away every edit and goes back to the patch as it was when this started.",
                "{}"),

            new("propose",
                "Offers the patch to the person, with one line saying what it does. This ends your "
                + "turn. Nothing you have built reaches their editor until you call this. The patch "
                + "must compile cleanly first, and must have an output — a Video Output, an Audio "
                + "Output, or both.",
                """
                {
                  "properties": {
                    "summary": { "type": "string", "description": "One line: what this patch does." }
                  },
                  "required": ["summary"]
                }
                """),
        ];

        if (vision)
        {
            tools.Add(new PatchTool(
                "render",
                "Draws the patch and shows you the result: several frames side by side, so you can "
                + "see movement as well as color. Use it once the shape is right, and again after "
                + "adjusting what you saw.",
                $$"""
                {
                  "properties": {
                    "times": {
                      "type": "array",
                      "description": "Seconds to capture, at most {{limits.MaxFrames}} of them, up to {{Number(limits.LatestTime)}}. Defaults to 0.5, 1.5 and 3.5.",
                      "items": { "type": "number" }
                    },
                    "note": { "type": "string", "description": "What you are looking for, for your own record." }
                  }
                }
                """));
        }

        if (hearing)
        {
            tools.Add(new PatchTool(
                "listen",
                "Renders the patch's sound, measures it, and has a model that can hear describe it "
                + "to you. Use it once the sound is wired, and again after adjusting what was "
                + "described. It is the only way to find out whether a patch built for the speakers "
                + "is anything at all, as opposed to merely legal.",
                $$"""
                {
                  "properties": {
                    "seconds": {
                      "type": "number",
                      "description": "How much to render, from 0.25 to {{Number(limits.LongestListen)}}. Defaults to 2."
                    },
                    "from": {
                      "type": "number",
                      "description": "Where on the timeline to start, up to {{Number(limits.LatestTime)}}. Defaults to 0. Everything before it is still rendered, so delays arrive with the tail they would really have."
                    },
                    "note": { "type": "string", "description": "What you are listening for, for your own record. It is deliberately not passed on: whoever listens is told nothing about the patch, so that what comes back could disagree with you." }
                  }
                }
                """));
        }

        if (lookups)
        {
            tools.Add(new PatchTool(
                "describe_module",
                "Everything about one module: its ports, their defaults and ranges, and what it is "
                + "for.",
                """
                { "properties": { "type_id": { "type": "string" } }, "required": ["type_id"] }
                """));

            tools.Add(new PatchTool(
                "find_modules",
                "Searches the module list by type id, name, category or description.",
                """
                { "properties": { "query": { "type": "string" } }, "required": ["query"] }
                """));
        }

        return tools;
    }

    // --- small helpers ------------------------------------------------------

    private void Adopt(Patch patch)
    {
        working = patch;

        // Before the handles are made, so the Output gets one like everything
        // else and the assistant has something to wire into. It cannot add one
        // and cannot remove one, so this is where it has to arrive.
        working.EnsureOutput(modules);

        byHandle.Clear();
        handleOf.Clear();

        foreach (var node in patch.Nodes)
        {
            var handle = Available(node.TypeId);
            byHandle[handle] = node;
            handleOf[node.Id] = handle;
        }
    }

    /// <summary>A handle nothing is using yet, made from the tail of the type id.</summary>
    private string Available(string typeId)
    {
        var tail = typeId[(typeId.LastIndexOf('.') + 1)..];
        var stem = new string(tail.Where(char.IsAsciiLetterOrDigit).ToArray()).ToLowerInvariant();

        if (stem.Length == 0 || char.IsAsciiDigit(stem[0])) stem = "node" + stem;

        for (var n = 1; ; n++)
        {
            var candidate = stem + n.ToString(CultureInfo.InvariantCulture);
            if (!byHandle.ContainsKey(candidate)) return candidate;
        }
    }

    private string Handle(NodeInstance? node) =>
        node is not null && handleOf.TryGetValue(node.Id, out var handle) ? handle : "?";

    private bool Node(
        JsonElement arguments,
        string field,
        out NodeInstance node,
        out NodeDef def,
        out string refusal)
    {
        node = null!;
        def = null!;

        if (!Text(arguments, field, out var handle))
        {
            refusal = $"'{field}' is required and must be a module's handle.";
            return false;
        }

        if (!byHandle.TryGetValue(handle, out var found))
        {
            refusal = byHandle.Count == 0
                ? $"there is no module called '{handle}'; the patch is empty."
                : $"there is no module called '{handle}'. The patch has: {string.Join(", ", byHandle.Keys)}.";
            return false;
        }

        if (modules.Get(found.TypeId) is not { } definition)
        {
            refusal = $"'{handle}' is a {found.TypeId}, which this build does not have.";
            return false;
        }

        node = found;
        def = definition;
        refusal = string.Empty;
        return true;
    }

    /// <summary>Applies a knob list, or says why it could not. Null means it worked.</summary>
    private string? Turn(NodeInstance node, NodeDef def, JsonElement knobs)
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
                return $"{Handle(node)} has no input called '{portName}'. Its inputs are: {List(def.Inputs)}.";

            // A normalled socket has no knob to turn: it compiles to the module
            // it is normalled to, and the value stored against it is never read.
            // Refused rather than stored quietly, because storing it would look
            // like it worked and the patch would not change — which is the kind
            // of thing an assistant can spend a whole run failing to notice.
            if (modules.Normalled(def.Inputs[port]) is { } source)
            {
                return $"{Handle(node)}'s '{portName}' is normalled to {source} and has no knob: "
                    + "it is already reading that, with no wire, and a value set here would not be "
                    + $"read. Patch something into '{portName}' to drive it with that instead — a "
                    + "Value module if what you want there really is a constant.";
            }

            Grow(node, def);
            node.InputValues[port] = (float)value.GetDouble();
            Edits++;
        }

        return null;
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

    private static float Knob(NodeInstance node, int port, NodeDef def) =>
        port < node.InputValues.Length ? node.InputValues[port] : def.Inputs[port].Default;

    private static string Sockets(NodeDef def) => $"Its ports: in {List(def.Inputs)}; out {List(def.Outputs)}.";

    private string Issues()
    {
        // Both programs, for the reason 'propose' asks after both: the video
        // pass stops at the first line when there is no screen, so a patch built
        // for the speakers was reported flawless whatever was wrong with it.
        // That is precisely what a silent patch looks like from this side — an
        // assistant told "No issues." after every edit has no way left to find
        // out, because it cannot hear the thing either.
        var issues = working.CompileForVideo(modules, samples, pictures).Issues
            .Concat(working.CompileForAudio(modules, samples).Issues)
            .ToArray();

        if (issues.Length == 0) return "No issues.";

        // Distinct, because a module both sinks reach is compiled twice and
        // would otherwise be complained about twice.
        var faults = issues.Where(i => i.Severity == IssueSeverity.Error)
            .Select(i => i.Message).Distinct().ToArray();

        var notes = issues.Where(i => i.Severity != IssueSeverity.Error)
            .Select(i => i.Message).Distinct().ToArray();

        var text = new StringBuilder();

        if (faults.Length > 0) text.Append("Issues: ").Append(string.Join(" | ", faults)).Append('.');

        // Kept apart from the faults rather than listed with them. A warning is
        // something to know, not something to clear — an assistant that cannot
        // tell the two apart will spend the run clearing them instead of
        // finishing, which is what a run that never proposed anything looks
        // like from out here.
        if (notes.Length > 0)
        {
            if (text.Length > 0) text.Append(' ');
            text.Append("Worth knowing: ").Append(string.Join(" | ", notes)).Append('.');
        }

        return text.ToString();
    }

    private string Nearest(string typeId)
    {
        var tail = typeId[(typeId.LastIndexOf('.') + 1)..];

        var close = modules.All
            .Where(d => d.TypeId.Contains(tail, StringComparison.OrdinalIgnoreCase)
                || d.Name.Contains(tail, StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .Select(d => d.TypeId)
            .ToArray();

        return close.Length == 0
            ? "Use find_modules, or read the module list again."
            : $"Did you mean: {string.Join(", ", close)}?";
    }

    private string Name(Connection wire, bool sourceOf)
    {
        if (working.Find(sourceOf ? wire.SourceNode : wire.TargetNode) is not { } node) return "?";
        if (modules.Get(node.TypeId) is not { } def) return "?";

        return sourceOf ? PortName(def.Outputs, wire.SourcePort) : PortName(def.Inputs, wire.TargetPort);
    }

    private static string PortName(IReadOnlyList<PortSpec> ports, int index) =>
        index >= 0 && index < ports.Count ? ports[index].Name : index.ToString(CultureInfo.InvariantCulture);

    private static string List(IReadOnlyList<PortSpec> ports) =>
        ports.Count == 0
            ? "(none)"
            : string.Join(", ", ports.Select((p, i) => $"{i} {p.Name}"));

    private static bool Port(IReadOnlyList<PortSpec> ports, string name, out int index)
    {
        // An index is accepted as well as a name, which is the way out if a
        // plugin ever ships two ports called the same thing. Every listing this
        // class prints shows both, so the escape hatch is always in view.
        if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var direct)
            && direct < ports.Count)
        {
            index = direct;
            return true;
        }

        for (var i = 0; i < ports.Count; i++)
        {
            if (!string.Equals(ports[i].Name, name, StringComparison.OrdinalIgnoreCase)) continue;

            index = i;
            return true;
        }

        index = -1;
        return false;
    }

    private static bool Text(JsonElement arguments, string field, out string value)
    {
        if (arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty(field, out var found)
            && found.ValueKind == JsonValueKind.String
            && found.GetString() is { Length: > 0 } text)
        {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static ToolOutcome Fine(string text) => ToolOutcome.Fine(text);

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
