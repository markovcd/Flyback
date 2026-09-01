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
        private readonly List<string> statements = [];
        private readonly HashSet<Guid> done = [];
        private readonly Dictionary<Guid, int> where = [];

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
                        statements.Add($"{From(wire)} |> out.{name}");
                        continue;
                    }

                    if (Knob(sink, def, port) is { } value) statements.Add($"out.{name} = {value}");
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

            return text.ToString();
        }

        /// <summary>
        /// The statements as they should be read: what a name needs before the
        /// name is used, and the pipelines that end at the Output last.
        /// </summary>
        private IEnumerable<string> Ordered()
        {
            // Bindings come out in the order they were settled, and the
            // Output's own lines were queued before any of them — so those are
            // moved to the end, where a reader expects the point of the patch.
            var sinks = statements.Where(s => s.Contains("|> out.") || s.StartsWith("out.", StringComparison.Ordinal));
            var rest = statements.Where(s => !s.Contains("|> out.") && !s.StartsWith("out.", StringComparison.Ordinal));

            return [.. rest, string.Empty, .. sinks];
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
                Place($"let {plan.Names[id]} = {Short(def)}()");
                return;
            }

            var text = Call(node, def);

            Place($"let {plan.Names[id]} = {text}");
        }

        private void Place(string statement)
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
                    if (patch.IncomingTo(node.Id, port) is { } wire)
                        statements.Add($"{plan.Names[node.Id]}.{def.Inputs[port].Name.Replace(' ', '_')} <- {From(wire)}");
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

        private static string Value(float value, PortDisplay display) => display switch
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

            for (var digits = 0; digits <= 7; digits++)
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

            return Number(decades);
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
