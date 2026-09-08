using System.Globalization;
using System.Text;
using Flyback.Core.Graph;

namespace Flyback.Core.Language;

/// <summary>
/// A patch written back out as source. The lossy direction, deliberately.
/// </summary>
/// <remarks>
/// What this is for is reading: a patch somebody sent, a diff between two of
/// them, a model being shown what it is working on. It is not for round-tripping
/// a file — node ids are regenerated, canvas positions are re-laid by
/// <see cref="PatchLayout"/>, and a group's collapsed state does not survive.
/// <see cref="PatchIO"/> is what keeps a patch exactly.
/// <para>
/// What it does guarantee is that the text means the same instrument: printing a
/// patch and building it again produces the same program, opcode for opcode.
/// That is the property worth having and the one the tests hold it to.
/// </para>
/// </remarks>
public static class PatchPrinter
{
    /// <summary>What a node is worth to the reader, and how it is written.</summary>
    private sealed record Plan(Dictionary<Guid, string> Names, HashSet<Guid> Bound, Guid Coord, Guid Clock);

    /// <summary>
    /// One statement, and what has to be known about it to put it in a box.
    /// </summary>
    /// <param name="Owner">
    /// The module this declares, or <see cref="Guid.Empty"/> for a statement
    /// that declares none — the Output's own lines and the wires that run
    /// backwards.
    /// </param>
    /// <param name="Names">
    /// Which bindings it reaches for. What a group block does is move its
    /// members down beside each other, and this is what says whether anything
    /// between them would be left reaching for a name that had moved past it.
    /// </param>
    private sealed record Line(string Text, Guid Owner, IReadOnlySet<Guid> Names);

    /// <summary>
    /// The patch as source, against <paramref name="against"/> or the installed
    /// catalogue.
    /// </summary>
    /// <param name="called">
    /// What to call each module, where the caller has names of its own it needs
    /// the text to agree with. Supplying this also gives every module a binding
    /// rather than inlining what is only used once — the point of passing names
    /// in is that everything can be pointed at afterwards, and a module folded
    /// into the middle of a pipeline has nothing to point at.
    /// </param>
    public static string Print(
        Patch patch,
        ModuleCatalog? against = null,
        IReadOnlyDictionary<Guid, string>? called = null)
    {
        var modules = against ?? NodeCatalog.Current;
        var plan = Prepare(patch, modules, called);
        var state = new Writer(patch, modules, plan);

        return state.Run();
    }

    /// <summary>
    /// Decides which modules get a name of their own before anything is written.
    /// </summary>
    /// <remarks>
    /// A module is named when inlining it would not say the same thing: when
    /// more than one wire leaves it, when nothing does, when what leaves is an
    /// output other than the first — no expression can stand for a Sequencer's
    /// gate — or when somebody named it on the canvas, in which case the name is
    /// worth keeping whatever the shape.
    /// </remarks>
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
        //
        // And so is one in a box. A bare word is a reading rather than a
        // declaration, so a clock written as `t` is declared nowhere and can be
        // in nobody's group — where somebody has drawn a box round it, being
        // written out as the module it is costs a word and keeps the box.
        var coord = Bare(patch, NodeCatalog.CoordTypeId);
        var clock = Bare(patch, NodeCatalog.TimeTypeId);

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
                || Usable(node.Name)

                // A module in a box gets a name whatever its shape, because a
                // group's members are the modules declared inside its block —
                // and one folded into the middle of a pipeline is declared
                // wherever that pipeline is, which is not in the block.
                || Boxed(patch, node.Id);

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
    /// One knob, written the way the language writes one.
    /// </summary>
    /// <remarks>
    /// Here so that anything editing a source file writes a number the same way
    /// a printing does — a note as its name, a duration as the time it means, an
    /// integer without a point. A second spelling of the same value would be a
    /// diff on every knob somebody touched.
    /// </remarks>
    public static string Knob(float value, PortDisplay display) => Writer.Value(value, display);

    /// <summary>Whether a module is in a box on the canvas.</summary>
    private static bool Boxed(Patch patch, Guid id) =>
        patch.Groups?.Any(group => group.Members.Contains(id)) == true;

    /// <summary>
    /// The one module of its type that gets written as a bare word, or nothing
    /// where that word would cost a box.
    /// </summary>
    private static Guid Bare(Patch patch, string typeId)
    {
        var only = patch.Nodes.FirstOrDefault(node => node.TypeId == typeId)?.Id ?? Guid.Empty;

        return Boxed(patch, only) ? Guid.Empty : only;
    }

    /// <summary>
    /// What to call a module: the name somebody gave it, else the short name it
    /// is written by, else what the palette calls it.
    /// </summary>
    /// <remarks>
    /// The third is for the handful whose short name is a word the language
    /// wants back — <c>midi.in</c> shortens to <c>in</c>, and a binding called
    /// that reads as a socket everywhere it appears. Its label is "MIDI In",
    /// which makes a perfectly good <c>midi_in</c>.
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
        private readonly List<Line> statements = [];
        private readonly HashSet<Guid> done = [];

        /// <summary>
        /// The bindings each statement being written has named so far, one entry
        /// per statement still open.
        /// </summary>
        /// <remarks>
        /// A stack because writing one statement writes the statements it needs:
        /// a name reached for in the middle of a pipeline is written out before
        /// the pipeline is, and what it named is its own rather than the
        /// pipeline's. The entry on top is always the statement being written.
        /// </remarks>
        private readonly Stack<HashSet<Guid>> naming = new();

        public string Run()
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
                        Say(Guid.Empty, () => $"{From(wire)} |> out.{name}");
                        continue;
                    }

                    if (Knob(sink, def, port) is { } value)
                        Say(Guid.Empty, () => $"out.{name} = {value}");
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

            foreach (var statement in Ordered()) text.AppendLine(statement);

            // Where the modules go is worked out after the graph is built and
            // where the lines break is worked out after the text is written, by
            // a pass that knows nothing about how either was made — see
            // SourceLayout, and PatchLayout on the other side of it.
            return SourceLayout.Wrap(text.ToString());
        }

        /// <summary>
        /// The statements as they should be read: what a name needs before the
        /// name is used, the boxes drawn round what belongs in one, and the
        /// pipelines that end at the Output last.
        /// </summary>
        private IEnumerable<string> Ordered()
        {
            // Bindings come out in the order they were settled, and the
            // Output's own lines were queued before any of them — so those are
            // moved to the end, where a reader expects the point of the patch.
            var sinks = statements.Where(Sunk).Select(line => line.Text);
            var rest = Boxed([.. statements.Where(line => !Sunk(line))]);

            return [.. rest, string.Empty, .. sinks];
        }

        private static bool Sunk(Line line) =>
            line.Text.Contains("|> out.") || line.Text.StartsWith("out.", StringComparison.Ordinal);

        /// <summary>
        /// The bindings, with each group's members gathered into a block.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The reason this was not here before, and the shape of the answer. A
        /// binding is written where something first needs it, and a group's
        /// members are not generally next to each other in that order — so a
        /// block cannot simply be opened and closed around a run of them. What
        /// can be done is to move the members <em>down</em> to where the last of
        /// them stands. Every binding a member needs is already above it and
        /// stays above it, so the only thing a move can break is a statement in
        /// between that was reaching for one of them.
        /// </para>
        /// <para>
        /// So that is the one question asked, and a group that fails it is
        /// written out flat. A box is presentation and the instrument is not:
        /// losing one is a patch that reads worse, and moving a binding past
        /// something that needed it is a patch that does not read at all.
        /// </para>
        /// </remarks>
        private IEnumerable<string> Boxed(IReadOnlyList<Line> lines)
        {
            var boxes = (patch.Groups ?? [])
                .Where(group => Rows(lines, group).Count >= NodeGroup.Fewest)
                .ToList();

            // A group that cannot be made contiguous is written out flat and the
            // rest tried again. It terminates because there is one fewer group
            // every time round and no groups at all always sorts — and a box is
            // presentation, where an order that does not read is a patch that
            // does not build.
            List<IReadOnlyList<int>>? order;

            while ((order = Sorted(lines, boxes)) is null) boxes.Remove(Stuck(lines, boxes));

            var written = new List<string>();

            foreach (var unit in order)
            {
                if (Boxing(lines, boxes, unit[0]) is not { } box)
                {
                    written.AddRange(unit.Select(row => lines[row].Text));
                    continue;
                }

                written.Add($"group \"{Titled(box)}\" {{");
                written.AddRange(unit.Select(row => "  " + lines[row].Text));
                written.Add("}");
            }

            return written;
        }

        /// <summary>
        /// The statements in an order where every group's members stand
        /// together, or null where no such order exists.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the whole of what was hard about groups, and the reason the
        /// printer went without them. A binding is written where something first
        /// needs it, and that order scatters a group's members: Whole band's
        /// clock is four modules and half the patch reaches for one of them, so
        /// they end up spread through it. Moving them down beside each other
        /// leaves whatever was between them reaching for a name that has gone
        /// past.
        /// </para>
        /// <para>
        /// So the order is worked out again with each group counted as one
        /// thing. Every statement is a unit, a group is a unit of several, and a
        /// unit may be written once everything it names has been — which is an
        /// ordinary topological sort over units instead of over statements. Ties
        /// go to whichever unit was written first, so the result is as close to
        /// the emit order as the boxes allow.
        /// </para>
        /// <para>
        /// It fails only where two groups reach into each other, which is a
        /// shape no box on a canvas has any reason to be — but it is possible to
        /// draw, so it is answered rather than assumed away.
        /// </para>
        /// </remarks>
        private static List<IReadOnlyList<int>>? Sorted(IReadOnlyList<Line> lines, List<NodeGroup> boxes)
        {
            var units = Units(lines, boxes);
            var home = new Dictionary<int, int>();

            for (var unit = 0; unit < units.Count; unit++)
                foreach (var row in units[unit])
                    home[row] = unit;

            // Where each binding was declared, so a name can be traced to the
            // unit that has to come first.
            var declared = new Dictionary<Guid, int>();

            for (var row = 0; row < lines.Count; row++)
                if (lines[row].Owner != Guid.Empty)
                    declared[lines[row].Owner] = home[row];

            var needs = units.Select(_ => new HashSet<int>()).ToList();
            var feeds = units.Select(_ => new List<int>()).ToList();

            for (var row = 0; row < lines.Count; row++)
            {
                foreach (var name in lines[row].Names)
                {
                    if (!declared.TryGetValue(name, out var from) || from == home[row]) continue;
                    if (!needs[home[row]].Add(from)) continue;

                    feeds[from].Add(home[row]);
                }
            }

            var ready = new PriorityQueue<int, int>();

            for (var unit = 0; unit < units.Count; unit++)
                if (needs[unit].Count == 0)
                    ready.Enqueue(unit, units[unit][0]);

            var order = new List<IReadOnlyList<int>>();

            while (ready.TryDequeue(out var unit, out _))
            {
                order.Add(units[unit]);

                foreach (var next in feeds[unit])
                    if (needs[next].Remove(unit) && needs[next].Count == 0)
                        ready.Enqueue(next, units[next][0]);
            }

            return order.Count == units.Count ? order : null;
        }

        /// <summary>
        /// Every statement as a unit to be ordered: a group as one, and anything
        /// not in one on its own.
        /// </summary>
        private static List<IReadOnlyList<int>> Units(IReadOnlyList<Line> lines, List<NodeGroup> boxes)
        {
            var units = new List<IReadOnlyList<int>>();
            var taken = new HashSet<int>();

            foreach (var box in boxes)
            {
                var rows = Rows(lines, box);

                units.Add(rows);
                taken.UnionWith(rows);
            }

            for (var row = 0; row < lines.Count; row++)
                if (!taken.Contains(row))
                    units.Add([row]);

            // In the order they were written, so the sort's tie-break has
            // something meaningful to break on.
            units.Sort((a, b) => a[0].CompareTo(b[0]));

            return units;
        }

        /// <summary>
        /// A group that is part of why no order exists, for the caller to give
        /// up on and try again without.
        /// </summary>
        /// <remarks>
        /// The widest, which is the one most likely to be enclosing another —
        /// and where that guess is wrong the loop simply comes back for the next
        /// one. Something has to be dropped and no box is more important than
        /// another.
        /// </remarks>
        private static NodeGroup Stuck(IReadOnlyList<Line> lines, List<NodeGroup> boxes) =>
            boxes.MaxBy(box => Rows(lines, box) is { Count: > 0 } rows ? rows[^1] - rows[0] : 0)!;

        /// <summary>Which box a statement is written in, or null where it is in none.</summary>
        private static NodeGroup? Boxing(IReadOnlyList<Line> lines, List<NodeGroup> boxes, int row) =>
            lines[row].Owner == Guid.Empty
                ? null
                : boxes.FirstOrDefault(box => box.Members.Contains(lines[row].Owner));

        /// <summary>Which statements declare this group's members, in the order they were written.</summary>
        private static IReadOnlyList<int> Rows(IReadOnlyList<Line> lines, NodeGroup group)
        {
            var rows = new List<int>();

            for (var row = 0; row < lines.Count; row++)
                if (lines[row].Owner != Guid.Empty && group.Members.Contains(lines[row].Owner))
                    rows.Add(row);

            return rows;
        }

        /// <summary>
        /// What to call a box. Quotes are the one thing a name cannot carry
        /// through, since a quote is what ends one.
        /// </summary>
        private static string Titled(NodeGroup group) =>
            string.IsNullOrWhiteSpace(group.Name) ? "Group" : group.Name.Replace("\"", string.Empty);

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
                Say(id, () => $"let {plan.Names[id]} = {Short(def)}()");
                return;
            }

            Say(id, () => $"let {plan.Names[id]} = {Call(node, def)}");
        }

        /// <summary>
        /// Writes one statement, keeping the bindings it named.
        /// </summary>
        /// <remarks>
        /// The writing happens inside rather than before, because a statement
        /// writes the statements it needs as it goes — see
        /// <see cref="naming"/>. Whatever those write is theirs and lands ahead
        /// of this one, which is what puts the list in an order that reads.
        /// </remarks>
        private void Say(Guid owner, Func<string> write)
        {
            naming.Push([]);

            var text = write();

            statements.Add(new Line(text, owner, naming.Pop()));
        }

        /// <summary>Records that the statement being written reached for a binding.</summary>
        private void Note(Guid id)
        {
            if (naming.Count > 0) naming.Peek().Add(id);
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

                    var socket = def.Inputs[port].Name.Replace(' ', '_');

                    // Nobody's, though it names one: a back-wire is the second
                    // half of a module already declared, and putting it in that
                    // module's box would carry the whole loop in with it. It
                    // still names that module, so it has to be ordered after it.
                    Say(Guid.Empty, () =>
                    {
                        var carried = From(wire);

                        Note(node.Id);

                        return $"{plan.Names[node.Id]}.{socket} <- {carried}";
                    });
                }
            }
        }

        /// <summary>What a module is written as, with its pipe chosen to read back the same way.</summary>
        private string Call(NodeInstance node, NodeDef def)
        {
            var used = new HashSet<int>();
            string? piped = null;

            // The pipe is chosen so that the rule which reads it puts the signal
            // back where it came from: an 'in' first, then a position taken whole
            // off one module's leading pair.
            var signal = Port(def.Inputs, "in");

            if (signal >= 0 && patch.IncomingTo(node.Id, signal) is { } straight)
            {
                piped = From(straight);
                used.Add(signal);
            }
            else if (def.Inputs.Count >= 2
                && Named(def.Inputs[0], "x") && Named(def.Inputs[1], "y")
                && patch.IncomingTo(node.Id, 0) is { SourcePort: 0 } first
                && patch.IncomingTo(node.Id, 1) is { SourcePort: 1 } second
                && first.SourceNode == second.SourceNode
                && first.SourceNode != plan.Coord)
            {
                piped = Whole(first.SourceNode);
                used.Add(0);
                used.Add(1);
            }
            else if (signal < 0 && patch.IncomingTo(node.Id, 0) is { } leading)
            {
                // Otherwise the first socket, which is where the rule that reads
                // this puts a signal when there is no 'in' and no position. It is
                // what turns a patch into the chain it was built as, rather than
                // one expression nested inside another twenty deep.
                piped = From(leading);
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
                    arguments.Add($"{name}: {From(wire)}");
                    continue;
                }

                if (Knob(node, def, port) is { } value) arguments.Add($"{name}: {value}");
            }

            var text = $"{Short(def)}({string.Join(", ", arguments)})";

            if (Carried(node, def) is { } block) text += $" {block}";

            return piped is null ? text : $"{piped} |> {text}";
        }

        /// <summary>A whole module, for a pipe that carries a position on.</summary>
        private string Whole(Guid id)
        {
            if (plan.Bound.Contains(id))
            {
                Ensure(id);
                Note(id);

                return plan.Names[id];
            }

            return patch.Find(id) is { } node && modules.Get(node.TypeId) is { } def
                ? Call(node, def)
                : "0";
        }

        /// <summary>What a wire's far end is written as.</summary>
        private string From(Connection wire)
        {
            if (wire.SourceNode == plan.Coord)
            {
                return wire.SourcePort switch
                {
                    NodeCatalog.CoordXPort => "x",
                    NodeCatalog.CoordYPort => "y",
                    2 => "radius",
                    _ => "angle",
                };
            }

            if (wire.SourceNode == plan.Clock) return "t";

            if (patch.Find(wire.SourceNode) is not { } node || modules.Get(node.TypeId) is not { } def)
                return "0";

            if (plan.Bound.Contains(wire.SourceNode))
            {
                Ensure(wire.SourceNode);
                Note(wire.SourceNode);

                var name = plan.Names[wire.SourceNode];

                return wire.SourcePort == 0
                    ? name
                    : $"{name}.{def.Outputs[wire.SourcePort].Name.Replace(' ', '_')}";
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

        private string? Carried(NodeInstance node, NodeDef def)
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
