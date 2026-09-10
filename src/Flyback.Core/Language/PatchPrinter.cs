using System.Globalization;
using System.Text;
using Flyback.Core.Graph;

namespace Flyback.Core.Language;

/// <summary>A patch as text, and where in that text each of its modules stands.</summary>
/// <remarks>
/// The map is what makes a printing something to click about: it says which
/// module the words under a caret are. What it holds is positions, so a module
/// folded into the middle of a pipeline is called nothing and is pointed at all
/// the same.
/// </remarks>
/// <param name="Order">
/// The modules whose calls stand in the text, from the first word to the last,
/// so a printing a knob has been written into can be mapped again without
/// printing it afresh — see <see cref="PatchPrinter.Locate"/>.
/// </param>
public sealed record Printing(string Source, SourceMap Map, IReadOnlyList<Guid> Order);

/// <summary>
/// A patch written back out as source. The lossy direction, deliberately.
/// </summary>
/// <remarks>
/// For reading: a patch somebody sent, a diff, a model being shown what it is
/// working on. Not for round-tripping a file — ids are regenerated, positions
/// are re-laid by <see cref="PatchLayout"/>, and a group's collapsed state does
/// not survive; <see cref="PatchIO"/> is what keeps a patch exactly. What it
/// does guarantee is that the text means the same instrument: printing a patch
/// and building it again gives the same program, opcode for opcode.
/// </remarks>
public static class PatchPrinter
{
    /// <summary>What a node is worth to the reader, and how it is written.</summary>
    private sealed record Plan(Dictionary<Guid, string> Names, HashSet<Guid> Bound, Guid Coord, Guid Clock);

    /// <summary>One piece of written text, and the modules whose calls it contains.</summary>
    /// <remarks>
    /// In the order they are written, so they can be lined up afterwards with the
    /// calls a parser finds. Only the order has to survive, which is why nothing
    /// here counts characters: folding the long lines moves every offset.
    /// </remarks>
    private readonly record struct Part(string Text, IReadOnlyList<Guid> Calls)
    {
        /// <summary>Text that names no module — a bare word, or a blank line.</summary>
        public static Part Of(string text) => new(text, []);
    }

    /// <summary>
    /// The patch as source, against <paramref name="against"/> or the installed
    /// catalogue.
    /// </summary>
    /// <param name="called">
    /// What to call each module, where the caller has names the text must agree
    /// with. Supplying this also gives every module a binding rather than
    /// inlining what is used once, since a module folded into a pipeline has
    /// nothing to point at.
    /// </param>
    public static string Print(
        Patch patch,
        ModuleCatalog? against = null,
        IReadOnlyDictionary<Guid, string>? called = null) =>
        Written(patch, against, called).Source;

    /// <summary>The same, with where in the text each module ended up.</summary>
    public static Printing Written(
        Patch patch,
        ModuleCatalog? against = null,
        IReadOnlyDictionary<Guid, string>? called = null)
    {
        var modules = against ?? NodeCatalog.Current;
        var plan = Prepare(patch, modules, called);
        var state = new Writer(patch, modules, plan);
        var (source, order) = state.Run();

        return new Printing(source, Located(source, patch, modules, plan, order), order);
    }

    /// <summary>
    /// Where each module stands in a printing that has been written into since.
    /// </summary>
    /// <remarks>
    /// A knob turned in the panel moves every offset after it and leaves the
    /// calls where they were in the order, so the same list lines up against the
    /// edited text and the map is made again without replacing what somebody is
    /// reading. The count is the guard: text that has grown or lost a call is no
    /// longer this printing, and what comes back points at nothing.
    /// </remarks>
    public static SourceMap Locate(
        Patch patch,
        string source,
        IReadOnlyList<Guid> order,
        ModuleCatalog? against = null)
    {
        var modules = against ?? NodeCatalog.Current;

        return Located(source, patch, modules, Prepare(patch, modules, null), order);
    }

    /// <summary>
    /// How a knob is written, so a value put into a source file is spelled the
    /// way a printing spells one — a note by its name and a length of time by the
    /// time it means, since writing the raw figure would leave a diff on every
    /// value anybody touched.
    /// </summary>
    public static string Knob(float value, PortDisplay display) => Writer.Value(value, display);

    /// <summary>
    /// The tune or the scale a module carries, written as the block that says it
    /// — or null where the module carries neither.
    /// </summary>
    /// <remarks>
    /// Null also for an empty block, because a printing is for reading. A caller
    /// putting one back into a file somebody has open wants <c>[ ]</c> instead,
    /// since there it has to say what changed.
    /// </remarks>
    public static string? Carried(NodeInstance node, NodeDef def) => Writer.Carried(node, def);

    /// <summary>
    /// The file a module names rather than carries (ADR-0052), or null where it
    /// names none.
    /// </summary>
    /// <remarks>
    /// The path as it stands, quotes not included. Whether it can be written is
    /// the writer's question: there is no escape for a quote, so a path carrying
    /// one has no spelling here.
    /// </remarks>
    public static string? Held(NodeInstance node, NodeDef def) =>
        def.Extra<SampleExtra>() is not null ? SampleExtra.Of(node) ?? string.Empty
            : def.Extra<PictureExtra>() is not null ? PictureExtra.Of(node) ?? string.Empty
            : null;

    /// <summary>
    /// What a plugin's field is written as, or null where this build has no
    /// spelling for it.
    /// </summary>
    /// <remarks>
    /// A number on whatever scale the field reads on, a switch as one or nought,
    /// and a choice as the one string the language has, since what is stored is
    /// an id. A shape this build has never heard of is left alone rather than
    /// written wrongly.
    /// </remarks>
    public static string? Field(NodeInstance node, NodeExtra extra, ExtraField field)
    {
        var stored = node.StateOf(extra.Key)?[field.Key];

        return field switch
        {
            ExtraField.Number number => Writer.Value(number.Value(stored), number.Spec.Display),
            ExtraField.Toggle toggle => toggle.Value(stored) ? "1" : "0",
            ExtraField.Choice choice => Quotable(choice.Value(stored)),
            _ => null,
        };
    }

    /// <summary>Whether a field still holds what a fresh instance of the module would.</summary>
    private static bool Fresh(NodeInstance node, NodeExtra extra, ExtraField field)
    {
        var stored = node.StateOf(extra.Key)?[field.Key];

        return field switch
        {
            ExtraField.Number number => Math.Abs(number.Value(stored) - number.Spec.Default) < 1e-7f,
            ExtraField.Toggle toggle => toggle.Value(stored) == toggle.On,
            ExtraField.Choice choice => choice.Value(stored) == choice.Fallback,
            _ => true,
        };
    }

    /// <summary>
    /// A string as the language writes one, or null where it cannot be written.
    /// </summary>
    /// <remarks>
    /// A quote would end the string and there is no escape for one, so a value
    /// carrying one is refused rather than written unreadably.
    /// </remarks>
    private static string? Quotable(string value) => value.Contains('"') ? null : $"\"{value}\"";

    /// <summary>
    /// Where each module ended up in the text that was just written.
    /// </summary>
    /// <remarks>
    /// The text is read back to find out: the writer knows the order it wrote the
    /// calls in, the parser knows where they are, and a printing is exactly as
    /// many calls as the writer emitted — so lining the two up places every
    /// module, including those written with no name. It also means the folding
    /// pass can move whatever it likes. A printing that will not parse is a fault
    /// in the printer, and the honest answer is a map pointing at nothing.
    /// </remarks>
    private static SourceMap Located(
        string source,
        Patch patch,
        ModuleCatalog modules,
        Plan plan,
        IReadOnlyList<Guid> order)
    {
        var issues = new List<LanguageIssue>();
        var read = new Parser(Lexer.Statements(Lexer.Scan(source, issues)), issues).Parse();

        if (issues.Count > 0) return SourceMap.Empty;

        var written = new List<CallExpr>();
        var mentioned = new List<NameExpr>();
        var turns = new List<KnobStatement>();
        var principals = new List<CallExpr>();

        foreach (var statement in read) Gather(statement, written, mentioned, turns, principals);

        written.Sort((a, b) => a.Line == b.Line ? a.Column - b.Column : a.Line - b.Line);

        if (written.Count != order.Count) return SourceMap.Empty;

        var mentions = new List<(Site Where, Guid Node)>();
        var calls = new Dictionary<Guid, Site>();
        var values = new Dictionary<(Guid Node, string Name), Site?>();
        var placed = new Dictionary<CallExpr, Guid>();
        var sink = patch.Nodes.FirstOrDefault(n => NodeCatalog.IsSink(n.TypeId));

        for (var i = 0; i < written.Count; i++)
        {
            var call = written[i];
            var node = order[i];
            var site = new Site(call.Line, call.Column);

            placed[call] = node;
            calls[node] = site;
            mentions.Add((site, node));

            if (patch.Find(node) is not { } instance || modules.Get(instance.TypeId) is not { } def) continue;

            foreach (var argument in call.Arguments)
            {
                if (argument.Name is null) continue;

                // Under the name a caller will ask by, which is the socket's own
                // spelling or the field's key — not whichever of key and label
                // the text happened to use.
                if (Canonical(def, argument.Name) is not { } name) continue;

                values[(node, name)] = Wrote(argument.Value);
            }
        }

        // A binding's name and a bare mention of it are both the module, so both
        // are somewhere to click. The Output is named by the socket it is being
        // wired into, which is the only way a patch ever mentions it.
        var byName = plan.Names.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

        foreach (var name in mentioned)
        {
            if (name.Name == "out" && sink is not null) mentions.Add((new Site(name.Line, name.Column), sink.Id));
            else if (byName.TryGetValue(name.Name, out var node)) mentions.Add((new Site(name.Line, name.Column), node));
        }

        if (sink is not null && modules.Get(sink.TypeId) is { } shape)
        {
            foreach (var turn in turns.Where(t => t.Target.Name == "out" && t.Target.Port is not null))
                if (Canonical(shape, turn.Target.Port!) is { } name)
                    values[(sink.Id, name)] = Wrote(turn.Value);
        }

        var named = plan.Names.ToDictionary(pair => pair.Key, pair => pair.Value);

        if (sink is not null) named[sink.Id] = "out";

        return new SourceMap(
            source,
            mentions,
            calls,
            values,
            named,
            principals.Where(placed.ContainsKey).Select(call => placed[call]).ToHashSet());
    }

    /// <summary>Every call, name and knob one statement writes.</summary>
    private static void Gather(
        Statement statement,
        List<CallExpr> calls,
        List<NameExpr> names,
        List<KnobStatement> turns,
        List<CallExpr> principals)
    {
        switch (statement)
        {
            case LetStatement let:
                Inside(let.Value, calls, names);
                if (Principal(let.Value) is { } bound) principals.Add(bound);
                break;

            case PipelineStatement pipeline:
                Inside(pipeline.Value, calls, names);
                if (Principal(pipeline.Value) is { } ending) principals.Add(ending);
                break;

            case KnobStatement knob:
                turns.Add(knob);
                names.Add(knob.Target);
                Inside(knob.Value, calls, names);
                break;

            case BackWireStatement back:
                names.Add(back.Target);
                Inside(back.Value, calls, names);
                break;
        }
    }

    private static void Inside(Expr expr, List<CallExpr> calls, List<NameExpr> names)
    {
        switch (expr)
        {
            case CallExpr call:
                calls.Add(call);
                foreach (var argument in call.Arguments) Inside(argument.Value, calls, names);
                break;

            case PipeExpr pipe:
                Inside(pipe.Source, calls, names);
                Inside(pipe.Stage, calls, names);
                break;

            case NameExpr name:
                names.Add(name);
                break;
        }
    }

    /// <summary>
    /// The call a statement is about, which is the last stage of its pipeline —
    /// <c>let hum = t |&gt; sine(...) |&gt; gain(...)</c> is a Gain called hum.
    /// </summary>
    private static CallExpr? Principal(Expr expr) => expr switch
    {
        PipeExpr pipe => Principal(pipe.Stage) ?? Principal(pipe.Source),
        CallExpr call => call,
        _ => null,
    };

    /// <summary>
    /// Where a written value stands, or null where it is not one this can put
    /// another in place of.
    /// </summary>
    private static Site? Wrote(Expr expr) => expr switch
    {
        NumberExpr number => new Site(number.Line, number.Column),
        NegateExpr negate when negate.Value is NumberExpr => new Site(negate.Line, negate.Column),
        TextExpr text => new Site(text.Line, text.Column),
        _ => null,
    };

    /// <summary>
    /// What a name written in a call is properly called: the socket's own
    /// spelling, or the field's key. Null where the module has neither.
    /// </summary>
    private static string? Canonical(NodeDef def, string written)
    {
        foreach (var port in def.Inputs)
            if (string.Equals(port.Name.Replace(' ', '_'), written, StringComparison.OrdinalIgnoreCase))
                return port.Name.Replace(' ', '_');

        foreach (var extra in def.Extras)
            foreach (var field in extra.Fields)
                if (string.Equals(field.Key, written, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(field.Label, written, StringComparison.OrdinalIgnoreCase))
                {
                    return field.Key;
                }

        return null;
    }

    /// <summary>
    /// Decides which modules get a name of their own before anything is written:
    /// where more than one wire leaves, where none does, where what leaves is an
    /// output other than the first — no expression can stand for a Sequencer's
    /// gate — or where somebody named it on the canvas.
    /// </summary>
    private static Plan Prepare(
        Patch patch,
        ModuleCatalog modules,
        IReadOnlyDictionary<Guid, string>? called)
    {
        var bound = new HashSet<Guid>();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var names = new Dictionary<Guid, string>();

        // One Coordinates and one Time become the bare words the language has
        // for them. A second of either is an ordinary module, since only one can
        // be what 'x' means.
        var coord = patch.Nodes.FirstOrDefault(n => n.TypeId == NodeCatalog.CoordTypeId)?.Id ?? Guid.Empty;
        var clock = patch.Nodes.FirstOrDefault(n => n.TypeId == NodeCatalog.TimeTypeId)?.Id ?? Guid.Empty;

        foreach (var node in patch.Nodes)
        {
            if (node.Id == coord || node.Id == clock) continue;
            if (NodeCatalog.IsSink(node.TypeId)) continue;

            var leaving = patch.Connections.Where(c => c.SourceNode == node.Id).ToList();
            var def = modules.Get(node.TypeId);

            var must = called is not null
                || def is { IsCycleBreaker: true }
                || leaving.Count != 1
                || leaving.Any(c => c.SourcePort != 0)
                || Usable(node.Name);

            if (must) bound.Add(node.Id);
        }

        foreach (var node in patch.Nodes.Where(n => bound.Contains(n.Id)))
        {
            var wanted = called is not null && called.TryGetValue(node.Id, out var given) && Usable(given)
                ? given
                : Wanted(node, modules);

            names[node.Id] = Unique(wanted, taken);
        }

        return new Plan(names, bound, coord, clock);
    }

    /// <summary>
    /// What to call a module: the name somebody gave it, else the short name it
    /// is written by, else what the palette calls it.
    /// </summary>
    /// <remarks>
    /// The third is for the handful whose short name is a word the language wants
    /// back — <c>midi.in</c> shortens to <c>in</c>, and a binding called that
    /// reads as a socket. Its label makes a perfectly good <c>midi_in</c>.
    /// </remarks>
    private static string Wanted(NodeInstance node, ModuleCatalog modules)
    {
        if (Usable(node.Name)) return node.Name!;

        var dot = node.TypeId.LastIndexOf('.');
        var stem = dot < 0 ? node.TypeId : node.TypeId[(dot + 1)..];

        if (Usable(stem)) return stem;

        if (modules.Get(node.TypeId) is { } def)
        {
            var label = new string([.. def.Name.ToLowerInvariant()
                .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_')]);

            if (Usable(label)) return label;
        }

        return "node";
    }

    private static string Unique(string wanted, HashSet<string> taken)
    {
        if (taken.Add(wanted)) return wanted;

        for (var n = 2; ; n++)
        {
            var tried = wanted + n.ToString(CultureInfo.InvariantCulture);

            if (taken.Add(tried)) return tried;
        }
    }

    /// <summary>
    /// Whether a name may be written as one. A note is the sharp edge here: a
    /// module called <c>A3</c> would be read back as a pitch, so it is not a
    /// name this can use however good it looks on the canvas.
    /// </summary>
    private static bool Usable(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!char.IsAsciiLetter(name[0]) && name[0] != '_') return false;
        if (name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')) return false;
        if (Lexer.Note(name) is not null) return false;

        return name is not ("let" or "def" or "group" or "out" or "in" or "x" or "y" or "t" or "radius" or "angle");
    }

    /// <summary>
    /// Writing one patch. A class because the walk is emit-on-first-use: a name
    /// is written out at the point something first needs it, which is what puts
    /// the statements in an order that reads.
    /// </summary>
    private sealed class Writer(Patch patch, ModuleCatalog modules, Plan plan)
    {
        private readonly List<Part> statements = [];
        private readonly HashSet<Guid> done = [];
        private readonly Dictionary<Guid, int> where = [];

        /// <summary>
        /// The text, and the modules whose calls stand in it from the first word
        /// to the last.
        /// </summary>
        public (string Source, IReadOnlyList<Guid> Order) Run()
        {
            var sink = patch.Nodes.FirstOrDefault(n => NodeCatalog.IsSink(n.TypeId));
            var text = new StringBuilder();

            if (sink is not null && modules.Get(sink.TypeId) is { } def)
            {
                // The picture and the sound, in the order the block carries them.
                for (var port = 0; port < def.Inputs.Count; port++)
                {
                    var name = def.Inputs[port].Name.Replace(' ', '_');

                    if (patch.IncomingTo(sink.Id, port) is { } wire)
                    {
                        var from = From(wire);

                        statements.Add(new Part($"{from.Text} |> out.{name}", from.Calls));
                        continue;
                    }

                    if (Knob(sink, def, port) is { } value) statements.Add(Part.Of($"out.{name} = {value}"));
                }
            }

            // Anything the Output cannot reach is still in the patch and still
            // has to be said, or printing would quietly delete half of what
            // somebody built for the other sink.
            foreach (var node in patch.Nodes)
            {
                if (node.Id == plan.Coord || node.Id == plan.Clock) continue;
                if (NodeCatalog.IsSink(node.TypeId)) continue;

                Ensure(node.Id);
            }

            Cycles();

            var ordered = Ordered();

            foreach (var statement in ordered) text.AppendLine(statement.Text);

            // Where the modules go is worked out after the graph is built and
            // where the lines break is worked out after the text is written, by
            // a pass that knows nothing about how either was made — see
            // SourceLayout, and PatchLayout on the other side of it.
            return (SourceLayout.Wrap(text.ToString()), [.. ordered.SelectMany(part => part.Calls)]);
        }

        /// <summary>
        /// The statements as they should be read: what a name needs before the
        /// name is used, and the pipelines that end at the Output last.
        /// </summary>
        private List<Part> Ordered()
        {
            // Bindings come out in the order they were settled, and the
            // Output's own lines were queued before any of them — so those are
            // moved to the end, where a reader expects the point of the patch.
            var sinks = statements.Where(Sink).ToList();
            var rest = statements.Where(part => !Sink(part)).ToList();

            // The blank line between them goes in only where there is something
            // on both sides of it. A patch whose Output is fed by one expression
            // has no bindings at all, and would otherwise be printed starting on
            // an empty line.
            return rest.Count == 0 || sinks.Count == 0
                ? [.. rest, .. sinks]
                : [.. rest, Part.Of(string.Empty), .. sinks];

            static bool Sink(Part part) =>
                part.Text.Contains("|> out.") || part.Text.StartsWith("out.", StringComparison.Ordinal);
        }

        /// <summary>Writes a module's binding if it has not been written yet.</summary>
        private void Ensure(Guid id)
        {
            if (!plan.Bound.Contains(id) || !done.Add(id)) return;
            if (patch.Find(id) is not { } node || modules.Get(node.TypeId) is not { } def) return;

            // A cycle breaker is written before what feeds it, because what
            // feeds it runs backwards and is written as its own statement at the
            // end. Nothing else in the language can close a loop.
            if (def.IsCycleBreaker)
            {
                Place(new Part($"let {plan.Names[id]} = {Short(def)}()", [id]));
                return;
            }

            var written = Call(node, def);

            Place(new Part($"let {plan.Names[id]} = {written.Text}", written.Calls));
        }

        private void Place(Part statement)
        {
            statements.Add(statement);
            where[Guid.Empty] = statements.Count;
        }

        /// <summary>The wires that run backwards, which is what a loop is made of.</summary>
        private void Cycles()
        {
            foreach (var node in patch.Nodes)
            {
                if (modules.Get(node.TypeId) is not { IsCycleBreaker: true } def) continue;

                for (var port = 0; port < def.Inputs.Count; port++)
                {
                    if (patch.IncomingTo(node.Id, port) is not { } wire) continue;

                    var from = From(wire);
                    var name = def.Inputs[port].Name.Replace(' ', '_');

                    statements.Add(new Part($"{plan.Names[node.Id]}.{name} <- {from.Text}", from.Calls));
                }
            }
        }

        /// <summary>What a module is written as, with its pipe chosen to read back the same way.</summary>
        /// <remarks>
        /// The modules it names come back in the order the words do: whatever is
        /// piped in is written first, then this one, then its arguments.
        /// </remarks>
        private Part Call(NodeInstance node, NodeDef def)
        {
            var used = new HashSet<int>();
            var before = new List<Guid>();
            var after = new List<Guid>();
            string? piped = null;

            // The pipe is chosen so that the rule which reads it puts the signal
            // back where it came from: an 'in' first, then a position taken whole
            // off one module's leading pair.
            var signal = Port(def.Inputs, "in");

            if (signal >= 0 && patch.IncomingTo(node.Id, signal) is { } straight)
            {
                var part = From(straight);

                piped = part.Text;
                before.AddRange(part.Calls);
                used.Add(signal);
            }
            else if (def.Inputs.Count >= 2
                && Named(def.Inputs[0], "x") && Named(def.Inputs[1], "y")
                && patch.IncomingTo(node.Id, 0) is { SourcePort: 0 } first
                && patch.IncomingTo(node.Id, 1) is { SourcePort: 1 } second
                && first.SourceNode == second.SourceNode
                && first.SourceNode != plan.Coord)
            {
                var part = Whole(first.SourceNode);

                piped = part.Text;
                before.AddRange(part.Calls);
                used.Add(0);
                used.Add(1);
            }
            else if (signal < 0 && patch.IncomingTo(node.Id, 0) is { } leading)
            {
                // Otherwise the first socket, which is where the rule that reads
                // this puts a signal when there is no 'in' and no position. It is
                // what turns a patch into the chain it was built as, rather than
                // one expression nested inside another twenty deep.
                var part = From(leading);

                piped = part.Text;
                before.AddRange(part.Calls);
                used.Add(0);
            }

            var arguments = new List<string>();

            // The file a module names rather than carries (ADR-0052). It is not
            // a socket, so it goes in as the one thing the language writes in
            // quotes, and it goes first because that is where it reads.
            if (File(node, def) is { } path) arguments.Add($"\"{path}\"");

            for (var port = 0; port < def.Inputs.Count; port++)
            {
                if (used.Contains(port)) continue;

                var name = def.Inputs[port].Name.Replace(' ', '_');

                if (patch.IncomingTo(node.Id, port) is { } wire)
                {
                    var part = From(wire);

                    arguments.Add($"{name}: {part.Text}");
                    after.AddRange(part.Calls);
                    continue;
                }

                if (Knob(node, def, port) is { } value) arguments.Add($"{name}: {value}");
            }

            // A plugin's own fields, which are named arguments like any knob and
            // are addressed by key rather than by label — a label is free to be
            // reworded and a key is in every saved patch (ADR-0055). Last,
            // because a socket is what a reader is looking for.
            foreach (var extra in def.Extras)
                foreach (var field in extra.Fields)
                    if (!Fresh(node, extra, field) && Field(node, extra, field) is { } written)
                        arguments.Add($"{field.Key}: {written}");

            var text = $"{Short(def)}({string.Join(", ", arguments)})";

            if (Carried(node, def) is { } block) text += $" {block}";

            return new Part(
                piped is null ? text : $"{piped} |> {text}",
                [.. before, node.Id, .. after]);
        }

        /// <summary>A whole module, for a pipe that carries a position on.</summary>
        private Part Whole(Guid id)
        {
            if (plan.Bound.Contains(id))
            {
                Ensure(id);
                return Part.Of(plan.Names[id]);
            }

            return patch.Find(id) is { } node && modules.Get(node.TypeId) is { } def
                ? Call(node, def)
                : Part.Of("0");
        }

        /// <summary>What a wire's far end is written as.</summary>
        private Part From(Connection wire)
        {
            if (wire.SourceNode == plan.Coord)
            {
                return Part.Of(wire.SourcePort switch
                {
                    NodeCatalog.CoordXPort => "x",
                    NodeCatalog.CoordYPort => "y",
                    2 => "radius",
                    _ => "angle",
                });
            }

            if (wire.SourceNode == plan.Clock) return Part.Of("t");

            if (patch.Find(wire.SourceNode) is not { } node || modules.Get(node.TypeId) is not { } def)
                return Part.Of("0");

            if (plan.Bound.Contains(wire.SourceNode))
            {
                Ensure(wire.SourceNode);

                var name = plan.Names[wire.SourceNode];

                return Part.Of(wire.SourcePort == 0
                    ? name
                    : $"{name}.{def.Outputs[wire.SourcePort].Name.Replace(' ', '_')}");
            }

            return Call(node, def);
        }

        /// <summary>
        /// A knob, or null where there is nothing to say about it — a value that
        /// is already the default, or a socket that has no knob at all because
        /// something is normalled to it.
        /// </summary>
        private string? Knob(NodeInstance node, NodeDef def, int port)
        {
            if (port >= node.InputValues.Length) return null;

            var spec = def.Inputs[port];

            if (modules.Normalled(spec) is not null) return null;

            var value = node.InputValues[port];

            return Math.Abs(value - spec.Default) < 1e-7f ? null : Value(value, spec.Display);
        }

        /// <summary>The path a player or a picture names, or null where it names none.</summary>
        private static string? File(NodeInstance node, NodeDef def)
        {
            var path = def.Extra<SampleExtra>() is not null ? SampleExtra.Of(node)
                : def.Extra<PictureExtra>() is not null ? PictureExtra.Of(node)
                : string.Empty;

            // A quote would end the string and there is no escape for one, so a
            // path carrying one is left off rather than written unreadably. It
            // is not a filename anybody has.
            return string.IsNullOrEmpty(path) || path.Contains('"') ? null : path;
        }

        internal static string? Carried(NodeInstance node, NodeDef def)
        {
            if (def.Extra<StepsExtra>() is { } steps)
            {
                var written = StepsExtra.Of(node);

                if (written.Count == 0) return null;

                var note = steps.Spec.Display == PortDisplay.Note;

                return "[ " + string.Join(' ', written.Select(s => Step(s, note))) + " ]";
            }

            if (def.Extra<ScaleExtra>() is not null)
            {
                var scale = ScaleExtra.Of(node);

                return scale.Count == 0 ? null : "[ " + string.Join(' ', scale.Select(Pitch.ClassName)) + " ]";
            }

            return null;
        }

        private static string Step(Graph.Step step, bool note)
        {
            // A rest has no pitch to write. A note at no volume keeps its own,
            // and the two are different steps however alike they sound.
            var head = step.Volume <= 0f && step.Value == 0f
                ? "~"
                : note ? Pitch.Name(step.Value) : Number(step.Value);

            if (step.Volume < 1f && head != "~") head += "%" + Number(step.Volume);
            if (Math.Abs(step.Length - 1f) > 1e-6f) head += "@" + Number(step.Length);

            return head;
        }

        internal static string Value(float value, PortDisplay display) => display switch
        {
            PortDisplay.Note => Whole(value) ? Pitch.Name(value) : Number(value),
            PortDisplay.Duration => Seconds(value),
            PortDisplay.Integer => value.ToString("0", CultureInfo.InvariantCulture),
            _ => Number(value),
        };

        private static bool Whole(float value) => Math.Abs(value - MathF.Round(value)) < 1e-6f;

        /// <summary>
        /// A Duration knob written as the time it means, where saying it that way
        /// reads back as the same number.
        /// </summary>
        /// <remarks>
        /// The socket holds a power of ten, and the whole point of the literal is
        /// that nobody should have to. But not every decade is a round time, so
        /// the time is written and then checked: if reading it back does not land
        /// on the same knob, the decade is written instead and is exactly right.
        /// </remarks>
        private static string Seconds(float decades)
        {
            if (!float.IsFinite(decades)) return Number(decades);

            var seconds = Math.Pow(10d, decades);

            var (unit, name) = seconds switch
            {
                < 1e-3d => (1e-6d, "us"),
                < 1d => (1e-3d, "ms"),
                _ => (1d, "s"),
            };

            // Always with a unit on it, because a bare number on one of these
            // sockets is no longer a number the language will read — it was the
            // hundredfold mistake the literal exists to prevent. So the spelling
            // has to be found rather than fallen back from.
            for (var digits = 0; digits <= 17; digits++)
            {
                // Never "G": a hundred milliseconds comes back from that as
                // "1E+02", which is not a number this language has and would put
                // an unreadable line in the middle of an otherwise fine patch.
                var written = (seconds / unit).ToString(
                    digits == 0 ? "0" : "0." + new string('#', digits), CultureInfo.InvariantCulture);

                if (!double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out var back)) continue;

                // The same arithmetic the lexer will do on the way back, and the
                // same float it will land on. Near enough is not enough: this has
                // to be the knob, not a knob a thousandth away from it, or a patch
                // would drift a little every time it went through here.
                if ((float)Math.Log10(back * unit) == decades) return written + name;
            }

            return (seconds / unit).ToString("0.#################", CultureInfo.InvariantCulture) + name;
        }

        /// <summary>
        /// A number written so that reading it back lands on the same float.
        /// </summary>
        /// <remarks>
        /// Found rather than assumed. A fixed six decimal places turns the
        /// twelfth In key sets a knob to into 0.083333, which is a different
        /// number from a twelfth and compiles to a different constant — so the
        /// shortest spelling that survives the trip is the one written, and there
        /// always is one short of the fallback.
        /// </remarks>
        private static string Number(float value)
        {
            if (!float.IsFinite(value)) return "0";

            // Widened before it is written. A float formats to about seven
            // significant digits and no format string will get more out of it,
            // so a twelfth spells itself "0.08333334" however many places are
            // asked for — and that is a different float from a twelfth. The
            // double behind it has the digits.
            var exact = (double)value;

            for (var digits = 0; digits <= 12; digits++)
            {
                var written = exact.ToString(
                    digits == 0 ? "0" : "0." + new string('#', digits), CultureInfo.InvariantCulture);

                // Through a double and then narrowed, because that is the road
                // the number takes on the way back: the lexer reads a double and
                // the binder casts it to the knob.
                if (double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out var back)
                    && (float)back == value)
                {
                    return written;
                }
            }

            return exact.ToString("0.############", CultureInfo.InvariantCulture);
        }

        private static string Short(NodeDef def)
        {
            var dot = def.TypeId.LastIndexOf('.');
            var stem = dot < 0 ? def.TypeId : def.TypeId[(dot + 1)..];

            // Two of the ninety collide, and one more shortens to a word the
            // language uses for a socket. Written in full, all three are plain.
            return stem is "hsv" or "mix" or "in" ? def.TypeId : stem;
        }

        private static int Port(IReadOnlyList<PortSpec> ports, string name)
        {
            for (var i = 0; i < ports.Count; i++)
                if (Named(ports[i], name)) return i;

            return -1;
        }

        private static bool Named(PortSpec spec, string name) =>
            string.Equals(spec.Name, name, StringComparison.OrdinalIgnoreCase);
    }
}
