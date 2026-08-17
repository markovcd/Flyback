using System.Globalization;
using System.Text;
using System.Text.Json;
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
    private readonly WorkbenchLimits limits;
    private readonly string startingPoint;
    private readonly Dictionary<string, NodeInstance> byHandle = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, string> handleOf = [];

    private Patch working = new();
    private string? proposal;

    public PatchWorkbench(
        ModuleCatalog modules,
        Patch startingPoint,
        bool vision = true,
        WorkbenchLimits? limits = null)
    {
        this.modules = modules;
        this.limits = limits ?? new WorkbenchLimits();

        // Kept as text so Reset cannot hand back something an earlier edit
        // reached into, and so the starting point is provably reloadable.
        this.startingPoint = PatchIo.ToJson(startingPoint, modules);

        Adopt(PatchIo.Read(this.startingPoint, modules).Patch);

        var prose = Handbook.Render(modules, prose: true);
        var large = prose.Length > Handbook.ProseBudget;

        Briefing = large ? Handbook.Render(modules, prose: false) : prose;
        Tools = BuildTools(vision, lookups: large);
    }

    /// <summary>The conventions and the catalogue, as a model should be told them.</summary>
    public string Briefing { get; }

    public IReadOnlyList<PatchTool> Tools { get; }

    public bool HasProposal => proposal is not null;

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
                "connect" => Connect(arguments),
                "disconnect" => Disconnect(arguments),
                "remove_module" => RemoveModule(arguments),
                "reset" => Reset(),
                "propose" => Propose(arguments),
                "render" => await RenderAsync(arguments, cancel).ConfigureAwait(false),
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

        // A patch has one screen and one pair of speakers. The second sink is
        // the mistake that hides itself — compilation roots at the first one and
        // never reaches the other, so the patch compiles, says nothing, and half
        // of what was wired up is simply not there.
        if (!working.CanAdd(typeId) && working.FirstOf(typeId) is { } sink)
            return ToolOutcome.Refused(
                $"this patch already has a {def.Name}, as {Handle(sink)}, and may have only one. "
                + "Wire into that one, or remove it first if it is in the wrong place.");

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

        report.Append(' ').Append(Sockets(node, def)).Append(' ').Append(Issues());
        return Fine(report.ToString());
    }

    private ToolOutcome SetKnobs(JsonElement arguments)
    {
        if (!Node(arguments, "handle", out var node, out var def, out var refusal))
            return ToolOutcome.Refused(refusal);

        if (!arguments.TryGetProperty("knobs", out var knobs))
            return ToolOutcome.Refused("'knobs' is required: a list of {port, value}.");

        if (Turn(node, def, knobs) is { } bad) return ToolOutcome.Refused(bad);

        return Fine($"set. {Sockets(node, def)} {Issues()}");
    }

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

        if (working.IncomingTo(node.Id, port) is null)
            return Fine($"nothing was wired to {Handle(node)}.{def.Inputs[port].Name}; its knob is in use.");

        working.Disconnect(node.Id, port);
        Edits++;

        return Fine(
            $"unwired {Handle(node)}.{def.Inputs[port].Name}, which is back on its knob "
            + $"at {def.Inputs[port].Format(Knob(node, port, def))}. {Issues()}");
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
        var errors = working.CompileForVideo(modules).Issues
            .Concat(working.CompileForAudio(modules).Issues)
            .Where(i => i.Severity == IssueSeverity.Error)
            .Select(i => i.Message)
            .Distinct()
            .ToArray();

        if (errors.Length > 0)
            return ToolOutcome.Refused(
                "this patch does not compile cleanly yet, so it is not worth proposing: "
                + string.Join(" | ", errors));

        // Warnings do not block, but a patch with neither sink is not a patch:
        // nothing is watching and nothing is listening, so there is nothing to
        // offer whatever the modules add up to.
        if (!working.Nodes.Any(n =>
            n.TypeId == NodeCatalog.VideoOutputTypeId || n.TypeId == NodeCatalog.AudioOutputTypeId))
        {
            return ToolOutcome.Refused(
                "this patch has no Video Output and no Audio Output, so nothing it does comes out "
                + "anywhere. Add one before proposing.");
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
        }

        text.Append(working.Nodes.Count).Append(" modules, ")
            .Append(working.Connections.Count).AppendLine(" wires.");

        var video = working.CompileForVideo(modules);
        var audio = working.CompileForAudio(modules);

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
                .AppendLine(port.Kind == PortKind.Colour ? " colour" : port.Kind == PortKind.Any ? " any" : "");
        }

        for (var i = 0; i < def.Outputs.Count; i++)
            text.Append("  out ").Append(i).Append(' ').AppendLine(def.Outputs[i].Name);

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

        // Asked for directly, because the compiler no longer remarks on a
        // missing screen once the speakers are wired — a patch built for sound
        // is a deliberate thing, not a complaint waiting to happen. It is still
        // nothing to look at: what would come back is a black rectangle, and an
        // assistant shown black goes and "fixes" a patch that was working.
        if (!working.Nodes.Any(n => n.TypeId == NodeCatalog.VideoOutputTypeId))
        {
            return Task.FromResult(ToolOutcome.Refused(
                "this patch has no Video Output, so it draws nothing and there is nothing to "
                + "look at. Add one if it is meant to be seen."));
        }

        var patch = working.CompileForVideo(modules);

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

    // --- layout -------------------------------------------------------------

    /// <summary>
    /// Places the nodes so the patch reads left to right, sinks on the right.
    /// The assistant never thinks about coordinates; without this every node
    /// would arrive stacked at the origin.
    /// </summary>
    private void Arrange()
    {
        const double columnWidth = 220d;
        const double rowHeight = 130d;

        var depth = new Dictionary<Guid, int>();
        foreach (var node in working.Nodes) Depth(node.Id, depth, []);

        var deepest = depth.Count == 0 ? 0 : depth.Values.Max();
        var filled = new Dictionary<int, int>();

        foreach (var node in working.Nodes)
        {
            var column = deepest - depth[node.Id];
            var row = filled.GetValueOrDefault(column);

            filled[column] = row + 1;
            node.X = column * columnWidth;
            node.Y = row * rowHeight;
        }
    }

    /// <summary>
    /// How far a node is from the far end of the patch, counted in wires. A node
    /// already being measured scores zero rather than recursing: the compiler
    /// refuses cycles, but the working patch is allowed to hold one for as long
    /// as it takes the assistant to notice.
    /// </summary>
    private int Depth(Guid id, Dictionary<Guid, int> known, HashSet<Guid> walking)
    {
        if (known.TryGetValue(id, out var already)) return already;
        if (!walking.Add(id)) return 0;

        var deepest = 0;

        foreach (var wire in working.Connections)
        {
            if (wire.SourceNode != id) continue;

            deepest = Math.Max(deepest, 1 + Depth(wire.TargetNode, known, walking));
        }

        walking.Remove(id);
        return known[id] = deepest;
    }

    // --- the vocabulary itself ----------------------------------------------

    private IReadOnlyList<PatchTool> BuildTools(bool vision, bool lookups)
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
                + "see movement as well as colour. Use it once the shape is right, and again after "
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

    private string Sockets(NodeInstance node, NodeDef def) => $"Its ports: in {List(def.Inputs)}; out {List(def.Outputs)}.";

    private string Issues()
    {
        // Both programs, for the reason 'propose' asks after both: the video
        // pass stops at the first line when there is no screen, so a patch built
        // for the speakers was reported flawless whatever was wrong with it.
        // That is precisely what a silent patch looks like from this side — an
        // assistant told "No issues." after every edit has no way left to find
        // out, because it cannot hear the thing either.
        var issues = working.CompileForVideo(modules).Issues
            .Concat(working.CompileForAudio(modules).Issues)
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
