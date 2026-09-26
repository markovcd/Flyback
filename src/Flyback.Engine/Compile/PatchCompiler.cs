using Flyback.Core.Graph;

namespace Flyback.Core.Compile;

/// <summary>
/// Walks back from a sink node and lowers everything it reaches into a flat op
/// list, so a patch costs only what reaches that sink. Rooted at a sink rather
/// than at "the output", so the screen and the speakers each get their own
/// program and a module only the ear reaches costs the eye nothing.
/// </summary>
public static class PatchCompiler
{
    /// <summary>Compiles the program the screen shows, reading the Output's color.</summary>
    public static CompileResult CompileForVideo(
        this Patch patch,
        ModuleCatalog? modules = null,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null,
        bool played = false) =>
        Compile(patch, NodeCatalog.Screen, modules, samples: samples, pictures: pictures, played: played);

    /// <summary>Compiles the program the speakers play, reading the Output's left and right.</summary>
    public static CompileResult CompileForAudio(
        this Patch patch,
        ModuleCatalog? modules = null,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null,
        bool played = false) =>
        Compile(patch, NodeCatalog.Speakers, modules, samples: samples, pictures: pictures, played: played);

    /// <summary>
    /// Compiles the program a Probe shows, rooted at the probe rather than at the
    /// Output — which is never visited, so the picture the patch makes costs
    /// nothing while its chart is up.
    /// </summary>
    /// <param name="patch"></param>
    /// <param name="probe">
    /// Which module to root at. A node that is not in the patch compiles the
    /// ordinary picture instead, rather than a black screen with nothing to say
    /// why.
    /// </param>
    /// <param name="modules"></param>
    /// <param name="samples"></param>
    /// <param name="pictures"></param>
    /// <param name="played">Whether the panel's knobs are read live — see <c>Compile</c>.</param>
    public static CompileResult CompileForProbe(
        this Patch patch,
        Guid probe,
        ModuleCatalog? modules = null,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null,
        bool played = false) =>
        Compile(patch, NodeCatalog.Screen, modules, probe, samples, pictures, played);

    /// <param name="patch">The graph to lower.</param>
    /// <param name="sink">Which of the Output's results this program reads.</param>
    /// <param name="modules">
    /// Which catalog the type ids mean, defaulting to the installed one. Naming
    /// another is what makes a plugin's modules testable.
    /// </param>
    /// <param name="probe">
    /// A module to root at in place of the Output, or null for the sink itself.
    /// Everything below reads this as "is this a probe compilation": such a
    /// program has no sink in it, so the port range that splits screen from
    /// speakers means nothing.
    /// </param>
    /// <param name="samples"></param>
    /// <param name="pictures"></param>
    /// <param name="played">
    /// Whether the panel's knobs may be turned while this runs. Only then does a
    /// linked socket read its knob live; otherwise where the knob rests is baked in,
    /// which is right for a file and for a renderer handed no live block.
    /// </param>
    private static CompileResult Compile(
        Patch patch,
        NodeCatalog.SinkKind sink,
        ModuleCatalog? modules,
        Guid? probe = null,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null,
        bool played = false)
    {
        var catalog = modules ?? NodeCatalog.Current;
        var width = sink.Width;

        var issues = new List<CompileIssue>();

        // Buses first, so everything below sees them as the wires they stand for.
        patch = Buses.Joined(patch, (node, message) => issues.Add(new CompileIssue(node, message, IssueSeverity.Warning)));

        // An Auto remap's knobs are fractions of whatever its wires reach, and plain
        // numbers where a wire reaches something with no range, which is said.
        var spans = AutoRemap.Resolve(patch, catalog);

        foreach (var (remap, span) in spans)
        {
            var title = patch.Find(remap)!.Title(catalog.Require(NodeCatalog.AutoRemapTypeId));

            if (span.InWhy is { } inWhy)
                issues.Add(new CompileIssue(remap, $"{title}'s 'in low' and 'in high' are plain numbers: {inWhy}.", IssueSeverity.Warning));

            if (span.OutWhy is { } outWhy)
                issues.Add(new CompileIssue(remap, $"{title}'s 'out low' and 'out high' are plain numbers: {outWhy}.", IssueSeverity.Warning));
        }

        foreach (var wire in patch.Connections)
            if (AutoRemap.Overflow(patch, wire, catalog) is { } overflow)
                issues.Add(new CompileIssue(wire.TargetNode, $"{overflow}. An Auto remap in the wire fits it.", IssueSeverity.Warning));

        // What every Scope in the patch contributes, which is opposite things to
        // the two programs — a tap to the one that plays, a buffer to the one
        // that draws. See TapSpec.
        var taps = new List<TapSpec>();

        // The probe first, and forgotten again when the patch no longer holds
        // it: a stale id falls back to the picture rather than to nothing. From
        // here the two compile the same way, and only the walk's root differs.
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

        // An Output with nothing wired into it compiles to one flat color and
        // silence — a legal program and not a patch.
        //
        // Said only when nothing reaches it. A patch wired for the eye and not
        // the ear is as deliberate as one wired the other way (ADR-0022).
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

        // Each node as lowered so far, with the x, y and t it read. A sweep reuses
        // one only where the domain it pushed left those registers alone: a clock
        // read under a Probe's substituted domain is a different value from one
        // read outside it, and a knob is the same knob.
        var resolved = new Dictionary<Guid, List<(Slot[] Outputs, DomainRead Read)>>();

        // The hidden modules, one instance each however many sockets are
        // normalled to them — see PortSpec.NormalledTo. Keyed by type id, which
        // is all a normal names.
        var normals = new Dictionary<string, List<(Slot[] Outputs, DomainRead Read)>>();

        var knobs = new Dictionary<(Guid Node, int Port), Slot>();

        var visiting = new HashSet<Guid>();

        // The wires that run backwards, and the plane each carries its value
        // round in. One per output rather than one per wire, so an output feeding
        // two loops is one delayed value read twice rather than two planes
        // holding the same number.
        var backwards = Cycles.Backwards(patch);
        var carried = new Dictionary<(Guid Node, int Port), int>();
        var loops = new Queue<(Connection Wire, int Slot)>();

        // Only this program's share of the sink's results; everything upstream of
        // the other half is never resolved. A probe is not a sink, so it
        // contributes its first output and nothing else — and a module with no
        // outputs contributes silence rather than throwing.
        var outputs = Resolve(root);

        Slot[] result;
        if (probe is null) result = outputs[sink.Results];
        else if (outputs.Length > 0) result = [outputs[0]];
        else result = [emitter.Constant(0f)];

        // And then the roots the sink does not reach. A Scope's whole use is a
        // side effect, so the walk above would never have visited what it charts.
        // Only the speakers' program is made larger this way, because only its
        // evaluations happen in order and are the sound.
        //
        // After the sink and before the loops drain, so a tap whose input reaches
        // a cycle breaker still has its read emitted ahead of every write.
        if (plays)
        {
            foreach (var node in patch.Nodes)
            {
                // A Scope switched off is a module out of the patch, and its tap
                // is the one thing nothing else would have declined for it: the
                // whole of its use is a side effect, so it is never reached.
                if (node.Off) continue;
                if (catalog.Get(node.TypeId) is not { TapsSignal: true } def) continue;
                if (def.Inputs.Count == 0) continue;

                emitter.Tap(taps.Count, ResolveInput(node, def, 0));
                taps.Add(new TapSpec(node.Id, WindowOf(node, def), Traces.Silence));
            }
        }

        // Now close whatever loops that walk found. Every read is emitted before
        // the first write, and that ordering is the whole of a cycle's latency: a
        // value handed to a plane cannot be seen until the next evaluation.
        //
        // Drained as a queue, because a breaker's own input may reach a breaker
        // the walk never touched, whose write has to land after every read too.
        var fills = new List<(int Slot, Slot Value)>();

        while (loops.Count > 0)
        {
            var (wire, slot) = loops.Dequeue();

            // Resolved here rather than where the read was, which is the whole of
            // the trick: by now the walk has left the loop, so following the wire
            // backwards cannot arrive at itself. It may well arrive at another
            // loop, which is why this is a queue.
            fills.Add((
                slot,
                patch.Find(wire.SourceNode) is { } from
                    ? Pick(Resolve(from), wire.SourcePort)
                    : emitter.Constant(0f)));
        }

        // Every write after every read, including the reads the resolving above
        // went on emitting. Written in one pass at the end rather than as each
        // value is worked out, so that a second loop reading the first loop's
        // plane still reads what the previous evaluation left there.
        foreach (var (slot, carries) in fills) emitter.PlaneWrite(slot, carries);

        var value = emitter.PackChannels(result, width);

        return new CompileResult(
            new CompiledPatch(
                emitter.ToProgram(value),
                emitter.RegisterCount,
                value.Base,
                width,
                emitter.Tables,
                taps,
                emitter.LiveInputs,
                emitter.Pictures,
                emitter.Owners),
            issues);

        Slot[] Resolve(NodeInstance node)
        {
            if (Earlier(resolved, node.Id) is { } cached) return cached;

            var def = catalog.Get(node.TypeId);
            if (def is null)
            {
                issues.Add(new CompileIssue(node.Id, $"Unknown module '{node.TypeId}'."));
                return Remember(resolved, node.Id, ([emitter.Constant(0f)], default));
            }

            if (!visiting.Add(node.Id))
            {
                // Unreachable through a patch: every loop has a wire running
                // backwards and that wire is read rather than followed. A program
                // whose wires disagree with Cycles.Backwards would arrive here,
                // and a message beats a stack overflow.
                issues.Add(new CompileIssue(node.Id,
                    $"'{node.Title(def)}' feeds back into itself through a wire that carries this "
                    + "evaluation rather than the one before."));
                return [.. def.Outputs.Select(_ => emitter.Constant(0f))];
            }

            var lowered = emitter.Reading(() => Lower(node, def));

            visiting.Remove(node.Id);
            return Remember(resolved, node.Id, lowered);
        }

        Slot[] Lower(NodeInstance node, NodeDef def)
        {
            var inputs = new Slot[def.Inputs.Count];

            // On the sink, only the sockets this program is rooted at; the rest
            // stand in as zero. The emit function still runs whole, so the other
            // half's ops survive as a multiply by nothing — the price of the sink
            // staying data like every other module (ADR-0008). Everything patched
            // into that half is still never visited, which is where the cost is.
            // Nothing to split when the root is a probe.
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
                    inputs[port] = Knob(node, port, spec);
                    continue;
                }

                // Lowered when the module asks, under the ordinary cache — see
                // NodeDef.AsksForItsInputs.
                if (def.AsksForItsInputs) continue;

                var incoming = Live(patch.IncomingTo(node.Id, port), out var delayed);
                Slot slotValue;

                if (incoming is not null && delayed)
                {
                    // The wire that closes a loop, which is read rather than
                    // followed: what it carries is the evaluation before, and
                    // following it would be this walk arriving at itself.
                    slotValue = Delayed(incoming);
                }
                else if (incoming is not null && patch.Find(incoming.SourceNode) is { } source)
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
                    // A domain port left on its knob is a constant, so the module
                    // read across it does not move: an oscillator holds one value,
                    // a sequencer holds one step. Both compile to something valid,
                    // which is the trouble — the patch is silent, or a flat field,
                    // with nothing to say why.
                    //
                    // Every domain in the catalog is normalled to Time, so
                    // reaching here means a domain normalled to nothing or to a
                    // module this catalog does not hold.
                    if (spec.Domain)
                    {
                        issues.Add(new CompileIssue(
                            node.Id,
                            $"Nothing is wired into {node.Title(def)}'s '{spec.Name}', so it never moves. "
                            + "Patch Time in to hear it, or Coordinates to draw with it.",
                            IssueSeverity.Warning));
                    }

                    slotValue = Knob(node, port, spec);
                }

                // An Any port takes whatever arrives; typed ports coerce.
                inputs[port] = spec.Kind == PortKind.Any ? slotValue : emitter.Coerce(slotValue, spec.Width);
            }

            // Whatever this module carries that is not a knob, each kind reading
            // and tidying its own — see NodeExtra. A module with none, which is
            // nearly all of them, folds nothing and pays nothing.
            var context = Carried(
                new EmitContext(inputs)
                {
                    Node = node.Id,
                    Trace = Watched(node, def),
                    Spans = spans.GetValueOrDefault(node.Id),
                    // A swept input is lowered where the module asks, under whatever it
                    // pushed, so the memo hands back only what reads none of it.
                    Resolver = def.AsksForItsInputs ? Asked(node, def) : port => ResolveInput(node, def, port),
                },
                node,
                def);

            // Every cell of memory a module claims is claimed from inside its emit
            // function, which has no idea which node it is running for. Saying so
            // here is what lets a recompile hand a module back its own phase — see
            // StateOwners. Saved and put back rather than set, because a swept
            // input resolves other modules from inside this call.
            var outerOwner = emitter.Owner;

            emitter.Owner = node.Id;

            var outputsOfNode = def.Emit(emitter, context);

            emitter.Owner = outerOwner;

            return outputsOfNode;
        }

        // What a node or a normal was lowered to already, where lowering it again
        // under the domain in force now would read the same registers.
        Slot[]? Earlier<TKey>(Dictionary<TKey, List<(Slot[] Outputs, DomainRead Read)>> memo, TKey key)
            where TKey : notnull
        {
            if (memo.TryGetValue(key, out var earlier))
                foreach (var (outputs, read) in earlier)
                    if (emitter.Reuses(read)) return outputs;

            return null;
        }

        Slot[] Remember<TKey>(
            Dictionary<TKey, List<(Slot[] Outputs, DomainRead Read)>> memo,
            TKey key,
            (Slot[] Outputs, DomainRead Read) lowered)
            where TKey : notnull
        {
            if (!memo.TryGetValue(key, out var earlier)) memo[key] = earlier = [];

            earlier.Add(lowered);
            return lowered.Outputs;
        }

        // The inputs of a module that lowers them itself, each the first time it is
        // asked for and the same register every time after.
        Func<int, Slot> Asked(NodeInstance node, NodeDef def)
        {
            var asked = new Dictionary<int, Slot>();

            return port => asked.TryGetValue(port, out var slot) ? slot : asked[port] = ResolveInput(node, def, port);
        }

        // What a node carries that is not a knob, read onto the context the module
        // is about to be handed. Each kind knows its own field and its own
        // complaints — see NodeExtra — so what is left here is lending them what a
        // node cannot: its name, where a file is found, and where a complaint goes.
        //
        // A clip is resolved for whichever sink asked, since a Probe is a video
        // program (ADR-0040) and has to chart what a Sample does. The backends
        // stay in step by drawing on the processor whenever a program reads a
        // clip, because the shader cannot.
        EmitContext Carried(EmitContext ctx, NodeInstance node, NodeDef def)
        {
            if (def.Extras.Count == 0) return ctx;

            // The picture library only where the picture is drawn. An Image in an
            // audio program is a color nothing can hear, so the speakers' walk is
            // handed nothing to read a file with and the module lowers to black.
            var env = new ExtraEnv(
                node.Title(def), samples, issues.Add, plays ? null : pictures);

            foreach (var extra in def.Extras) ctx = extra.Fold(ctx, node, env);

            return ctx;
        }

        // The buffer a Scope charts, and null for a module that charts nothing and
        // for the program that plays rather than draws.
        //
        // Made as the walk reaches the module rather than for every Scope in the
        // patch: one the picture never arrives at would be refilled sixty times a
        // second for nobody. The speakers' side is the other way round, because
        // being reached is precisely what a Scope does not need.
        LoadedSample? Watched(NodeInstance node, NodeDef def)
        {
            // Charting rather than tapping, because the two are no longer the
            // same question — see NodeDef.ChartsSignal. A module that measures
            // what it taps wants no buffer here and no refill, and asking about
            // the tap would have given it both.
            if (!def.ChartsSignal || plays || def.Inputs.Count == 0) return null;

            var buffer = Traces.Buffer();
            taps.Add(new TapSpec(node.Id, WindowOf(node, def), buffer, def.ChartsSpectrum));

            return buffer;
        }

        // The one hidden instance of a module sockets are normalled to, emitted the
        // first time something asks for it — see PortSpec.NormalledTo. Null where
        // the catalog does not hold it, which drops the socket back to its knob
        // rather than to silence.
        //
        // Its inputs are the definition's defaults: there is no node to have
        // turned a knob on, and resolving them as sockets is the one way this
        // could recurse, with no instance to detect the cycle through.
        Slot? Hidden(PortNormal bus)
        {
            if (Earlier(normals, bus.TypeId) is not { } outputs)
            {
                if (catalog.Get(bus.TypeId) is not { } def) return null;

                var knobs = new Slot[def.Inputs.Count];

                for (var i = 0; i < knobs.Length; i++)
                    knobs[i] = emitter.Coerce(emitter.Constant(def.Inputs[i].Default), def.Inputs[i].Width);

                // Its extras come from a scratch instance seeded the way a freshly
                // placed one is, so a hidden module carries what a placed one
                // would — including a clip, for a normal pointing at a module that
                var scratch = NodeInstance.Create(def, 0d, 0d);

                outputs = Remember(normals, bus.TypeId, emitter.Reading(() => def.Emit(
                    emitter,
                    Carried(new EmitContext(knobs), scratch, def))));
            }

            return bus.Port >= 0 && bus.Port < outputs.Length ? outputs[bus.Port] : null;
        }

        // The value on one of a node's inputs: whatever is wired in, whatever it
        // is normalled to, or the knob it rests on. Narrower than the loop inside
        // Resolve, because its callers are a cycle breaker and a swept input,
        // which are none of the cases that loop handles.
        //
        // A normal is honored here, since a socket that read its module in one
        // place and its knob in the other would be a difference nothing states.
        Slot ResolveInput(NodeInstance node, NodeDef def, int port)
        {
            var spec = def.Inputs[port];
            var incoming = Live(patch.IncomingTo(node.Id, port), out var delayed);
            Slot slotValue;

            if (incoming is not null && delayed)
                slotValue = Delayed(incoming);
            else if (incoming is not null && patch.Find(incoming.SourceNode) is { } source)
                slotValue = Pick(Resolve(source), incoming.SourcePort);
            else if (spec.NormalledTo is { } bus && Hidden(bus) is { } carried)
                slotValue = carried;
            else
                slotValue = Knob(node, port, spec);

            return spec.Kind == PortKind.Any ? slotValue : emitter.Coerce(slotValue, spec.Width);
        }

        // What an unwired socket rests on: its own knob, or the panel knob it
        // follows. Scaled here rather than by whoever turns the knob, because one
        // knob may drive several sockets over different ranges. One register per
        // socket, however many sweeps lower its module: a knob reads no place.
        Slot Knob(NodeInstance node, int port, PortSpec spec) =>
            knobs.TryGetValue((node.Id, port), out var slot) ? slot : knobs[(node.Id, port)] = Turned(node, port, spec);

        Slot Turned(NodeInstance node, int port, PortSpec spec)
        {
            if (ControlMap.Of(node, port) is not { } link)
                return emitter.Constant(DefaultFor(node, port, spec));

            if (patch.Control(link.Control) is not { } control)
            {
                var name = catalog.Get(node.TypeId) is { } def ? node.Title(def) : node.TypeId;

                issues.Add(new CompileIssue(
                    node.Id,
                    $"{name}'s '{spec.Name}' follows a knob this patch has no longer. "
                    + "It rests where it is until it is linked to another.",
                    IssueSeverity.Warning));

                return emitter.Constant(DefaultFor(node, port, spec));
            }

            // A stepped socket must not be handed 2.4 notes because a knob was.
            if (!played)
            {
                var resting = link.At(control.Value);

                return emitter.Constant(spec.Stepped ? MathF.Floor(resting + 0.5f) : resting);
            }

            var turned = emitter.Live(control.Key);
            var reading = link.Knee > 0f
                ? Tapered(turned, link)
                : emitter.Add(emitter.Mul(turned, link.Max - link.Min), link.Min);

            return spec.Stepped
                ? emitter.Unary(OpCode.Floor, emitter.Add(reading, 0.5f))
                : reading;
        }

        // ControlLink.At as ops: low + knee * (e^(travel * ln(1 + span / knee)) - 1),
        // with the travel turned round where the range is.
        Slot Tapered(Slot turned, ControlLink link)
        {
            var low = MathF.Min(link.Min, link.Max);
            var span = MathF.Abs(link.Max - link.Min);
            var travel = link.Max < link.Min ? emitter.Add(emitter.Mul(turned, -1f), 1f) : turned;
            var rise = emitter.Unary(OpCode.Exp, emitter.Mul(travel, MathF.Log(1f + span / link.Knee)));

            return emitter.Add(emitter.Mul(emitter.Add(rise, -1f), link.Knee), low);
        }

        // What a wire running backwards hands over: the plane its output left
        // behind last evaluation, rather than the value it is about to have. The
        // write is queued for the drain, which is what puts an evaluation between
        // the two — see ADR-0075.
        Slot Delayed(Connection wire)
        {
            var output = (wire.SourceNode, wire.SourcePort);

            if (carried.TryGetValue(output, out var already)) return emitter.PlaneRead(already);

            // Said here because nothing is claiming it from inside an emit
            // function: the plane belongs to the wire, and a wire is named by its
            // two ends — see Cycles.Owner, and ADR-0067 for why a name is needed
            // at all.
            var outer = emitter.Owner;

            emitter.Owner = Cycles.Owner(wire);

            var slot = emitter.AllocatePlaneSlot();

            emitter.Owner = outer;

            carried[output] = slot;
            loops.Enqueue((wire, slot));

            return emitter.PlaneRead(slot);
        }

        // The wire that is actually feeding a socket. One arriving from a module
        // that is off carries instead whatever is patched into that module — see
        // NodeDef.Through — however many off modules it passes through, and
        // nothing at all where the chain ends at a socket with no wire on it. A
        // socket handed nothing does what an unpatched socket does, so turning a
        // module off is pulling its wires out rather than sending silence down
        // them.
        Connection? Live(Connection? wire, out bool delayed)
        {
            HashSet<Guid>? through = null;

            delayed = false;

            while (wire is not null)
            {
                // A wire anywhere along the chain that runs backwards makes the
                // whole of it carry the evaluation before: a module switched off
                // inside a loop is still inside it, and the cut is where it was.
                delayed |= backwards.Contains(wire);

                if (patch.Find(wire.SourceNode) is not { Off: true } off) return wire;
                if (catalog.Get(off.TypeId) is not { } def) return null;

                // A ring of modules that are all off has nothing at the end to
                // arrive at, and the walk has to stop somewhere.
                if (!(through ??= []).Add(off.Id)) return null;

                var port = def.Through(wire.SourcePort);

                wire = port < 0 ? null : patch.IncomingTo(off.Id, port);
            }

            // Nothing arrives, so there is nothing for the cut to have delayed.
            delayed = false;
            return null;
        }

        // Which of a node's results a wire carries, and silence for a socket that
        // is not there — a saved patch outliving a change to the module it names.
        Slot Pick(Slot[] outputs, int port) =>
            port >= 0 && port < outputs.Length ? outputs[port] : emitter.Constant(0f);
    }

    /// <summary>
    /// How much of the past a Scope is asking for, in seconds: its first duration
    /// knob, which is marked in decades like every other one.
    /// </summary>
    /// <remarks>
    /// Found by <see cref="PortDisplay.Duration"/> rather than by position, so a
    /// plugin's own Scope declares its window by saying what the socket is. A
    /// module with none asks for a fiftieth of a second. The knob and not the
    /// wire: the refill runs once a frame and outside the program, and could do
    /// nothing with a value arriving per sample.
    /// </remarks>
    private static float WindowOf(NodeInstance node, NodeDef def)
    {
        for (var port = 0; port < def.Inputs.Count; port++)
        {
            if (def.Inputs[port].Display != PortDisplay.Duration) continue;

            var decades = DefaultFor(node, port, def.Inputs[port]);
            if (!float.IsFinite(decades)) break;

            // Bounded by what there is ring to answer with and by nothing else, so
            // a window turned all the way up compiles to the time it says. See
            // DelayState.MaxWindowSeconds, which is also what sizes the ring.
            return Math.Clamp(MathF.Pow(10f, decades), 0.0001f, DelayState.MaxWindowSeconds);
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
