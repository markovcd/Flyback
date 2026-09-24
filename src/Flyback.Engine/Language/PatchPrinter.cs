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
    /// <param name="Taken">Every name given out, for one given while writing.</param>
    private sealed record Plan(Dictionary<Guid, string> Names, HashSet<Guid> Bound, HashSet<string> Taken, Guid Coord, Guid Clock);

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
    /// catalog.
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
    /// How the computer keyboard is laid out, as the line that says it — or null
    /// for the piano, which is what a patch that says nothing is.
    /// </summary>
    public static string? Keyboard(IReadOnlyList<int>? scale) =>
        scale is null
            ? null
            : scale.Count == 0
                ? "keyboard scale [ ]"
                : "keyboard scale [ " + string.Join(' ', scale.Select(Pitch.ClassName)) + " ]";

    /// <summary>
    /// What the patch is for, as the statement that says it, or null where it says
    /// nothing. Run on over as many strings as keep it to the page's width.
    /// </summary>
    public static string? Description(string? description)
    {
        if (Patch.Tidied(description) is not { } said) return null;

        const string opening = "description ";
        const string after = "  ";

        var lines = new List<string>();
        var line = new StringBuilder();

        foreach (var word in said.Split(' '))
        {
            var lead = lines.Count == 0 ? opening : after;

            if (line.Length > 0 && lead.Length + line.Length + word.Length + 3 > SourceLayout.Width)
            {
                lines.Add($"{lead}\"{line}\"");
                line.Clear();
            }

            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }

        lines.Add($"{(lines.Count == 0 ? opening : after)}\"{line}\"");

        return string.Join('\n', lines);
    }

    /// <summary>Who made the patch, as the statement that says it, or null where nobody is credited.</summary>
    public static string? Author(string? author) =>
        Patch.TidiedAuthor(author) is { } said ? $"author \"{said}\"" : null;

    /// <summary>The patch's tags, as the one line that says them, or null where it has none.</summary>
    public static string? Tags(IEnumerable<string>? tags) =>
        Patch.TidiedTags(tags) is { } kept ? "tags " + string.Join(' ', kept.Select(tag => $"\"{tag}\"")) : null;

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
    /// and a choice or text as the one string the language has, since what is
    /// stored is an id or what was typed. Text of several lines has a bar where a
    /// line breaks — see <see cref="LineBreak"/>. A shape this build has never
    /// heard of is left alone rather than written wrongly.
    /// </remarks>
    public static string? Field(NodeInstance node, NodeExtra extra, ExtraField field)
    {
        var stored = node.StateOf(extra.Key)?[field.Key];

        return field switch
        {
            ExtraField.Number number => Writer.Value(number.Value(stored), number.Spec.Display),
            ExtraField.Toggle toggle => toggle.Value(stored) ? "1" : "0",
            ExtraField.Choice choice => Quotable(choice.Value(stored)),
            ExtraField.Text { Multiline: true } text => Lines(text.Value(stored)),
            ExtraField.Text text => Quotable(text.Value(stored)),
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
            ExtraField.Text text => text.Value(stored) == text.Fallback,
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
    /// What a line break inside a text field is written as, since a string in
    /// the language is one line and has no escapes.
    /// </summary>
    public const char LineBreak = '|';

    /// <summary>
    /// Text of several lines as one string, or null where it cannot be written:
    /// a bar already in it would read back as a break.
    /// </summary>
    private static string? Lines(string value) =>
        value.Contains(LineBreak) ? null : Quotable(value.Replace('\n', LineBreak));

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

        var written = new List<Expr>();
        var mentioned = new List<NameExpr>();
        var turns = new List<KnobStatement>();
        var principals = new List<Expr>();

        foreach (var statement in read) Gather(statement, written, mentioned, turns, principals);

        written.Sort((a, b) => a.Line == b.Line ? a.Column - b.Column : a.Line - b.Line);

        if (written.Count != order.Count) return SourceMap.Empty;

        var mentions = new List<(Site Where, Guid Node)>();
        var calls = new Dictionary<Guid, Site>();
        var values = new Dictionary<(Guid Node, string Name), Site?>();
        var placed = new Dictionary<Expr, Guid>();
        var sink = patch.Nodes.FirstOrDefault(n => NodeCatalog.IsSink(n.TypeId));

        for (var i = 0; i < written.Count; i++)
        {
            var node = order[i];
            var site = new Site(written[i].Line, written[i].Column);

            placed[written[i]] = node;
            mentions.Add((site, node));

            // A sum places its Expression at its operator, and has no brackets of
            // its own for anything to be written into.
            if (written[i] is not CallExpr call) continue;

            calls[node] = site;

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

    /// <summary>Every call and sum, name and knob one statement writes.</summary>
    private static void Gather(
        Statement statement,
        List<Expr> calls,
        List<NameExpr> names,
        List<KnobStatement> turns,
        List<Expr> principals)
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

    private static void Inside(Expr expr, List<Expr> calls, List<NameExpr> names, bool summing = false)
    {
        switch (expr)
        {
            case CallExpr call:
                calls.Add(call);
                foreach (var argument in call.Arguments) Inside(argument.Value, calls, names);
                break;

            // The outermost operator of a sum that reads a signal is an Expression;
            // the operators inside it are that same one.
            case BinaryExpr or NegateExpr:
                if (!summing && Signalled(expr)) calls.Add(expr);

                if (expr is BinaryExpr binary)
                {
                    Inside(binary.Left, calls, names, summing: true);
                    Inside(binary.Right, calls, names, summing: true);
                }
                else Inside(((NegateExpr)expr).Value, calls, names, summing: true);

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
    private static Expr? Principal(Expr expr) => expr switch
    {
        PipeExpr pipe => Principal(pipe.Stage) ?? Principal(pipe.Source),
        CallExpr call => call,
        BinaryExpr or NegateExpr when Signalled(expr) => expr,
        _ => null,
    };

    /// <summary>Whether arithmetic reads anything but numbers, which is whether it places a module.</summary>
    private static bool Signalled(Expr expr) => expr switch
    {
        NumberExpr => false,
        NegateExpr negate => Signalled(negate.Value),
        BinaryExpr binary => Signalled(binary.Left) || Signalled(binary.Right),
        _ => true,
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
    /// output other than the first — a Sequencer's gate reads better off a name
    /// than off the end of the call that wrote its tune — or where somebody
    /// named it on the canvas.
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
        // be what 'x' means — and so is one that is switched off, which has to
        // keep a name of its own to be said as off.
        var coord = patch.Nodes.FirstOrDefault(n => n.TypeId == NodeCatalog.CoordTypeId && !n.Off)?.Id ?? Guid.Empty;
        var clock = patch.Nodes.FirstOrDefault(n => n.TypeId == NodeCatalog.TimeTypeId && !n.Off)?.Id ?? Guid.Empty;

        // Where a wire runs backwards into a module, that module is written as a
        // name and the wire as a back-wire onto it — the one statement in the
        // language that runs right to left, and the only way a loop can be said
        // at all. Which means the module needs a name to be said with.
        var looped = Cycles.Backwards(patch).Select(wire => wire.TargetNode).ToHashSet();

        foreach (var node in patch.Nodes)
        {
            if (node.Id == coord || node.Id == clock) continue;
            if (NodeCatalog.IsSink(node.TypeId)) continue;

            var leaving = patch.Connections.Where(c => c.SourceNode == node.Id).ToList();

            var must = called is not null
                || looped.Contains(node.Id)
                || leaving.Count != 1
                || leaving.Any(c => c.SourcePort != 0)
                || node.Off
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

        return new Plan(names, bound, taken, coord, clock);
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

        return name is not ("_" or "let" or "def" or "group" or "off" or "out" or "in" or "x" or "y" or "t" or "radius" or "angle" or "aspect");
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
        /// The wires that run backwards, which are the only ones this does not
        /// follow: written where they are reached, a loop would be walked round
        /// and round for ever. Each is said once at the end instead, as the
        /// back-wire it is — see <see cref="Cycles"/>.
        /// </summary>
        private readonly IReadOnlySet<Connection> backwards = Graph.Cycles.Backwards(patch);

        /// <summary>
        /// What feeds a socket, unless what feeds it runs backwards — in which
        /// case nothing does, as far as the expression being written is
        /// concerned.
        /// </summary>
        private Connection? Forward(Guid node, int port) =>
            patch.IncomingTo(node, port) is { } wire && !backwards.Contains(wire) ? wire : null;

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

                        statements.Add(new Part($"{Headed(from.Text)} |> out.{name}", from.Calls));
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
            Switched();

            var ordered = Ordered();

            // First, because it is about the whole patch and not about any line
            // below it.
            string?[] about = [Description(patch.Description), Author(patch.Author), Tags(patch.Tags)];

            if (about.Any(line => line is not null))
            {
                foreach (var line in about.OfType<string>()) text.AppendLine(line);
                text.AppendLine();
            }

            if (Keyboard(patch.KeyboardScale) is { } keyboard)
            {
                text.AppendLine(keyboard);
                text.AppendLine();
            }

            foreach (var statement in ordered) text.AppendLine(statement.Text);

            // Where the modules go is worked out after the graph is built and
            // where the lines break is worked out after the text is written, by
            // a pass that knows nothing about how either was made — see
            // SourceLayout, and PatchLayout on the other side of it.
            return (SourceLayout.Wrap(text.ToString()), [.. ordered.SelectMany(part => part.Calls)]);
        }

        /// <summary>
        /// Text fit to open a statement: a minus that leads a line carries on the
        /// line above, so a sum that opens on one is bracketed up to its first pipe.
        /// </summary>
        private static string Headed(string text)
        {
            if (!text.StartsWith('-')) return text;

            var depth = 0;
            var quoted = false;

            for (var i = 0; i < text.Length - 1; i++)
            {
                var c = text[i];

                if (c == '"') quoted = !quoted;
                if (quoted) continue;

                if (c is '(' or '[') depth++;
                else if (c is ')' or ']') depth--;
                else if (depth == 0 && c == '|' && text[i + 1] == '>')
                    return $"({text[..i].TrimEnd()}) {text[i..]}";
            }

            return $"({text})";
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

            var written = Call(node, def);

            Place(new Part($"let {plan.Names[id]} = {written.Text}", written.Calls));
        }

        private void Place(Part statement)
        {
            statements.Add(statement);
            where[Guid.Empty] = statements.Count;
        }

        /// <summary>
        /// The wires that run backwards, which is what a loop is made of. Written
        /// last, after every module either end of one has a name to be said with.
        /// </summary>
        private void Cycles()
        {
            foreach (var wire in patch.Connections)
            {
                if (!backwards.Contains(wire)) continue;
                if (patch.Find(wire.TargetNode) is not { } node) continue;
                if (modules.Get(node.TypeId) is not { } def) continue;
                if (wire.TargetPort < 0 || wire.TargetPort >= def.Inputs.Count) continue;

                Ensure(wire.TargetNode);

                var from = From(wire);
                var name = def.Inputs[wire.TargetPort].Name.Replace(' ', '_');

                statements.Add(new Part($"{plan.Names[wire.TargetNode]}.{name} <- {from.Text}", from.Calls));
            }
        }

        /// <summary>
        /// The modules that are switched off, each said after the binding that
        /// gives it a name — see <see cref="Prepare"/>, which is where one is
        /// made sure of having one.
        /// </summary>
        private void Switched()
        {
            foreach (var node in patch.Nodes)
            {
                if (!node.Off || NodeCatalog.IsSink(node.TypeId)) continue;
                if (!plan.Names.TryGetValue(node.Id, out var name)) continue;

                statements.Add(Part.Of($"off {name}"));
            }
        }

        /// <summary>What a module is written as, with its pipe chosen to read back the same way.</summary>
        /// <remarks>
        /// The modules it names come back in the order the words do: whatever is
        /// piped in is written first, then this one, then its arguments.
        /// </remarks>
        private Part Call(NodeInstance node, NodeDef def)
        {
            if (Infix(node, def) is { } sum) return sum;

            var used = new HashSet<int>();
            var before = new List<Guid>();
            var after = new List<Guid>();
            string? piped = null;
            int? landing = null;

            // The pipe is chosen so that the rule which reads it puts the signal
            // back where it came from: an 'in' first, then a position taken whole
            // off one module's leading pair.
            var signal = Port(def.Inputs, "in");

            // A module's only socket is where a bare pipe lands, as 'in' is.
            if (signal < 0 && def.Inputs.Count == 1) signal = 0;

            if (signal >= 0 && Forward(node.Id, signal) is { } straight)
            {
                var part = From(straight);

                piped = part.Text;
                before.AddRange(part.Calls);
                used.Add(signal);
            }
            else if (def.Inputs.Count >= 2
                && Named(def.Inputs[0], "x") && Named(def.Inputs[1], "y")
                && Forward(node.Id, 0) is { SourcePort: 0 } first
                && Forward(node.Id, 1) is { SourcePort: 1 } second
                && first.SourceNode == second.SourceNode
                && first.SourceNode != plan.Coord)
            {
                var part = Whole(first.SourceNode);

                piped = part.Text;
                before.AddRange(part.Calls);
                used.Add(0);
                used.Add(1);
            }
            else if (signal < 0 && Tint(node, def) is var (tint, colored))
            {
                // A color into the one color socket a module has lands there
                // with no '_', so it is written that way.
                var part = From(colored);

                piped = part.Text;
                before.AddRange(part.Calls);
                used.Add(tint);
            }
            else if (signal < 0 && MainLine(node, def) is var (port, wire))
            {
                // Otherwise the socket the longest chain arrives on, said with
                // '_'. It is what turns a patch into the chain it was built as,
                // rather than a name bound for every module that has no 'in'.
                var part = From(wire);

                piped = part.Text;
                before.AddRange(part.Calls);
                used.Add(port);
                landing = port;
            }

            var arguments = new List<string>();

            // The file a module names rather than carries (ADR-0052). It is not
            // a socket, so it goes in as the one thing the language writes in
            // quotes, and it goes first because that is where it reads.
            if (File(node, def) is { } path) arguments.Add($"\"{path}\"");

            for (var port = 0; port < def.Inputs.Count; port++)
            {
                var name = def.Inputs[port].Name.Replace(' ', '_');

                if (port == landing) arguments.Add($"{name}: _");
                if (used.Contains(port)) continue;

                // Forward rather than incoming: a socket fed by a wire that runs
                // backwards is written as nothing here and said at the end as the
                // back-wire it is. Written here, it would be the module quoting
                // itself.
                if (Forward(node.Id, port) is { } wire)
                {
                    var part = Argument(wire);

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

        /// <summary>
        /// The socket a module with no <c>in</c> is piped into: the one the longest
        /// chain of written-out calls arrives on, else the first, else none.
        /// </summary>
        /// <remarks>
        /// A name, a coordinate or a sum reads as well inside the brackets as
        /// before the pipe, so only a call counts toward a chain. Ties go to the
        /// earlier socket.
        /// </remarks>
        private (int Port, Connection Wire)? MainLine(NodeInstance node, NodeDef def)
        {
            (int Port, Connection Wire)? best = null;
            var longest = 0;

            for (var port = 0; port < def.Inputs.Count; port++)
            {
                if (Forward(node.Id, port) is not { } wire) continue;

                var length = Chain(wire.SourceNode);

                if (length <= longest) continue;

                longest = length;
                best = (port, wire);
            }

            return best ?? (Forward(node.Id, 0) is { } leading ? (0, leading) : null);
        }

        /// <summary>The one color socket a module has, where a color arrives on it from a module declaring one.</summary>
        private (int Port, Connection Wire)? Tint(NodeInstance node, NodeDef def)
        {
            var colors = Enumerable.Range(0, def.Inputs.Count).Where(i => def.Inputs[i].Kind == PortKind.Color).ToList();

            if (colors is not [var tint] || Forward(node.Id, tint) is not { } wire) return null;

            // A module that leads with a position would read a bare pipe from a
            // two-output source as the pair, so it keeps its '_'.
            if (def.Inputs.Count >= 2 && Named(def.Inputs[0], "x") && Named(def.Inputs[1], "y")) return null;
            if (wire.SourceNode == plan.Coord || wire.SourceNode == plan.Clock) return null;
            if (patch.Find(wire.SourceNode) is not { } source || modules.Get(source.TypeId) is not { } from) return null;

            return wire.SourcePort < from.Outputs.Count && from.Outputs[wire.SourcePort].Kind == PortKind.Color
                ? (tint, wire)
                : null;
        }

        private readonly Dictionary<Guid, int> chains = [];

        /// <summary>How many written-out calls a module's text is a chain of, counting itself.</summary>
        private int Chain(Guid id)
        {
            if (chains.TryGetValue(id, out var known)) return known;

            // Settled before the walk, so a loop the backwards wires missed ends here.
            chains[id] = 0;

            if (id == plan.Coord || id == plan.Clock || plan.Bound.Contains(id)) return 0;
            if (patch.Find(id) is not { } node || modules.Get(node.TypeId) is not { } def) return 0;

            var longest = 0;

            for (var port = 0; port < def.Inputs.Count; port++)
                if (Forward(id, port) is { } wire)
                    longest = Math.Max(longest, Chain(wire.SourceNode));

            // A sum over names is written as the sum and reads as one; a sum
            // reading a chain, or one the arithmetic cannot say, is the call.
            if (longest == 0 && Summed(node, def)) return 0;

            return chains[id] = longest + 1;
        }

        /// <summary>Whether an Expression can be written as its sum, as far as its own formula and sockets say.</summary>
        private bool Summed(NodeInstance node, NodeDef def)
        {
            if (def.Extra<FormulaExtra>() is not { } extra) return false;

            var reads = new List<(int Socket, bool After)>();

            if (Formula.Infix(FormulaExtra.Of(node), extra.Functions, _ => "0", _ => "a", reads) is null) return false;

            return reads.All(read => Forward(node.Id, read.Socket) is not null);
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

        /// <summary>
        /// What a wire's far end is written as inside an argument, which takes no
        /// pipeline: one that would be is given a name of its own and said above.
        /// </summary>
        private Part Argument(Connection wire)
        {
            var part = From(wire);

            if (!Pipeline(part.Text) || patch.Find(wire.SourceNode) is not { } node) return part;

            plan.Bound.Add(node.Id);
            plan.Names[node.Id] = Unique(Wanted(node, modules), plan.Taken);
            chains.Clear();

            return From(wire);
        }

        /// <summary>Whether written text has a pipe outside every bracket and quote.</summary>
        private static bool Pipeline(string text)
        {
            var depth = 0;
            var quoted = false;

            for (var i = 0; i < text.Length - 1; i++)
            {
                var c = text[i];

                if (c == '"') quoted = !quoted;
                if (quoted) continue;

                if (c is '(' or '[') depth++;
                else if (c is ')' or ']') depth--;
                else if (depth == 0 && c == '|' && text[i + 1] == '>') return true;
            }

            return false;
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
                    3 => "angle",
                    _ => "aspect",
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
        /// An Expression written as the arithmetic it is, or null where that would
        /// not read back as this module.
        /// </summary>
        /// <remarks>
        /// Read back, a sum is one Expression whose signals are its sockets (ADR-0106),
        /// so what is written has to be one: every socket it reads wired, and wired
        /// forwards, and every wire into it read. A socket's source may not be an
        /// Expression written the same way, or the two sums would read back as one;
        /// a source written out in full may stand only once, or it would read back
        /// as two modules; and a source that is a pipeline would need brackets the
        /// layout cannot fold. Anything else is the call, which always reads back.
        /// <para>
        /// The module is placed where the operator that joins the whole sum stands,
        /// so the modules its sockets name come back either side of it.
        /// </para>
        /// </remarks>
        private Part? Infix(NodeInstance node, NodeDef def)
        {
            if (def.Extra<FormulaExtra>() is not { } extra) return null;

            var formula = FormulaExtra.Of(node);
            var reads = new List<(int Socket, bool After)>();

            if (Formula.Infix(formula, extra.Functions, _ => "0", _ => "a", reads) is null) return null;

            var read = reads.Select(r => r.Socket).ToHashSet();

            for (var port = 0; port < def.Inputs.Count; port++)
            {
                var incoming = patch.IncomingTo(node.Id, port);

                if (read.Contains(port) != (incoming is not null)) return null;
                if (incoming is null) continue;
                if (Forward(node.Id, port) is null) return null;

                if (patch.Find(incoming.SourceNode) is { TypeId: NodeCatalog.ExpressionTypeId }
                    && !plan.Bound.Contains(incoming.SourceNode))
                {
                    return null;
                }
            }

            var parts = new Dictionary<int, Part>();

            foreach (var port in read)
            {
                var part = From(Forward(node.Id, port)!);

                if (part.Calls.Count > 0 && reads.Count(r => r.Socket == port) > 1) return null;

                // A pipeline bracketed into a sum is the one line the layout cannot
                // fold, and it reads better piped into the call.
                if (!Atom(part.Text)) return null;

                parts[port] = part;
            }

            reads.Clear();

            var text = Formula.Infix(
                formula,
                extra.Functions,
                value => Value(value, PortDisplay.Number),
                port => parts[port].Text,
                reads);

            if (text is null) return null;

            return new Part(
                text,
                [
                    .. reads.Where(r => !r.After).SelectMany(r => parts[r.Socket].Calls),
                    node.Id,
                    .. reads.Where(r => r.After).SelectMany(r => parts[r.Socket].Calls),
                ]);
        }

        /// <summary>
        /// Whether written text reads as one value inside a sum without brackets,
        /// which a pipeline or a sum at its top level does not.
        /// </summary>
        private static bool Atom(string text)
        {
            if (text.StartsWith('-')) return false;

            var depth = 0;
            var quoted = false;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                if (c == '"') quoted = !quoted;
                if (quoted) continue;

                if (c is '(' or '[') depth++;
                else if (c is ')' or ']') depth--;
                else if (depth == 0 && (c is '|' or '+' or '*' or '/' or '%' || (c == '-' && i > 0 && text[i - 1] == ' ')))
                    return false;
            }

            return true;
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

        private static string Step(Step step, bool note)
        {
            // A rest has no pitch to write. A note at no volume keeps its own,
            // and the two are different steps however alike they sound.
            // ReSharper disable once CompareOfFloatsByEqualityOperator
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
                // ReSharper disable once CompareOfFloatsByEqualityOperator
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
                    // ReSharper disable once CompareOfFloatsByEqualityOperator
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
