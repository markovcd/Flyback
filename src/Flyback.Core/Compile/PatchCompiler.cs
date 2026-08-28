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
    /// <summary>Compiles the program the screen shows, reading the Output's color.</summary>
    public static CompileResult CompileForVideo(
        this Patch patch,
        ModuleCatalog? modules = null,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null) =>
        Compile(patch, NodeCatalog.Screen, modules, samples: samples, pictures: pictures);

    /// <summary>Compiles the program the speakers play, reading the Output's left and right.</summary>
    public static CompileResult CompileForAudio(
        this Patch patch,
        ModuleCatalog? modules = null,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null) =>
        Compile(patch, NodeCatalog.Speakers, modules, samples: samples, pictures: pictures);

    /// <summary>
    /// Compiles the program a Probe shows, rooted at the probe rather than at the
    /// Output. What the screen would have shown is not merely covered up: the
    /// Output is never visited, so nothing upstream of it is lowered and the
    /// picture the patch makes costs nothing while its chart is up.
    /// </summary>
    /// <param name="patch"></param>
    /// <param name="probe">
    /// Which module to root at. A node that is not in the patch — a selection
    /// outliving the module it named — compiles the ordinary picture instead,
    /// because the alternative is a black screen with nothing to say why.
    /// </param>
    /// <param name="modules"></param>
    public static CompileResult CompileForProbe(
        this Patch patch,
        Guid probe,
        ModuleCatalog? modules = null,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null) =>
        Compile(patch, NodeCatalog.Screen, modules, probe, samples, pictures);

    /// <param name="patch">The graph to lower.</param>
    /// <param name="sink">Which of the Output's results this program reads.</param>
    /// <param name="modules">
    /// Which catalogue the type ids mean, defaulting to the installed one. Named
    /// explicitly, a patch can be compiled against a catalogue that is not the
    /// running program's — which is what makes a plugin's modules testable.
    /// </param>
    /// <param name="probe">
    /// A module to root at in place of the Output, or null for the sink itself.
    /// Everything below reads this as "is this a probe compilation", because a
    /// rooted-elsewhere program has no sink in it: the port range that splits the
    /// screen from the speakers means nothing, and what a missing wire costs is
    /// said about a different node.
    /// </param>
    private static CompileResult Compile(
        Patch patch,
        NodeCatalog.SinkKind sink,
        ModuleCatalog? modules,
        Guid? probe = null,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null)
    {
        var catalog = modules ?? NodeCatalog.Current;
        var width = sink.Width;

        var issues = new List<CompileIssue>();

        // What every Scope in the patch contributes, which is opposite things to
        // the two programs — a tap to the one that plays, a buffer to the one
        // that draws. See TapSpec.
        var taps = new List<TapSpec>();

        // The probe first, and forgotten again when the patch no longer holds
        // it: a stale id falls back to the picture rather than to nothing, and
        // everything below reads 'probe' as "is this rooted somewhere other than
        // the sink". From here the two compile the same way, and only what the
        // walk starts at differs.
        var probed = probe is { } id ? patch.Find(id) : null;
        probe = probed?.Id;

        // Whether this is the program that is actually heard. Only that one taps
        // a Scope, because a trace is a record of evaluations in order and the
        // picture's are neither in order nor the sound. A chart rooted at a
        // Probe is a picture like any other.
        var plays = probe is null && sink.Name == NodeCatalog.Speakers.Name;

        var root = probed ?? patch.FirstOf(NodeCatalog.OutputTypeId);

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
        // one flat color and silence. That is a legal program and not a patch,
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
                probe is null
                    ? "Nothing is wired into the Output, so there is nothing to see or hear. "
                      + "Patch something into its 'color' or its 'left'."
                    : "Nothing is wired into the Probe, so it is charting its own knob. "
                      + "Patch the output you want to look at into its 'in'.",
                IssueSeverity.Warning));
        }

        var emitter = new Emitter();
        var resolved = new Dictionary<Guid, Slot[]>();

        // The hidden modules, one instance each however many sockets are
        // normalled to them — see PortSpec.NormalledTo. Keyed by type id because
        // that is all a normal names and all it needs to: there is no node here
        // to tell one from another, which is exactly what "shared" means.
        //
        // Swapped with 'resolved' inside a sweep, and for the same reason: a
        // clock read under a Probe's substituted domain is a different value
        // from the one read outside it.
        var normals = new Dictionary<string, Slot[]>();

        var visiting = new HashSet<Guid>();
        var loops = new Queue<(NodeInstance Node, NodeDef Def, int Slot)>();

        // Only this program's share of the sink's results. Everything upstream
        // of the other half is never resolved, so it emits no ops.
        //
        // A probe is not a sink and its results are not split between two of
        // them, so it contributes its first output and nothing else — and a
        // module with no outputs at all, which only a hand-passed id could root
        // this at, contributes silence rather than throwing.
        var outputs = Resolve(root);

        Slot[] result;
        if (probe is null) result = outputs[sink.Results];
        else if (outputs.Length > 0) result = [outputs[0]];
        else result = [emitter.Constant(0f)];

        // And then the roots the sink does not reach. A Scope's whole use is a
        // side effect, so nothing downstream depends on it and the walk above
        // would never have visited what it is looking at — this is the one place
        // the program is deliberately made larger than what it computes, and it
        // is only the program the speakers run, because that is the only one
        // whose evaluations happen in order and are the sound.
        //
        // After the sink and before the loops drain, so that a tap whose input
        // reaches a cycle breaker still has its read emitted ahead of every
        // write.
        if (plays)
        {
            foreach (var node in patch.Nodes)
            {
                if (catalog.Get(node.TypeId) is not { TapsSignal: true } def) continue;
                if (def.Inputs.Count == 0) continue;

                emitter.Tap(taps.Count, ResolveInput(node, def, 0));
                taps.Add(new TapSpec(node.Id, WindowOf(node, def), Traces.Silence));
            }
        }

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
            new CompiledPatch(
                emitter.ToProgram(),
                emitter.RegisterCount,
                value.Base,
                width,
                emitter.Tables,
                taps,
                emitter.LiveInputs,
                emitter.Pictures),
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
                    $"'{node.Title(def)}' feeds back into itself. Put a Unit Delay somewhere in the loop "
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
            // Nothing to split when the root is a probe: the Output is not in
            // this program at all, and the module that is has no half the
            // speakers might want.
            var (firstPort, portCount) = probe is null && node.Id == root.Id
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

                // A swept input is lowered by the module rather than for it, so
                // that whatever the module does to the domain first is in force
                // by the time anything upstream reads one. It rests on its knob
                // until then, which is what a module that never asks gets.
                if (spec.Swept)
                {
                    inputs[port] = emitter.Constant(DefaultFor(node, port, spec));
                    continue;
                }

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
                else if (spec.NormalledTo is { } bus && Hidden(bus) is { } carried)
                {
                    // The other kind of normalled jack: not an earlier socket of
                    // this module, but a module that is not in the patch at all.
                    // An oscillator left alone is reading the clock, which is
                    // what an oscillator in a rack does with nothing plugged in.
                    slotValue = carried;
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
                    //
                    // Every domain in the catalogue is normalled to Time, so
                    // reaching here at all means a domain that is normalled to
                    // nothing, or to a module this catalogue does not hold — a
                    // plugin's port, or a patch opened without the plugin that
                    // wrote it. The complaint means there what it always meant.
                    if (spec.Domain)
                    {
                        issues.Add(new CompileIssue(
                            node.Id,
                            $"Nothing is wired into {node.Title(def)}'s '{spec.Name}', so it never moves. "
                            + "Patch Time in to hear it, or Coordinates to draw with it.",
                            IssueSeverity.Warning));
                    }

                    slotValue = emitter.Constant(DefaultFor(node, port, spec));
                }

                // An Any port takes whatever arrives; typed ports coerce.
                inputs[port] = spec.Kind == PortKind.Any ? slotValue : emitter.Coerce(slotValue, spec.Width);
            }

            // Whatever this module carries that is not a knob, each kind reading
            // and tidying its own — see NodeExtra. A module with none, which is
            // nearly all of them, folds nothing and pays nothing.
            var context = Carried(
                new EmitContext(inputs, [])
                {
                    Node = node.Id,
                    Trace = Watched(node, def),
                    Resolver = port => Sweep(node, def, port),
                },
                node,
                def);

            var outputsOfNode = def.Emit(emitter, context);

            visiting.Remove(node.Id);
            return resolved[node.Id] = outputsOfNode;
        }

        // A swept input, lowered where the module asked for it rather than
        // before the module was entered — see PortSpec.Swept.
        //
        // Under its own cache, because by now the emitter is likely reading a
        // substituted domain: a module resolved in here is being read at a
        // different (x, y, t) from the same module resolved outside, and two
        // readings of one node are two values however much they share a graph.
        // Sharing a register between them would chart the wrong moment. Nothing
        // else is scoped — a cycle detected in here is still a cycle, and a
        // breaker found in here still writes its cell after every read in the
        // whole program.
        Slot Sweep(NodeInstance node, NodeDef def, int port)
        {
            var outer = resolved;
            resolved = [];

            var outerNormals = normals;
            normals = [];

            try
            {
                return ResolveInput(node, def, port);
            }
            finally
            {
                resolved = outer;
                normals = outerNormals;
            }
        }

        // What a node carries that is not a knob, read onto the context the
        // module is about to be handed. Each kind knows its own field, its own
        // tidying and its own complaints — see NodeExtra — so what is left here
        // is lending them the three things a node cannot tell them: what it is
        // called, where a file is found, and where a complaint goes.
        //
        // A clip is resolved for whichever sink asked, the eye as well as the
        // ear. That was not so at first: the screen was given nothing, on the
        // grounds that a shader cannot read a clip and two backends showing
        // different pictures is worse than neither showing one. What that
        // overlooked is the Probe, which is a video program (ADR-0040) — so
        // looking at a sample charted a flat line, and the one tool for seeing
        // what a signal does could not see the one signal that comes from
        // outside the patch.
        //
        // The backends are kept in step somewhere better instead: a program that
        // reads a clip is drawn on the processor, because the shader cannot draw
        // it. Only a Sample the screen actually reaches puts a table in the
        // video program, so nothing else in the catalogue pays for it.
        EmitContext Carried(EmitContext ctx, NodeInstance node, NodeDef def)
        {
            if (def.Extras.Count == 0) return ctx;

            // The picture library only where the picture is being drawn. An Image
            // in an audio program is one evaluation of a still — a colour that
            // never moves and that nothing can hear — so the speakers' walk is
            // handed nothing to read a file with, and the module lowers to black
            // without anybody having to open one. It is the same arrangement the
            // Scope's buffer has, upside down.
            var env = new ExtraEnv(
                node.Title(def), samples, issues.Add, plays ? null : pictures);

            foreach (var extra in def.Extras) ctx = extra.Fold(ctx, node, env);

            return ctx;
        }

        // The buffer a Scope charts, and null for every module that charts
        // nothing and for the program that does the playing rather than the
        // drawing.
        //
        // Made here, as the walk reaches the module, rather than for every Scope
        // in the patch: one the picture never arrives at draws no chart, so a
        // buffer for it would be refilled sixty times a second for nobody. The
        // speakers' side is the other way round and deliberately so — there
        // every Scope is visited whether or not anything reaches it, because
        // being reached is precisely what it does not need.
        LoadedSample? Watched(NodeInstance node, NodeDef def)
        {
            // Charting rather than tapping, because the two are no longer the
            // same question — see NodeDef.ChartsSignal. A module that measures
            // what it taps wants no buffer here and no refill, and asking about
            // the tap would have given it both.
            if (!def.ChartsSignal || plays || def.Inputs.Count == 0) return null;

            var buffer = Traces.Buffer();
            taps.Add(new TapSpec(node.Id, WindowOf(node, def), buffer));

            return buffer;
        }

        // The one hidden instance of a module sockets are normalled to, emitted
        // the first time something asks for it — see PortSpec.NormalledTo. Null
        // where the catalogue does not hold it, or holds it with no such output,
        // which drops the socket back to its knob rather than to silence.
        //
        // Its own inputs are the definition's defaults and nothing else. There is
        // no node for anybody to have turned a knob on, and resolving them as
        // sockets would be the one way this could recurse: a module normalled to
        // a module normalled back to it has no instance to detect a cycle
        // through.
        Slot? Hidden(PortNormal bus)
        {
            if (!normals.TryGetValue(bus.TypeId, out var outputs))
            {
                if (catalog.Get(bus.TypeId) is not { } def) return null;

                var knobs = new Slot[def.Inputs.Count];

                for (var i = 0; i < knobs.Length; i++)
                    knobs[i] = emitter.Coerce(emitter.Constant(def.Inputs[i].Default), def.Inputs[i].Width);

                // Its extras come from a scratch instance seeded the way a freshly
                // placed one is, rather than from a second reading of the
                // definition's defaults. One path, so a hidden module carries
                // what a placed one would — which it did not before: a normal
                // pointing at a module that reads a file got no clip at all,
                // because nothing here went looking for one.
                var scratch = NodeInstance.Create(def, 0d, 0d);

                outputs = normals[bus.TypeId] = def.Emit(
                    emitter,
                    Carried(new EmitContext(knobs, []), scratch, def));
            }

            return bus.Port >= 0 && bus.Port < outputs.Length ? outputs[bus.Port] : null;
        }

        // The value on one of a node's inputs: whatever is wired in, whatever it
        // is normalled to, or the knob it rests on. Narrower than the loop inside
        // Resolve — no normalling from an earlier socket, no sink port range, no
        // complaint about a domain — because its callers are a cycle breaker,
        // whose single socket is none of those things, and a swept input, which
        // is none of them either.
        //
        // A normal is honoured here because it is the only one of those three a
        // caller could sensibly declare, and a socket that read its module in one
        // place and its knob in the other would be a difference nothing states.
        Slot ResolveInput(NodeInstance node, NodeDef def, int port)
        {
            var spec = def.Inputs[port];
            var incoming = patch.IncomingTo(node.Id, port);
            Slot slotValue;

            if (incoming is not null && patch.Find(incoming.SourceNode) is { } source)
                slotValue = Pick(Resolve(source), incoming.SourcePort);
            else if (spec.NormalledTo is { } bus && Hidden(bus) is { } carried)
                slotValue = carried;
            else
                slotValue = emitter.Constant(DefaultFor(node, port, spec));

            return spec.Kind == PortKind.Any ? slotValue : emitter.Coerce(slotValue, spec.Width);
        }

        // Which of a node's results a wire carries, and silence for a socket that
        // is not there — a saved patch outliving a change to the module it names.
        Slot Pick(Slot[] outputs, int port) =>
            port >= 0 && port < outputs.Length ? outputs[port] : emitter.Constant(0f);
    }

    /// <summary>
    /// How much of the past a Scope is asking for, in seconds: its first
    /// duration knob, which is marked in decades like every other one.
    /// </summary>
    /// <remarks>
    /// Found by <see cref="PortDisplay.Duration"/> rather than by position, so
    /// that a plugin's own Scope declares its window by saying what the socket
    /// is instead of by counting sockets. A module with none asks for a
    /// fiftieth of a second, which is a few cycles of anything audible.
    /// <para>
    /// The knob and not the wire. What acts on this is the refill, which runs
    /// once a frame and outside the program entirely — a value arriving per
    /// sample is not something it could do anything with, and the same is true
    /// of the sweep the Output reads off its knobs for the same reason.
    /// </para>
    /// </remarks>
    private static float WindowOf(NodeInstance node, NodeDef def)
    {
        for (var port = 0; port < def.Inputs.Count; port++)
        {
            if (def.Inputs[port].Display != PortDisplay.Duration) continue;

            var decades = DefaultFor(node, port, def.Inputs[port]);
            if (!float.IsFinite(decades)) break;

            return Math.Clamp(MathF.Pow(10f, decades), 0.0001f, 2f);
        }

        return 0.02f;
    }

    /// <summary>
    /// The knob value for an unconnected input, falling back to the definition's
    /// default when a saved patch predates a change to the module's sockets.
    /// </summary>
    private static float DefaultFor(NodeInstance node, int port, PortSpec spec) =>
        port < node.InputValues.Length ? node.InputValues[port] : spec.Default;
}
