using Flyback.Core.Graph;

namespace Flyback.Core.Compile;

/// <summary>
/// How much an issue ought to stop something.
/// </summary>
/// <remarks>
/// <see cref="Error"/> is first so it is the default of the enum and the default
/// of <see cref="CompileIssue"/>'s parameter: a new complaint that nobody has
/// thought about the weight of should block, not slip through.
/// </remarks>
public enum IssueSeverity
{
    /// <summary>The patch is wrong here. What compiled is a stand-in.</summary>
    Error,

    /// <summary>
    /// Worth saying, but not wrong. A patch that trips only warnings is one
    /// somebody may have meant, and is still worth offering.
    /// </summary>
    Warning,
}

/// <summary>Anything the compiler wants to tell the user about a patch.</summary>
public sealed record CompileIssue(Guid? NodeId, string Message, IssueSeverity Severity = IssueSeverity.Error);

public sealed record CompileResult(CompiledPatch Program, IReadOnlyList<CompileIssue> Issues)
{
    public bool HasIssues => Issues.Count > 0;

    /// <summary>Whether anything here is wrong, as opposed to merely worth saying.</summary>
    public bool HasErrors => Issues.Any(i => i.Severity == IssueSeverity.Error);
}

/// <summary>
/// Walks back from a sink node and lowers everything it reaches into a flat op
/// list. Nodes that nothing depends on are simply never visited, so a patch
/// costs only what actually reaches that sink.
/// </summary>
/// <remarks>
/// Compilation is rooted at a sink rather than at "the output", so one patch
/// yields one program per sink: the screen and the speakers each get their own,
/// and a module only the ear reaches costs the eye nothing.
/// </remarks>
public static class PatchCompiler
{
    /// <summary>Compiles the program the screen shows, reading the Output's colour.</summary>
    public static CompileResult CompileForVideo(this Patch patch, ModuleCatalog? modules = null) =>
        Compile(patch, NodeCatalog.Screen, modules);

    /// <summary>Compiles the program the speakers play, reading the Output's left and right.</summary>
    public static CompileResult CompileForAudio(this Patch patch, ModuleCatalog? modules = null) =>
        Compile(patch, NodeCatalog.Speakers, modules);

    /// <param name="patch">The graph to lower.</param>
    /// <param name="sink">Which of the Output's results this program reads.</param>
    /// <param name="modules">
    /// Which catalogue the type ids mean, defaulting to the installed one. Named
    /// explicitly, a patch can be compiled against a catalogue that is not the
    /// running program's — which is what makes a plugin's modules testable.
    /// </param>
    private static CompileResult Compile(
        Patch patch,
        NodeCatalog.SinkKind sink,
        ModuleCatalog? modules)
    {
        var catalog = modules ?? NodeCatalog.Current;
        var width = sink.Width;

        var issues = new List<CompileIssue>();
        var root = patch.FirstOf(NodeCatalog.OutputTypeId);

        if (root is null)
        {
            // Every patch is supposed to carry one, so reaching here means a
            // graph assembled by hand rather than through Patch.EnsureOutput.
            // Still a value rather than a throw: the compiler's job is to say
            // what is wrong with a patch, not to refuse to look at it.
            issues.Add(new CompileIssue(
                null,
                "This patch has no Output. It cannot be seen or heard until one is put back.",
                IssueSeverity.Warning));

            return new CompileResult(CompiledPatch.Constant(width), issues);
        }

        // An Output with nothing wired into it at all compiles to a constant:
        // one flat colour and silence. That is a legal program and not a patch,
        // and it is the same complaint as a domain left on its knob, made about
        // the one node whose knobs were never going to be the point.
        //
        // Said only when *nothing* reaches it. A patch wired for the eye and not
        // the ear is as deliberate as one wired the other way, and nagging
        // either about the half it does not use would be noise on every edit —
        // which is what ADR-0022 established when these were two nodes.
        if (!patch.Connections.Any(c => c.TargetNode == root.Id))
        {
            issues.Add(new CompileIssue(
                root.Id,
                "Nothing is wired into the Output, so there is nothing to see or hear. "
                + "Patch something into its 'colour' or its 'left'.",
                IssueSeverity.Warning));
        }

        var emitter = new Emitter();
        var resolved = new Dictionary<Guid, Slot[]>();
        var visiting = new HashSet<Guid>();
        var loops = new Queue<(NodeInstance Node, NodeDef Def, int Slot)>();

        // Only this program's share of the sink's results. Everything upstream
        // of the other half is never resolved, so it emits no ops.
        var result = Resolve(root)[sink.Results];

        // Now close whatever loops that walk found. Every read in the program is
        // emitted by the time the first write is, and that ordering is the whole
        // of a cycle's latency: a value handed to a cell here cannot be seen
        // until the next evaluation, however the wires run.
        //
        // Drained as a queue rather than resolved in place, because a breaker's
        // own input may reach a breaker the walk above never touched — and that
        // one's write has to land after every read just as much as this one's.
        while (loops.Count > 0)
        {
            var (node, def, slot) = loops.Dequeue();
            emitter.UnitWrite(slot, ResolveInput(node, def, 0));
        }

        var value = emitter.PackChannels(result, width);

        return new CompileResult(
            new CompiledPatch(emitter.ToProgram(), emitter.RegisterCount, value.Base, width),
            issues);

        Slot[] Resolve(NodeInstance node)
        {
            if (resolved.TryGetValue(node.Id, out var cached)) return cached;

            var def = catalog.Get(node.TypeId);
            if (def is null)
            {
                issues.Add(new CompileIssue(node.Id, $"Unknown module '{node.TypeId}'."));
                return resolved[node.Id] = [emitter.Constant(0f)];
            }

            // A cycle breaker is the one module this walk does not enter, and that
            // is the whole of how a patch may hold a loop: a wire running
            // backwards is allowed to land here, and the walk stops rather than
            // arriving somewhere it is already inside. What it hands back is the
            // cell as the previous evaluation left it — the write that fills the
            // cell is deferred to the drain above.
            if (def.IsCycleBreaker)
            {
                var slot = emitter.AllocateUnitSlot();
                var outputs = new[] { emitter.UnitRead(slot) };

                // Cached before the write is queued, so a breaker that two things
                // read from is still one cell, read once and written once.
                resolved[node.Id] = outputs;

                // Nothing to carry round when there is no socket to carry it from,
                // which only a module declared by hand could manage. Reads zero
                // for ever rather than reaching for an input that is not there.
                if (def.Inputs.Count > 0) loops.Enqueue((node, def, slot));

                return outputs;
            }

            if (!visiting.Add(node.Id))
            {
                issues.Add(new CompileIssue(node.Id,
                    $"'{def.Name}' feeds back into itself. Put a Unit Delay somewhere in the loop "
                    + "to carry the previous evaluation round, or a Feedback module to read the "
                    + "previous frame."));
                return [.. def.Outputs.Select(_ => emitter.Constant(0f))];
            }

            var inputs = new Slot[def.Inputs.Count];

            // On the sink, only the sockets this program is rooted at. The rest
            // stand in as zero.
            //
            // The emit function still runs whole, so the other half's own ops
            // survive as a multiply by nothing: two of them in a video program,
            // none in an audio one. That is the price of the sink staying data
            // like every other module (ADR-0008) rather than the compiler
            // knowing what an Output does — everything *patched into* the other
            // half is still never visited, which is where all the cost is.
            var (firstPort, portCount) = node.Id == root.Id
                ? sink.Inputs.GetOffsetAndLength(inputs.Length)
                : (0, inputs.Length);

            for (var port = 0; port < inputs.Length; port++)
            {
                if (port < firstPort || port >= firstPort + portCount)
                {
                    inputs[port] = emitter.Constant(0f);
                    continue;
                }

                var spec = def.Inputs[port];
                var incoming = patch.IncomingTo(node.Id, port);
                Slot slotValue;

                if (incoming is not null && patch.Find(incoming.SourceNode) is { } source)
                {
                    slotValue = Pick(Resolve(source), incoming.SourcePort);
                }
                else if (spec.NormalledFrom >= 0 && spec.NormalledFrom < port)
                {
                    // A normalled jack carries an earlier input through when
                    // nothing is patched in. Only earlier ports can be named,
                    // because inputs resolve in order.
                    slotValue = inputs[spec.NormalledFrom];
                }
                else
                {
                    // A domain port left on its knob is a constant, and a module
                    // read across a constant does not move: an oscillator holds
                    // one value rather than oscillating, a sequencer holds one
                    // step rather than playing. Both compile to something
                    // perfectly valid, and that is the trouble with them — a
                    // patch built this way is silent, or a flat field, with
                    // nothing anywhere to say why.
                    if (spec.Domain)
                    {
                        issues.Add(new CompileIssue(
                            node.Id,
                            $"Nothing is wired into {def.Name}'s '{spec.Name}', so it never moves. "
                            + "Patch Time in to hear it, or Coordinates to draw with it.",
                            IssueSeverity.Warning));
                    }

                    slotValue = emitter.Constant(DefaultFor(node, port, spec));
                }

                // An Any port takes whatever arrives; typed ports coerce.
                inputs[port] = spec.Kind == PortKind.Any ? slotValue : emitter.Coerce(slotValue, spec.Width);
            }

            // Held to what can actually be played on the way in, so the emit
            // never has to defend itself against a zero length or a volume out
            // of range — a hand-edited file is the only way either arrives.
            var steps = node.Steps is { Count: > 0 } notes
                ? notes.Select(s => s.Sane()).ToArray()
                : [];

            var outputsOfNode = def.Emit(emitter, new EmitContext(inputs, steps));
            visiting.Remove(node.Id);
            return resolved[node.Id] = outputsOfNode;
        }

        // The value on one of a node's inputs: whatever is wired in, or the knob
        // it rests on. Narrower than the loop inside Resolve — no normalling, no
        // sink port range, no complaint about a domain — because its only caller
        // is a cycle breaker, whose single socket is none of those things.
        Slot ResolveInput(NodeInstance node, NodeDef def, int port)
        {
            var spec = def.Inputs[port];
            var incoming = patch.IncomingTo(node.Id, port);

            var slotValue = incoming is not null && patch.Find(incoming.SourceNode) is { } source
                ? Pick(Resolve(source), incoming.SourcePort)
                : emitter.Constant(DefaultFor(node, port, spec));

            return spec.Kind == PortKind.Any ? slotValue : emitter.Coerce(slotValue, spec.Width);
        }

        // Which of a node's results a wire carries, and silence for a socket that
        // is not there — a saved patch outliving a change to the module it names.
        Slot Pick(Slot[] outputs, int port) =>
            port >= 0 && port < outputs.Length ? outputs[port] : emitter.Constant(0f);
    }

    /// <summary>
    /// The knob value for an unconnected input, falling back to the definition's
    /// default when a saved patch predates a change to the module's sockets.
    /// </summary>
    private static float DefaultFor(NodeInstance node, int port, PortSpec spec) =>
        port < node.InputValues.Length ? node.InputValues[port] : spec.Default;
}
