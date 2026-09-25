using System.Text.Json.Nodes;
using Flyback.Core.Graph;

namespace Flyback.Core.Language;

/// <summary>
/// A syntax tree to a <see cref="Patch"/>. Everything the language knows about
/// the instrument is here.
/// </summary>
/// <remarks>
/// The catalog is the language: short names, socket names, arities and which
/// literals a socket takes are read out of <see cref="ModuleCatalog"/> as the
/// tree is walked, so a plugin's modules are usable the moment it loads.
/// Nothing is compiled or evaluated — a <c>def</c> is expanded, a number between
/// two numbers is folded, and everything else becomes a node and a wire.
/// </remarks>
public sealed class Binder
{
    private readonly ModuleCatalog modules;
    private readonly List<LanguageIssue> issues;
    private readonly Patch patch = new();

    private readonly ModuleNames moduleNames;
    private readonly Dictionary<string, DefStatement> defs = new(StringComparer.Ordinal);
    private readonly HashSet<string> expanding = new(StringComparer.Ordinal);

    /// <summary>Every place the text names a module, in the order it names them.</summary>
    private readonly List<(Site Where, Guid Node)> mentions = [];

    /// <summary>The call that placed each module, which is where a knob is added.</summary>
    private readonly Dictionary<Guid, Site> calls = [];

    /// <summary>
    /// Where the file writes each named value it sets — a socket's knob, or one
    /// of a plugin's declared fields.
    /// </summary>
    private readonly Dictionary<(Guid Node, string Name), Site?> written = [];

    /// <summary>Modules a statement is about, rather than mentions inside one.</summary>
    private readonly HashSet<Guid> bound = [];

    /// <summary>What the text calls each module it has a word for.</summary>
    private readonly Dictionary<Guid, string> named = [];

    /// <summary>Every id <see cref="Next"/> has handed out.</summary>
    private readonly HashSet<Guid> issued = [];

    /// <summary>The line that wired each socket the text wires.</summary>
    private readonly Dictionary<(Guid Node, int Port), int> wired = [];

    /// <summary>Each group the text opens and the modules its blocks placed, a name's blocks gathered into one.</summary>
    private readonly List<(string? Name, List<Guid> Members)> boxes = [];

    /// <summary>The boxes a block of which had a mistake in it, and so no say about its size.</summary>
    private readonly HashSet<int> troubled = [];

    /// <summary>Where each group block begins, and which of <see cref="boxes"/> it is.</summary>
    private readonly List<(Site Where, int Box)> opened = [];

    /// <summary>Where each group block begins, and the group it built.</summary>
    private readonly List<(Site Where, Guid Group)> blocks = [];

    /// <summary>Plugins a <c>requires</c> line named that this build does not have.</summary>
    private readonly List<string> missing = [];

    /// <summary>The line that set each knob the text sets.</summary>
    private readonly Dictionary<(Guid Node, int Port), int> turned = [];

    private Guid coordinates;
    private Guid clock;

    /// <summary>The line each top-level name is bound on, for saying so where one is read above it.</summary>
    private readonly Dictionary<string, int> boundOn = [];

    /// <summary>
    /// Where in the source the modules being placed are coming from, as a path of
    /// names — <c>let bass</c>, then <c>reverb~0</c> for the def it calls.
    /// </summary>
    /// <remarks>
    /// A module's id is this path and its position under it, so building the same
    /// text twice gives the same patch down to the guids and editing one line
    /// changes that line's modules only — which is what lets a rebuild keep the
    /// accumulator that was playing, the canvas position and the selection. Names
    /// rather than numbers wherever a statement has one: numbering would give
    /// every module below an inserted line a new identity.
    /// </remarks>
    private string where = string.Empty;

    /// <summary>How many modules have been placed directly under <see cref="where"/>.</summary>
    private int placedHere;

    /// <summary>
    /// How many things under <see cref="where"/> have had no name of their own —
    /// a pipeline that does not end at a socket, a def stamped out twice in one
    /// statement. Counted because there is nothing to name them by.
    /// </summary>
    private int unnamedHere;

    public Binder(ModuleCatalog modules, List<LanguageIssue> issues)
    {
        this.modules = modules;
        this.issues = issues;

        moduleNames = new ModuleNames(modules);
    }

    /// <summary>
    /// Where the text says each of the things it built, for a caret that has to
    /// name a module and a knob that has to be written back. Only the binder can
    /// say this: a patch carries nothing about the file it came from.
    /// </summary>
    public SourceMap Map(string source) => new(source, mentions, calls, written, named, bound, blocks);

    /// <summary>The patch these statements describe, laid out and ready to compile.</summary>
    public Patch Build(IReadOnlyList<Statement> statements)
    {
        // Named rather than left to chance like the rest of it, because the
        // Output is the one module every patch has and the one every rebuild
        // must recognise as the same one.
        patch.EnsureOutput(modules, Identity("out"));

        // The one module nothing places, so the one whose knobs can only ever be
        // written as a statement of their own.
        named[patch.Output.Id] = "out";

        var scope = new Scope(null);

        // Before anything names a module, so a plugin that is missing is said
        // once rather than once for every module it would have given.
        foreach (var requires in statements.OfType<RequiresStatement>()) Require(requires);

        foreach (var statement in statements.SelectMany(s => s is GroupStatement group ? group.Body : [s]))
        {
            IEnumerable<string> names = statement switch
            {
                LetStatement let => [let.Name],
                LetTupleStatement tuple => tuple.Names,
                PanelStatement panel => [panel.Name],
                _ => [],
            };

            foreach (var name in names) boundOn.TryAdd(name, statement.Line);
        }

        foreach (var statement in statements) Run(statement, scope);

        // Once every block has been read, since a group may be opened more than
        // once and its first block may hold fewer modules than a box can be.
        // Named from the text, so the same text builds the same boxes as it builds
        // the same modules.
        for (var i = 0; i < boxes.Count; i++)
        {
            var (name, members) = boxes[i];

            if (patch.Group(members) is not { } made)
            {
                if (troubled.Contains(i)) continue;

                var (line, column) = opened.First(open => open.Box == i).Where;

                Complain(IssueCode.GroupTooSmall, line, column,
                    $"a group is drawn round {NodeGroup.Fewest} modules or more, and this one has {members.Count}.");
                continue;
            }

            var group = made.Clone(Identity(name is null ? $"group #{i}" : "group " + name));

            group.Rename(name);
            patch.Groups![patch.Groups.IndexOf(made)] = group;

            foreach (var (where, box) in opened)
                if (box == i) blocks.Add((where, group.Id));
        }

        // A call to a Maths module the Expression stands for, and the sums and
        // calls around it, arrive as the Expressions a preset's do (ADR-0109).
        var into = new Dictionary<Guid, Guid>();
        ExpressionFusion.Fuse(patch, modules, into);
        Folded(into);

        // Positions are not in the language, so they are worked out afterwards
        // by the same layout the editor uses on a pasted fragment (ADR-0044).
        PatchLayout.Arrange(patch, modules);

        return patch;
    }

    /// <summary>
    /// What the text says about modules the folding took away or remade: a word
    /// that named one names the Expression it went into, and a knob or a call
    /// written for one points nowhere, since its brackets take no formula.
    /// </summary>
    private void Folded(Dictionary<Guid, Guid> into)
    {
        if (into.Count == 0) return;

        for (var i = 0; i < mentions.Count; i++)
            if (into.TryGetValue(mentions[i].Node, out var root))
                mentions[i] = (mentions[i].Where, root);

        foreach (var id in into.Keys) calls.Remove(id);

        foreach (var key in written.Keys.Where(key => into.ContainsKey(key.Node)).ToList()) written.Remove(key);

        foreach (var (id, root) in into)
            if (bound.Remove(id)) bound.Add(root);
    }

    // --- what a name is worth ------------------------------------------------

    /// <summary>Anything a name or an expression can stand for while binding.</summary>
    private abstract record Value;

    /// <summary>A number, which becomes a knob rather than a module.</summary>
    /// <param name="Where">
    /// Where the file writes it, so the knob can be changed where the text
    /// already says it. Null for a number no single figure stands for —
    /// <c>1/12</c> is one knob and two numbers.
    /// </param>
    private sealed record Figure(double Amount, NumberStyle Style, Site? Where = null) : Value;

    /// <summary>A placed module, standing for every one of its outputs at once.</summary>
    private sealed record Placed(Guid Id, NodeDef Def) : Value;

    /// <summary>One output of a placed module.</summary>
    private sealed record Socket(Guid Id, int Port) : Value;

    /// <summary>What a def with several results hands back.</summary>
    private sealed record Several(IReadOnlyList<Value> Items) : Value;

    /// <summary>
    /// A name whose statement was refused. Bound all the same, so every line that
    /// reads it fails without a word: the text is refused whole already, and a
    /// complaint per reader would bury the one mistake there is.
    /// </summary>
    private sealed record Failed : Value;

    /// <summary>A file a module names rather than carries (ADR-0052).</summary>
    private sealed record Named(string Path) : Value;

    /// <summary>
    /// A panel knob, and the range a socket reads it over where the text gives
    /// one: <c>cutoff</c>, or <c>cutoff(200..4000, knee: 20)</c>.
    /// </summary>
    /// <param name="Word">What the text calls it.</param>
    private sealed record Dial(PatchControl Control, string Word, Figure? Low = null, Figure? High = null, Figure? Knee = null)
        : Value;

    /// <summary>Names in sight, and the names the enclosing scope had.</summary>
    private sealed class Scope(Scope? parent)
    {
        private readonly Dictionary<string, (Value Value, int Line)> names = new(StringComparer.Ordinal);

        /// <param name="line">Where the name is bound, for a second binding to point back at.</param>
        public void Set(string name, Value value, int line) => names[name] = (value, line);

        public Value? Find(string name) => Entry(name)?.Value;

        public (Value Value, int Line)? Entry(string name) =>
            names.TryGetValue(name, out var entry) ? entry : parent?.Entry(name);
    }

    private void Complain(string code, int line, int column, string message) =>
        issues.Add(new LanguageIssue(line, column, code, message));

    /// <summary>
    /// Whether <paramref name="name"/> may be bound here, said where it may not.
    /// A name means one thing, so no reader has to work out which binding a word
    /// reaches.
    /// </summary>
    private bool Free(string name, Scope scope, int line, int column)
    {
        if (Builtin(name) is { } what)
        {
            Complain(IssueCode.ReservedName, line, column, $"'{name}' is already {what}. Call this something else.");
            return false;
        }

        if (scope.Entry(name) is { } first)
        {
            Complain(IssueCode.BoundTwice, line, column, $"'{name}' is already bound on line {first.Line}. A name is bound once.");
            return false;
        }

        return true;
    }

    // --- statements ----------------------------------------------------------

    private void Run(Statement statement, Scope scope)
    {
        // Named before entering, because the name is drawn from the segment this
        // statement sits in — a statement with nothing to be called by takes the
        // next number from its parent, not from itself.
        var outer = Enter(Naming(statement));

        Ran(statement, scope);

        Leave(outer);
    }

    /// <summary>
    /// What a statement is called, for the purposes of naming what it places. A
    /// <c>let</c> has a name and a terminated pipeline has a socket; what is left
    /// takes a number, which is the one case where inserting a line above moves
    /// something below it.
    /// </summary>
    private string Naming(Statement statement) => statement switch
    {
        LetStatement let => "let " + let.Name,
        LetTupleStatement tuple => "let " + string.Join(',', tuple.Names),
        KnobStatement knob => Aimed(knob.Target),
        BackWireStatement back => Aimed(back.Target) + " <-",
        GroupStatement group => "group " + group.Name,
        DefStatement def => "def " + def.Name,
        OffStatement off => "off " + off.Target.Name,
        PanelStatement panel => "panel " + panel.Name,
        RequiresStatement => "requires",
        KeyboardStatement => "keyboard",
        DescriptionStatement => "description",
        AuthorStatement => "author",
        TagsStatement => "tags",
        PipelineStatement pipeline => Ending(pipeline.Value) ?? Anonymous(),
        _ => Anonymous(),
    };

    private static string Aimed(NameExpr target) =>
        target.Port is null ? target.Name : target.Name + "." + target.Port;

    /// <summary>
    /// The socket a pipeline ends at, which is what a statement with no name of
    /// its own is known by — <c>out.color</c> and <c>out.left</c> stay two
    /// different statements however the lines around them are shuffled.
    /// </summary>
    private static string? Ending(Expr expr) =>
        expr is PipeExpr { Stage: NameExpr socket } ? Aimed(socket) : null;

    private string Anonymous() => "#" + unnamedHere++;

    private void Ran(Statement statement, Scope scope)
    {
        switch (statement)
        {
            case DefStatement def:
                if (!defs.TryAdd(def.Name, def))
                    Complain(IssueCode.DefTwice, def.Line, def.Column, $"'{def.Name}' is already the name of a def.");

                for (var i = 0; i < def.Parameters.Count; i++)
                {
                    var parameter = def.Parameters[i];

                    if (Builtin(parameter) is { } what)
                        Complain(IssueCode.ReservedName, def.Line, def.Column, $"'{parameter}' is already {what}. Call this something else.");
                    else if (def.Parameters.Take(i).Contains(parameter, StringComparer.Ordinal))
                        Complain(IssueCode.BoundTwice, def.Line, def.Column, $"'{def.Name}' takes two parameters called '{parameter}'.");
                }

                break;

            case LetStatement let:
                if (!Free(let.Name, scope, let.Line, let.Column)) break;

                if (Bind(let.Value, scope) is { } value)
                {
                    Label(value, let.Name);
                    scope.Set(let.Name, value, let.Line);
                    Owns(value);

                    if (value is Placed placed) named.TryAdd(placed.Id, let.Name);
                }
                else
                {
                    scope.Set(let.Name, new Failed(), let.Line);
                }

                break;

            case LetTupleStatement tuple:
                Destructure(tuple, scope);
                Unmade(tuple.Names, scope, tuple.Line);
                break;

            case PipelineStatement pipeline:
                Owns(Bind(pipeline.Value, scope));
                break;

            case KnobStatement knob:
                Turn(knob, scope);
                break;

            case BackWireStatement back:
                Backwards(back, scope);
                break;

            case GroupStatement group:
                Box(group, scope);
                break;

            case KeyboardStatement keyboard:
                Lay(keyboard);
                break;

            case DescriptionStatement description:
                Describe(description);
                break;

            case AuthorStatement author:
                Credit(author);
                break;

            case TagsStatement tags:
                Tag(tags);
                break;

            case OffStatement off:
                Switch(off, scope);
                break;

            case PanelStatement panel:
                Declare(panel, scope);
                Unmade([panel.Name], scope, panel.Line);
                break;
        }
    }

    /// <summary>Binds each name a refused statement leaves unbound to <see cref="Failed"/>.</summary>
    private static void Unmade(IEnumerable<string> names, Scope scope, int line)
    {
        foreach (var name in names)
            if (scope.Entry(name) is null) scope.Set(name, new Failed(), line);
    }

    // --- naming what gets placed ---------------------------------------------

    /// <summary>
    /// Steps into <paramref name="segment"/>, handing back what to put back
    /// afterwards. Saved and restored rather than set, because these nest: a def
    /// is stamped out inside the statement that called it.
    /// </summary>
    private (string Where, int Placed, int Unnamed) Enter(string segment)
    {
        var outer = (where, placedHere, unnamedHere);

        where = where.Length == 0 ? segment : where + "/" + segment;
        placedHere = 0;
        unnamedHere = 0;

        return outer;
    }

    private void Leave((string Where, int Placed, int Unnamed) outer) =>
        (where, placedHere, unnamedHere) = outer;

    /// <summary>The name for the next module placed where the binder is standing.</summary>
    /// <remarks>
    /// Only text that is already wrong names one place twice, such as two
    /// pipelines into <c>out.color</c>. It gets a second id, so the mistake is
    /// reported instead of two modules sharing one.
    /// </remarks>
    private Guid Next()
    {
        var name = $"{where}#{placedHere++}";
        var id = Identity(name);

        for (var again = 1; !issued.Add(id); again++) id = Identity($"{name}'{again}");

        return id;
    }

    /// <summary>
    /// A guid from a name, the same one every time.
    /// </summary>
    /// <remarks>
    /// A hash rather than a counter, because what has to be stable is the mapping
    /// from a piece of source to an id — across runs, across machines, and with
    /// statements added around it. SHA-256 cut to sixteen bytes: this is a name
    /// and not a secret.
    /// </remarks>
    internal static Guid Identity(string name) =>
        new(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(name)).AsSpan(0, 16));

    /// <summary>The id a panel knob the text calls <paramref name="word"/> is given.</summary>
    internal static Guid PanelId(string word) => Identity("panel " + word);

    /// <summary>
    /// Gives a node the name it was bound to, which the editor shows on it. Only
    /// a module placed by this very binding takes it: the name on the canvas
    /// should say where the module was made rather than where it was last
    /// mentioned.
    /// </summary>
    private void Label(Value value, string name)
    {
        if (value is not Placed placed) return;
        if (patch.Find(placed.Id) is not { Name: null } node) return;
        if (NodeCatalog.IsSink(node.TypeId)) return;

        node.Rename(placed.Def, name);
    }

    /// <summary>
    /// Marks a module as the one its statement is about, so a caret anywhere in
    /// the statement points at it.
    /// </summary>
    /// <remarks>
    /// The last stage of a pipeline, because that is the one the statement names:
    /// <c>let hum = t |&gt; sine(...) |&gt; gain(...)</c> is a Gain called hum.
    /// </remarks>
    private void Owns(Value? value)
    {
        if (value is Placed placed) bound.Add(placed.Id);
        else if (value is Socket socket) bound.Add(socket.Id);
    }

    /// <summary>Notes that <paramref name="expr"/> is a word naming a module.</summary>
    private void Mention(Expr expr, Value value)
    {
        var id = value switch
        {
            Placed placed => placed.Id,
            Socket socket => socket.Id,
            _ => Guid.Empty,
        };

        if (id != Guid.Empty) mentions.Add((new Site(expr.Line, expr.Column), id));
    }

    private void Destructure(LetTupleStatement statement, Scope scope)
    {
        for (var i = 0; i < statement.Names.Count; i++)
        {
            var name = statement.Names[i];

            if (!Free(name, scope, statement.Line, statement.Column)) return;

            if (statement.Names.Take(i).Contains(name, StringComparer.Ordinal))
            {
                Complain(IssueCode.BoundTwice, statement.Line, statement.Column, $"'{name}' is written twice. A name is bound once.");
                return;
            }
        }

        if (Bind(statement.Value, scope) is not { } value) return;

        if (value is not Several several)
        {
            Complain(IssueCode.TupleMismatch, statement.Line, statement.Column,
                "this hands back one thing, so it cannot be taken apart into several.");
            return;
        }

        if (several.Items.Count != statement.Names.Count)
        {
            Complain(IssueCode.TupleMismatch, statement.Line, statement.Column,
                $"this hands back {several.Items.Count} things and {statement.Names.Count} names are waiting for them.");
            return;
        }

        for (var i = 0; i < statement.Names.Count; i++)
        {
            Label(several.Items[i], statement.Names[i]);
            scope.Set(statement.Names[i], several.Items[i], statement.Line);
        }
    }

    private void Turn(KnobStatement statement, Scope scope)
    {
        if (Input(statement.Target, scope) is not var (node, def, port)) return;
        if (Bind(statement.Value, scope) is not { } value) return;

        if (value is Dial dial)
        {
            Link(node, def, port, dial, statement.Line, statement.Column);
            return;
        }

        if (value is not Figure figure)
        {
            Complain(IssueCode.KnobNeedsNumber, statement.Line, statement.Column,
                "a knob takes a number or a panel knob. Use '<-' to wire a signal into it.");
            return;
        }

        Knob(node, def, port, figure, statement.Line, statement.Column);
    }

    private void Backwards(BackWireStatement statement, Scope scope)
    {
        if (Input(statement.Target, scope) is not var (node, _, port)) return;
        if (Bind(statement.Value, scope) is not { } value) return;

        Feed(value, 0, node.Id, port, statement.Line, statement.Column);
    }

    /// <summary>
    /// Takes a module out of the signal path — see <see cref="NodeInstance.Off"/>.
    /// </summary>
    private void Switch(OffStatement statement, Scope scope)
    {
        var target = statement.Target;

        if (target.Name == "out")
        {
            Complain(IssueCode.OutputCannotBeOff, target.Line, target.Column,
                "the Output cannot be switched off. Switch off what is patched into it, "
                + "or write 'out.volume = 0'.");
            return;
        }

        if (scope.Find(target.Name) is not { } value)
        {
            Unknown(target);
            return;
        }

        if (value is Failed) return;

        if (value is not Placed placed || patch.Find(placed.Id) is not { } node)
        {
            Complain(IssueCode.NotAModule, target.Line, target.Column,
                $"'{target.Name}' is not a module, so there is nothing to switch off.");
            return;
        }

        mentions.Add((new Site(target.Line, target.Column), node.Id));
        node.Off = true;
    }

    /// <summary>The plugins a patch says it needs, checked against the ones this build has.</summary>
    private void Require(RequiresStatement statement)
    {
        foreach (var plugin in statement.Plugins)
        {
            if (modules.HasProvider(plugin) || missing.Contains(plugin, StringComparer.Ordinal)) continue;

            missing.Add(plugin);

            Complain(IssueCode.MissingPlugin, statement.Line, statement.Column,
                $"this build has no plugin '{plugin}', which this patch needs. Install it, or open the patch where it is.");
        }
    }

    /// <summary>
    /// A knob on the patch's panel, named like a <c>let</c> so the sockets that
    /// follow it can say so.
    /// </summary>
    private void Declare(PanelStatement statement, Scope scope)
    {
        var (line, column) = (statement.Line, statement.Column);

        // Stamped out per call, a def would put a knob on the panel per call.
        if (expanding.Count > 0)
        {
            Complain(IssueCode.PanelInDef, line, column,
                "a panel knob belongs to the patch, so it is declared outside a def and passed in.");
            return;
        }

        if (!Free(statement.Name, scope, line, column)) return;

        // A knob given a range is written like a call, so it cannot share a
        // module's name or a def's.
        if (Known(statement.Name))
        {
            Complain(IssueCode.ReservedName, line, column,
                $"'{statement.Name}' is already a module's name, and a knob is called like one. "
                + $"Call this something else, such as '{statement.Name}_knob'.");
            return;
        }

        if (Bind(statement.Value, scope) is not Figure { Style: NumberStyle.Plain } resting
            || resting.Amount is < 0d or > 1d or double.NaN)
        {
            Complain(IssueCode.OutOfRange, line, column,
                "a panel knob rests somewhere from 0 to 1, and the sockets that follow it say what that means to them.");
            return;
        }

        string? label = null;
        string? device = null;
        int? controller = null;
        var channel = 0;

        foreach (var setting in statement.Settings)
        {
            switch (setting.Name, setting.Value)
            {
                case ("label", TextExpr text):
                    label = text.Value;
                    break;

                case ("device", TextExpr text):
                    device = text.Value;
                    break;

                case ("cc", NumberExpr { Value: >= 0 and <= 127 } number) when number.Value % 1 == 0:
                    controller = (int)number.Value;
                    break;

                case ("channel", NumberExpr { Value: >= 1 and <= 16 } number) when number.Value % 1 == 0:
                    channel = (int)number.Value;
                    break;

                default:
                    Complain(IssueCode.UnknownSetting, setting.Line, setting.Column,
                        "a panel knob takes 'label: \"…\"', 'cc: 0 to 127', 'channel: 1 to 16' and 'device: \"…\"'.");
                    return;
            }
        }

        if (controller is null && (device is not null || channel != 0))
        {
            Complain(IssueCode.UnknownSetting, line, column,
                "'device' and 'channel' say which controller 'cc' is, so they come with one.");
            return;
        }

        if (controller is not null && device is null)
        {
            Complain(IssueCode.UnknownSetting, line, column,
                "a controller is known by its device: add 'device: \"…\"', as the instrument's profile names it.");
            return;
        }

        var control = new PatchControl
        {
            Id = PanelId(statement.Name),
            Name = label ?? statement.Name,
            Word = label is null || label == statement.Name ? null : statement.Name,
            Value = (float)resting.Amount,
            Midi = controller is { } cc ? new MidiBinding(device!, channel, cc) : null,
        };

        (patch.Controls ??= []).Add(control);
        scope.Set(statement.Name, new Dial(control, statement.Name), line);

        // So a knob turned on the panel can be written back where it rests.
        written[(control.Id, PatchPrinter.PanelKnob)] = resting.Where;
    }

    /// <summary>A panel knob called with the range a socket reads it over: <c>cutoff(200..4000, knee: 20)</c>.</summary>
    private Value? Ranged(Dial dial, CallExpr expr, Scope scope, Value? piped)
    {
        var (line, column) = (expr.Line, expr.Column);

        if (piped is not null)
        {
            return Refuse(IssueCode.PanelNotASignal, line, column,
                $"'{dial.Word}' is a panel knob, which a socket follows; nothing is piped into one.");
        }

        Figure? low = null;
        Figure? high = null;
        Figure? knee = null;

        foreach (var argument in expr.Arguments)
        {
            if (argument is { Name: null, Value: RangeExpr range } && low is null
                && Bind(range.Low, scope) is Figure from && Bind(range.High, scope) is Figure to)
            {
                (low, high) = (from, to);
                continue;
            }

            if (argument.Name == "knee" && knee is null && Bind(argument.Value, scope) is Figure { Style: NumberStyle.Plain } bend)
            {
                knee = bend;
                continue;
            }

            return Refuse(IssueCode.UnknownSetting, argument.Line, argument.Column,
                $"a socket reads a panel knob over a range, and a knee where it sweeps in decades: '{dial.Word}(200..4000, knee: 20)'.");
        }

        if (knee is not null && low is null)
        {
            return Refuse(IssueCode.UnknownSetting, line, column,
                $"a knee comes with the range it bends: '{dial.Word}(200..4000, knee: 20)'.");
        }

        return dial with { Low = low, High = high, Knee = knee };
    }

    private void Box(GroupStatement statement, Scope scope)
    {
        var before = patch.Nodes.Select(n => n.Id).ToHashSet();
        var said = issues.Count;
        var inner = new Scope(scope);

        foreach (var child in statement.Body)
        {
            switch (child)
            {
                // A box is drawn round modules, and each of these is about the
                // whole patch or the panel, which no box holds.
                case GroupStatement nested:
                    Complain(IssueCode.GroupInGroup, nested.Line, nested.Column,
                        "a group cannot hold another group. Close this one first.");
                    Unmade(Declared(nested.Body), scope, nested.Line);
                    break;

                case PanelStatement panel:
                    Complain(IssueCode.PanelInGroup, panel.Line, panel.Column,
                        "a panel knob belongs to the whole patch, so it is declared outside a group.");
                    Unmade([panel.Name], scope, panel.Line);
                    break;

                case RequiresStatement requires:
                    Complain(IssueCode.RequiresInGroup, requires.Line, requires.Column,
                        "what a patch requires is said once, outside every group, before anything else.");
                    break;

                default:
                    Run(child, inner);
                    break;
            }
        }

        // Everything placed while the block was open, which is what "declared
        // inside it" means once a def has been expanded in there too. The clock
        // and the coordinates a bare word reaches for are the whole patch's, so
        // they are in no box however early a block reads them.
        var made = patch.Nodes
            .Where(n => !before.Contains(n.Id) && n.Id != clock && n.Id != coordinates)
            .Select(n => n.Id)
            .ToList();

        // A name opened again is the same group, which is how a printing says one
        // whose modules do not come out next to each other.
        var index = statement.Name is null ? -1 : boxes.FindIndex(box => box.Name == statement.Name);

        if (index < 0)
        {
            index = boxes.Count;
            boxes.Add((statement.Name, []));
        }

        boxes[index].Members.AddRange(made);

        if (issues.Count > said) troubled.Add(index);
        opened.Add((new Site(statement.Line, statement.Column), index));

        // A group is a box on the canvas and nothing more, so the names it made
        // go on being visible after it — which is what lets one group wire into
        // the next, as the largest preset does throughout.
        foreach (var name in Declared(statement.Body.Where(child => child is not GroupStatement)))
            if (inner.Entry(name) is { } item) scope.Set(name, item.Value, item.Line);
    }

    /// <summary>The names the statements bind with <c>let</c>, in groups within them too.</summary>
    private static IEnumerable<string> Declared(IEnumerable<Statement> statements) =>
        statements.SelectMany(statement => statement switch
        {
            LetStatement let => [let.Name],
            LetTupleStatement tuple => tuple.Names,
            GroupStatement group => Declared(group.Body),
            _ => [],
        });

    // --- expressions ---------------------------------------------------------

    private Value? Bind(Expr expr, Scope scope) => expr switch
    {
        NumberExpr number => new Figure(number.Value, number.Style, new Site(number.Line, number.Column)),
        TextExpr text => new Named(text.Value),
        NegateExpr or BinaryExpr => Arithmetic(expr, scope),
        NameExpr name => Read(name, scope),
        CallExpr call => Call(call, scope, piped: null),
        SelectExpr select => Select(select, scope, piped: null),
        PipeExpr pipe => Pipe(pipe, scope),
        RangeExpr range => Refuse(IssueCode.RangeOutsideArgument, range.Line, range.Column, "a range only means something as an argument."),
        _ => null,
    };

    private Value? Refuse(string code, int line, int column, string message)
    {
        Complain(code, line, column, message);
        return null;
    }

    // --- arithmetic ---------------------------------------------------------

    /// <summary>
    /// Arithmetic as it is being read: numbers still to be folded, the signals
    /// between them, and the operators that join them.
    /// </summary>
    private abstract record Reckoned;

    private sealed record Operand(Figure Figure) : Reckoned;

    /// <param name="From">The output it is, which is what makes a signal read twice one socket.</param>
    private sealed record Signal(Value Value, (Guid Node, int Port) From) : Reckoned;

    /// <param name="Right">Null for a minus in front.</param>
    private sealed record Operation(char Sign, Reckoned Left, Reckoned? Right, int Line, int Column) : Reckoned;

    /// <summary>
    /// Infix arithmetic, which is one Expression however much of it there is —
    /// except between two numbers, where it is neither a module nor a wire but a
    /// knob that has already been worked out.
    /// </summary>
    /// <remarks>
    /// The whole tree is one formula over the signals in it, so <c>(fract(t * 60)
    /// * 2 - 1) * aspect</c> is two Expressions and a Fraction rather than four
    /// Maths modules and a Fraction. A tree reading more signals than an
    /// Expression has sockets hands its busier side to an Expression of its own.
    /// </remarks>
    private Value? Arithmetic(Expr expr, Scope scope) => Reckon(expr, scope) switch
    {
        Operand operand => operand.Figure,
        Signal signal => signal.Value,
        Operation operation => Expression(operation),
        _ => null,
    };

    private Reckoned? Reckon(Expr expr, Scope scope)
    {
        switch (expr)
        {
            case NegateExpr negate:
            {
                if (Reckon(negate.Value, scope) is not { } value) return null;

                // From the minus rather than from the digits, because the sign is
                // part of the number as far as anything rewriting it is concerned.
                if (value is Operand operand)
                {
                    return new Operand(operand.Figure with
                    {
                        Amount = -operand.Figure.Amount,
                        Where = new Site(negate.Line, negate.Column),
                    });
                }

                return Fit(new Operation('-', value, null, negate.Line, negate.Column));
            }

            case BinaryExpr binary:
            {
                if (Reckon(binary.Left, scope) is not { } left) return null;
                if (Reckon(binary.Right, scope) is not { } right) return null;

                if (left is Operand a && right is Operand b) return Folded(binary, a.Figure, b.Figure);

                var sign = binary.Operator switch
                {
                    TokenKind.Plus => '+',
                    TokenKind.Minus => '-',
                    TokenKind.Star => '*',
                    TokenKind.Slash => '/',
                    _ => '%',
                };

                return Fit(new Operation(sign, left, right, binary.Line, binary.Column));
            }

            default:
            {
                if (Bind(expr, scope) is not { } value) return null;

                if (value is Figure figure) return new Operand(figure);

                // A panel knob is a socket of the Expression that follows it, one
                // per mention since each may read it over a range of its own.
                if (value is Dial) return new Signal(value, (Guid.NewGuid(), 0));

                if (Output(value) is { } from) return new Signal(value, from);

                Complain(IssueCode.NotASignal, expr.Line, expr.Column, "this is not a signal, so nothing can be wired from it.");
                return null;
            }
        }
    }

    /// <summary>
    /// Two numbers made one, here rather than emitted, so a knob written as 1/12
    /// is a knob and not a Divide. A note or a duration is on a scale of its own,
    /// so arithmetic on one is refused rather than quietly done in semitones or
    /// decades.
    /// </summary>
    private Operand? Folded(BinaryExpr expr, Figure a, Figure b)
    {
        if (a.Style != NumberStyle.Plain || b.Style != NumberStyle.Plain)
        {
            Complain(IssueCode.ScaledArithmetic, expr.Line, expr.Column, Scaled);
            return null;
        }

        var folded = expr.Operator switch
        {
            TokenKind.Plus => a.Amount + b.Amount,
            TokenKind.Minus => a.Amount - b.Amount,
            TokenKind.Star => a.Amount * b.Amount,
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            TokenKind.Slash => b.Amount == 0d ? 0d : a.Amount / b.Amount,
            // The remainder Modulo takes, which wraps a negative number upwards.
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            _ => b.Amount == 0d ? 0d : a.Amount - b.Amount * Math.Floor(a.Amount / b.Amount),
        };

        return new Operand(new Figure(folded, NumberStyle.Plain));
    }

    private const string Scaled = "arithmetic on a note or a duration would be done on a scale nobody meant.";

    /// <summary>Which output a signal is, and null for what is not one.</summary>
    private static (Guid Node, int Port)? Output(Value value) => value switch
    {
        Placed placed => (placed.Id, 0),
        Socket socket => (socket.Id, socket.Port),
        Several { Items.Count: > 0 } several => Output(several.Items[0]),
        _ => null,
    };

    /// <summary>
    /// The operation as it is, if it reads no more signals than an Expression has
    /// sockets; otherwise with its busier side made an Expression of its own, and
    /// then the other, until it does.
    /// </summary>
    private Reckoned? Fit(Operation operation)
    {
        while (Inputs(operation).Count > Formula.Sockets.Length)
        {
            var left = Inputs(operation.Left).Count;
            var right = operation.Right is null ? 0 : Inputs(operation.Right).Count;

            if (left >= right)
            {
                if (Settled(operation.Left) is not { } settled) return null;
                operation = operation with { Left = settled };
            }
            else
            {
                if (Settled(operation.Right!) is not { } settled) return null;
                operation = operation with { Right = settled };
            }
        }

        return operation;
    }

    /// <summary>An operation placed as the Expression it is, and read from then on as its output.</summary>
    private Reckoned? Settled(Reckoned reckoned) =>
        reckoned is not Operation operation ? reckoned
        : Expression(operation) is { } placed && Output(placed) is { } from ? new Signal(placed, from)
        : null;

    /// <summary>The signals a tree reads, each once, in the order the formula names them.</summary>
    private static List<Signal> Inputs(Reckoned reckoned)
    {
        var found = new List<Signal>();

        Gather(reckoned);
        return found;

        void Gather(Reckoned part)
        {
            switch (part)
            {
                case Signal signal when found.All(seen => seen.From != signal.From):
                    found.Add(signal);
                    break;

                case Operation operation:
                    Gather(operation.Left);
                    if (operation.Right is not null) Gather(operation.Right);
                    break;
            }
        }
    }

    /// <summary>
    /// Places the Expression an operation is: its signals on a, b, c and d in the
    /// order they are first read, and the rest written into its formula.
    /// </summary>
    private Value? Expression(Operation operation)
    {
        var inputs = Inputs(operation);

        if (Written(operation, inputs) is not { } formula) return null;
        if (Module(NodeCatalog.ExpressionTypeId, operation.Line, operation.Column) is not { } def) return null;

        var placed = Place(def, [.. inputs.Select((input, socket) => (socket, input.Value))], operation.Line, operation.Column);

        if (placed is Placed { Id: var id } && patch.Find(id) is { } node)
        {
            node.SetState(FormulaExtra.StateKey, new JsonObject { [FormulaExtra.FormulaField] = formula });

            // Where the sum's operator stands, so the text points at the module it
            // placed. Not a call: there are no brackets to write a knob into.
            mentions.Add((new Site(operation.Line, operation.Column), id));
        }

        return placed;
    }

    /// <summary>
    /// An operation as a formula, bracketed wherever reading it back would group
    /// it differently — which, for the right of an operator of the same strength,
    /// is always: <c>a - (b - c)</c> and <c>a + (b + c)</c> are what was written,
    /// and floats do not reassociate.
    /// </summary>
    private string? Written(Reckoned reckoned, IReadOnlyList<Signal> inputs)
    {
        switch (reckoned)
        {
            case Operand { Figure: var figure }:
            {
                var where = figure.Where ?? new Site(0, 0);

                if (figure.Style != NumberStyle.Plain)
                {
                    Complain(IssueCode.ScaledArithmetic, where.Line, where.Column, Scaled);
                    return null;
                }

                var value = (float)figure.Amount;

                if (!float.IsFinite(value))
                {
                    Complain(IssueCode.NumberTooLarge, where.Line, where.Column, "that number is too large to hold.");
                    return null;
                }

                return value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            }

            case Signal signal:
                return Formula.Sockets[inputs.ToList().FindIndex(input => input.From == signal.From)].ToString();

            case Operation { Right: null } negate:
            {
                if (Written(negate.Left, inputs) is not { } operand) return null;

                return Strength(negate.Left) < Strength(negate) ? $"-({operand})" : $"-{operand}";
            }

            case Operation operation:
            {
                if (Written(operation.Left, inputs) is not { } left) return null;
                if (Written(operation.Right!, inputs) is not { } right) return null;

                var strength = Strength(operation);

                if (Strength(operation.Left) < strength) left = $"({left})";
                if (Strength(operation.Right!) <= strength) right = $"({right})";

                return $"{left} {operation.Sign} {right}";
            }

            default:
                return null;
        }
    }

    /// <summary>How tightly a part of a formula holds together: a sum least, a number or a socket most.</summary>
    private static int Strength(Reckoned reckoned) => reckoned switch
    {
        Operation { Right: null } => 3,
        Operation { Sign: '+' or '-' } => 1,
        Operation => 2,
        Operand { Figure.Amount: < 0 } => 3,
        _ => 4,
    };

    private Value? Read(NameExpr expr, Scope scope)
    {
        // The coordinates and the clock are one shared node each, however
        // often they are written — a patch that reads the clock in eight places
        // has one Time in it, which is what every preset does by hand.
        if (expr.Port is null && Source(expr.Name) is { } source) return source;

        if (expr.Name == "out")
        {
            return Refuse(IssueCode.OutputIsNotASource, expr.Line, expr.Column,
                "the Output has nothing to read. Pipe something into 'out.color' or 'out.left'.");
        }

        if (Placeholder(expr))
            return Refuse(IssueCode.PlaceholderMisplaced, expr.Line, expr.Column, "'_' stands for what is piped in, as a call's argument: 'socket: _'.");

        if (scope.Find(expr.Name) is not { } value)
        {
            Unknown(expr);
            return null;
        }

        if (value is Failed) return null;

        // Reading a name is naming the module, so the word is somewhere to click
        // even though nothing is placed here.
        Mention(expr, value);

        if (expr.Port is null) return value;

        if (value is not Placed)
            return Refuse(IssueCode.NotAModule, expr.Line, expr.Column, $"'{expr.Name}' is not a module, so it has no sockets.");

        return Output(value, expr.Port, expr.Line, expr.Column);
    }

    private void Unknown(NameExpr expr)
    {
        if (boundOn.TryGetValue(expr.Name, out var line) && line > expr.Line)
        {
            Complain(IssueCode.UsedBeforeBound, expr.Line, expr.Column,
                $"'{expr.Name}' is bound on line {line}, below where it is read. Bind it before reading it.");
            return;
        }

        if (expr.Port is null && StatementWord(expr.Name) is { } example)
        {
            Complain(IssueCode.UnknownName, expr.Line, expr.Column,
                $"'{expr.Name}' starts a statement only with what it says after it: {example}.");
            return;
        }

        Complain(IssueCode.UnknownName, expr.Line, expr.Column, $"nothing here is called '{expr.Name}'.");
    }

    /// <summary>How a statement a word begins is written, for that word on its own.</summary>
    private static string? StatementWord(string name) => name switch
    {
        "requires" => "requires flyback.picture",
        "keyboard" => "keyboard piano",
        "off" => "off drone",
        "description" => "description \"A slow drone\"",
        "author" => "author \"Ada\"",
        "tags" => "tags \"drone\" \"slow\"",
        "panel" => "panel level = 0.5",
        _ => null,
    };

    /// <summary>
    /// One output of what an expression placed, which is a binding's selector
    /// without the binding: <c>tempo(bpm: 104).beats</c>.
    /// </summary>
    /// <param name="piped">What is arriving, where the expression is a stage of a pipeline.</param>
    private Value? Select(SelectExpr expr, Scope scope, Value? piped)
    {
        var source = expr.Source switch
        {
            CallExpr call => Call(call, scope, piped),
            SelectExpr inner => Select(inner, scope, piped),
            _ => Bind(expr.Source, scope),
        };

        return source is null ? null : Output(source, expr.Port, expr.Line, expr.Column);
    }

    /// <summary>The output of <paramref name="value"/> called <paramref name="name"/>.</summary>
    private Value? Output(Value value, string name, int line, int column)
    {
        // A def may hand back one output, several things or a number, and none
        // of those has outputs of its own to choose between.
        if (value is not Placed placed)
            return Refuse(IssueCode.NotAModule, line, column, $"this is not a module, so it has no output called '{name}'.");

        var port = Find(placed.Def.Outputs, name);

        if (port < 0)
        {
            return Refuse(IssueCode.UnknownOutput, line, column,
                $"'{placed.Def.Name}' has no output called '{name}'. It has {List(placed.Def.Outputs)}.");
        }

        return new Socket(placed.Id, port);
    }

    /// <summary>What a word every patch already has stands for, if it is one.</summary>
    private static string? Builtin(string name) => name switch
    {
        "t" => "the clock",
        "x" or "y" or "radius" or "angle" or "aspect" => "one of the picture's coordinates",
        "out" => "the Output",
        "_" => "what a pipe brings in",
        "let" or "def" or "group" => "a word of the language",
        _ => null,
    };

    /// <summary>The shared Coordinates or Time a bare word stands for, if it is one.</summary>
    private Value? Source(string name)
    {
        var port = name switch
        {
            "x" => NodeCatalog.CoordXPort,
            "y" => NodeCatalog.CoordYPort,
            "radius" => 2,
            "angle" => 3,
            "aspect" => NodeCatalog.CoordAspectPort,
            _ => -1,
        };

        if (port >= 0) return new Socket(Shared(ref coordinates, NodeCatalog.CoordTypeId), port);

        return name == "t" ? new Socket(Shared(ref clock, NodeCatalog.TimeTypeId), 0) : null;
    }

    private Guid Shared(ref Guid held, string typeId)
    {
        if (held != Guid.Empty) return held;

        // Named for what it is rather than for where it was first mentioned:
        // there is one clock and one pair of coordinates in a patch however many
        // lines reach for them, and moving the first mention should not make it
        // a different module.
        var node = NodeInstance.Create(modules.Require(typeId), 0d, 0d, Identity("~" + typeId));

        patch.Nodes.Add(node);
        held = node.Id;

        return held;
    }

    // --- the pipe rule -------------------------------------------------------

    private Value? Pipe(PipeExpr expr, Scope scope)
    {
        if (Bind(expr.Source, scope) is not { } value)
        {
            // The socket is still checked, so one pass finds both mistakes.
            if (expr.Stage is NameExpr { Port: not null } unreached) Input(unreached, scope);

            return null;
        }

        // A knob is followed, never piped: carried into a call it would link
        // whatever socket the pipe happened to land on.
        if (value is Dial dial)
        {
            return Refuse(IssueCode.PanelNotASignal, expr.Source.Line, expr.Source.Column,
                $"'{dial.Word}' is a panel knob, which a socket follows where a number would go: 'freq: {dial.Word}'.");
        }

        // A pipe into a socket is a wire into it, which is what puts a patch on
        // the screen: '|> out.color'.
        if (expr.Stage is NameExpr socket)
        {
            // A module named after a pipe with no brackets is that module, placed
            // and taking nothing but what is arriving — the obvious way to write
            // it in a language made of pipes. Refusing it cost a whole run, since
            // every binding after the complaint went unread.
            if (socket.Port is null && scope.Find(socket.Name) is null && Known(socket.Name))
                return Call(new CallExpr(socket.Name, [], null, socket.Line, socket.Column), scope, value);

            if (socket.Port is null)
            {
                return Refuse(IssueCode.SocketUnsaid, socket.Line, socket.Column,
                    $"'{socket.Name}' is a module, not a socket. Say which one to wire into.");
            }

            if (Input(socket, scope) is not var (node, _, port)) return null;

            Feed(value, 0, node.Id, port, socket.Line, socket.Column);
            return value;
        }

        if (expr.Stage is CallExpr call) return Call(call, scope, value);

        // 'beats |> notes() [ A3 C4 ].gate' is the sequencer with the beats
        // arriving, and then its gate: the selector binds tighter than the pipe,
        // so it is the stage's output that is chosen and not the source's.
        if (expr.Stage is SelectExpr select) return Select(select, scope, value);

        return Refuse(IssueCode.BadStage, expr.Line, expr.Column, "only a module or a socket may follow '|>'.");
    }

    /// <summary>How many signals a value carries when it is piped.</summary>
    private static int Width(Value value) => value switch
    {
        Placed placed => placed.Def.Outputs.Count,
        Several several => several.Items.Count,
        _ => 1,
    };

    /// <summary>The <paramref name="index"/>th signal of a value, for wiring.</summary>
    private static Value Part(Value value, int index) => value switch
    {
        Placed placed => new Socket(placed.Id, index),
        Several several => several.Items[index],
        _ => value,
    };

    private Value? Call(CallExpr expr, Scope scope, Value? piped)
    {
        if (scope.Find(expr.Target) is Failed) return null;
        if (scope.Find(expr.Target) is Dial dial) return Ranged(dial, expr, scope, piped);
        if (defs.TryGetValue(expr.Target, out var macro)) return Expand(macro, expr, scope, piped);

        if (Module(expr.Target, expr.Line, expr.Column) is not { } def) return null;

        if (NodeCatalog.IsSink(def.TypeId))
        {
            return Refuse(IssueCode.OutputIsNotASource, expr.Line, expr.Column,
                "every patch already has its Output. Wire into 'out.color' or 'out.left'.");
        }

        var taken = new HashSet<int>();
        var wiring = new List<(int Port, Value Value)>();

        // Where each argument's value is written, so a complaint about one points at it.
        var sites = new Dictionary<int, Site>();
        var paths = new List<(string Path, int Line, int Column)>();
        var fields = new List<(NodeExtra Owner, ExtraField Field, JsonNode Value, Site Where)>();
        int? landing = null;

        // Named arguments first, because what they claim decides where
        // everything else can go.
        foreach (var argument in expr.Arguments)
        {
            if (argument.Name is null)
            {
                if (Placeholder(argument.Value))
                {
                    Complain(IssueCode.PlaceholderMisplaced, argument.Line, argument.Column,
                        "'_' goes in a named argument, 'socket: _', so it says which socket.");
                }

                continue;
            }

            var port = Find(def.Inputs, argument.Name);

            if (port < 0)
            {
                if (Field(def, argument.Name) is var (owner, field))
                {
                    if (Setting(field, argument, scope) is { } setting)
                    {
                        var where = new Site(argument.Value.Line, argument.Value.Column);

                        fields.Add((owner, field, setting, where));
                    }

                    continue;
                }

                Complain(IssueCode.UnknownSocket, argument.Line, argument.Column,
                    $"'{def.Name}' has no socket called '{argument.Name}'. It has {List(def.Inputs)}.");
                continue;
            }

            if (!taken.Add(port))
            {
                Complain(IssueCode.GivenTwice, argument.Line, argument.Column, $"'{argument.Name}' is given twice.");
                continue;
            }

            if (Placeholder(argument.Value))
            {
                if (piped is null)
                    Complain(IssueCode.PlaceholderMisplaced, argument.Line, argument.Column, "'_' stands for what is piped in, and nothing is.");
                else if (landing is not null)
                    Complain(IssueCode.PlaceholderTwice, argument.Line, argument.Column, "'_' is written twice, and a pipe brings one signal.");
                else
                    landing = port;

                continue;
            }

            if (!Piped(argument) && Bind(argument.Value, scope) is { } value)
            {
                wiring.Add((port, value));
                sites[port] = new Site(Leftmost(argument.Value).Line, Leftmost(argument.Value).Column);
            }
        }

        var piping = new List<(int Port, Value Value)>();

        if (landing is { } at)
            piping.Add((at, Part(piped!, 0)));
        else if (piped is not null && !Land(def, piped, taken, expr, piping))
            return null;

        // Whatever the pipe and the named arguments left, in order.
        var free = new Queue<int>(Enumerable.Range(0, def.Inputs.Count).Where(i => !taken.Contains(i)));

        foreach (var argument in expr.Arguments)
        {
            if (argument.Name is not null || Placeholder(argument.Value)) continue;

            if (argument.Value is TextExpr text)
            {
                paths.Add((text.Value, argument.Line, argument.Column));
                continue;
            }

            // A range is two arguments written as one, which is what makes
            // remap(-2..2, 0..1) four sockets and two commas.
            var parts = argument.Value is RangeExpr range ? new[] { range.Low, range.High } : [argument.Value];
            var refused = Piped(argument);

            foreach (var part in parts)
            {
                if (free.Count == 0)
                {
                    Complain(IssueCode.TooManyArguments, argument.Line, argument.Column,
                        $"'{def.Name}' has no socket left for this. It has {List(def.Inputs)}.");
                    break;
                }

                var port = free.Dequeue();
                taken.Add(port);

                if (!refused && Bind(part, scope) is { } value)
                {
                    wiring.Add((port, value));
                    sites[port] = new Site(Leftmost(part).Line, Leftmost(part).Column);
                }
            }
        }

        var node = Place(def, [.. piping, .. wiring], expr.Line, expr.Column, sites);

        if (node is Placed made)
        {
            // Where this module stands in the file. The call rather than the
            // binding, because a call is what a module is: four of them on one
            // line are four modules to point at.
            var site = new Site(expr.Line, expr.Column);

            calls[made.Id] = site;
            mentions.Add((site, made.Id));

            if (patch.Find(made.Id) is { } instance)
                foreach (var (owner, field, setting, where) in fields)
                {
                    Apply(instance, owner, field, setting);

                    // By the key rather than by whichever of key and label the
                    // file happened to use, since a label is free to be reworded
                    // and a caller asking for the field will have the key.
                    written[(made.Id, field.Key)] = where;
                }
        }

        foreach (var (path, line, column) in paths) File(node, def, path, line, column);
        if (expr.Block is { } block) Carry(node, def, block, expr.Line, expr.Column, (expr.BlockLine, expr.BlockColumn));

        return node;
    }

    /// <summary>
    /// Where a pipe lands when no argument says <c>_</c>: a socket called
    /// <c>in</c>, else a module's only socket, else a leading <c>x</c> and
    /// <c>y</c>, else the one color socket where a color is arriving, else
    /// nowhere.
    /// </summary>
    /// <remarks>
    /// Nowhere is an error rather than a guess at the first socket left. That
    /// guess wired a signal into <c>math.smoothstep</c>'s <c>edge0</c> and read
    /// perfectly, and which socket was "left" hung on the arguments beside it.
    /// </remarks>
    private bool Land(NodeDef def, Value piped, HashSet<int> taken, CallExpr expr, List<(int, Value)> into)
    {
        var signal = Find(def.Inputs, "in");

        // 'in' is one signal by definition, so a module that has one is a scalar
        // position and takes the source's first output — the same thing a bare
        // name means anywhere else a single value is wanted. A MIDI In has four
        // outputs and 'keys |> clamp(36, 84)' means its pitch, which falls out
        // of this rather than being a case anyone had to add.
        // A module with one socket has nowhere else for it to go.
        var sole = signal >= 0 ? signal : def.Inputs.Count == 1 ? 0 : -1;

        if (sole >= 0 && !taken.Contains(sole))
        {
            taken.Add(sole);
            into.Add((sole, Part(piped, 0)));

            return true;
        }

        var free = Enumerable.Range(0, def.Inputs.Count).Where(i => !taken.Contains(i)).ToList();

        if (free.Count == 0)
        {
            Complain(IssueCode.NoSocketFree, expr.Line, expr.Column, $"'{def.Name}' has no socket free for what is arriving.");
            return false;
        }

        // A position takes two, and it is the only thing that does: a module
        // whose own first sockets are 'x' and 'y' takes what the engine calls a
        // position — the pair ADR-0050 normals to Coordinates together — so a
        // Space chains off another without either end saying so. The module's
        // first two, not the first two the call left, so what the arguments
        // name never moves the landing.
        //
        // Forwarding every output a source has was the alternative, and
        // 'steps |> note(note: _)' rules it out: the sequencer's gate would land in
        // Note's octave and its index in the cents, which reads perfectly and is
        // not a tune.
        var position = def.Inputs.Count >= 2
            && Same(def.Inputs[0].Name, "x")
            && Same(def.Inputs[1].Name, "y")
            && !taken.Contains(0)
            && !taken.Contains(1)
            && Width(piped) >= 2;

        // A color into a module that takes exactly one color can only mean that
        // one, whatever else the module has.
        var colors = Enumerable.Range(0, def.Inputs.Count).Where(i => def.Inputs[i].Kind == PortKind.Color).ToList();

        if (!position && colors is [var tint] && !taken.Contains(tint) && KindOf(piped) == PortKind.Color)
        {
            taken.Add(tint);
            into.Add((tint, Part(piped, 0)));

            return true;
        }

        if (!position)
        {
            var example = def.Inputs[free[0]].Name.Replace(' ', '_');
            var why = signal >= 0 ? "its 'in' is already given" : "it has no socket called 'in'";

            Complain(IssueCode.PipeLandsNowhere, expr.Line, expr.Column,
                $"'{def.Name}': {why}, so say where the pipe lands: "
                + $"'{expr.Target}({example}: _)'. It has {List(def.Inputs)}.");
            return false;
        }

        taken.Add(0);
        taken.Add(1);
        into.Add((0, Part(piped, 0)));
        into.Add((1, Part(piped, 1)));

        return true;
    }

    /// <summary>
    /// What the first signal of a value is declared as, or null where nothing
    /// declares it — a number, or a Maths module passing on whatever it reads.
    /// </summary>
    private PortKind? KindOf(Value value) => value switch
    {
        Placed placed => placed.Def.Outputs.Count > 0 ? placed.Def.Outputs[0].Kind : null,
        Socket socket => patch.Find(socket.Id) is { } node && modules.Get(node.TypeId) is { } def
            && socket.Port < def.Outputs.Count
                ? def.Outputs[socket.Port].Kind
                : null,
        Several several when several.Items.Count > 0 => KindOf(several.Items[0]),
        _ => null,
    };

    /// <summary>Whether an argument is <c>_</c>, which stands for what is piped in.</summary>
    private static bool Placeholder(Expr value) => value is NameExpr { Name: "_", Port: null };

    /// <summary>
    /// Whether an argument holds a pipeline, said where it does. A pipeline is a
    /// statement's spine, so one inside an argument is written as a <c>let</c>
    /// above and a name here, and every edit stays a one-line edit.
    /// </summary>
    private bool Piped(Argument argument)
    {
        if (!Pipes(argument.Value)) return false;

        var start = Leftmost(argument.Value);

        Complain(IssueCode.PipelineInArgument, start.Line, start.Column,
            "a pipeline cannot go inside an argument. Bind it with 'let' above and name it here.");
        return true;
    }

    /// <summary>Where an expression begins in the text, which an operator's own position is not.</summary>
    private static Expr Leftmost(Expr expr) => expr switch
    {
        PipeExpr pipe => Leftmost(pipe.Source),
        BinaryExpr binary => Leftmost(binary.Left),
        SelectExpr select => Leftmost(select.Source),
        RangeExpr range => Leftmost(range.Low),
        _ => expr,
    };

    private static bool Pipes(Expr expr) => expr switch
    {
        PipeExpr => true,
        BinaryExpr binary => Pipes(binary.Left) || Pipes(binary.Right),
        NegateExpr negate => Pipes(negate.Value),
        SelectExpr select => Pipes(select.Source),
        RangeExpr range => Pipes(range.Low) || Pipes(range.High),
        _ => false,
    };

    // --- placing and wiring --------------------------------------------------

    /// <param name="sites">Where the text gives each socket its value, where it gives it one; the call's own place otherwise.</param>
    private Value Place(NodeDef def, IReadOnlyList<(int Port, Value Value)> inputs, int line, int column, IReadOnlyDictionary<int, Site>? sites = null)
    {
        var node = NodeInstance.Create(def, 0d, 0d, Next());
        patch.Nodes.Add(node);

        foreach (var (port, value) in inputs)
        {
            var (atLine, atColumn) = sites is not null && sites.TryGetValue(port, out var site) ? (site.Line, site.Column) : (line, column);

            if (value is Figure figure) Knob(node, def, port, figure, atLine, atColumn);
            else if (value is Dial dial) Link(node, def, port, dial, atLine, atColumn);
            else if (value is Named) Complain(IssueCode.NotASignal, atLine, atColumn, $"'{def.Inputs[port].Name}' takes a number or a signal, not text.");
            else Feed(value, 0, node.Id, port, atLine, atColumn);
        }

        return new Placed(node.Id, def);
    }

    /// <summary>Wires one signal of <paramref name="value"/> into a socket.</summary>
    private void Feed(Value value, int index, Guid target, int port, int line, int column)
    {
        switch (value)
        {
            case Socket socket:
                Wire(socket.Id, socket.Port, target, port, line, column);
                break;

            case Placed placed:
                Wire(placed.Id, 0, target, port, line, column);
                break;

            case Several several when several.Items.Count > index:
                Feed(several.Items[index], 0, target, port, line, column);
                break;

            case Figure figure:
                if (patch.Find(target) is { } node && modules.Get(node.TypeId) is { } def)
                    Knob(node, def, port, figure, line, column);

                break;

            case Dial dial:
                Complain(IssueCode.PanelNotASignal, line, column,
                    $"'{dial.Word}' is a panel knob, which a socket follows where a number would go: 'freq: {dial.Word}'.");
                break;

            default:
                Complain(IssueCode.NotASignal, line, column, "this is not a signal, so nothing can be wired from it.");
                break;
        }
    }

    /// <summary>
    /// A wire, refused where the text already wired that socket: the graph keeps
    /// one, and dropping the other without a word is a patch that reads wrong.
    /// </summary>
    private void Wire(Guid source, int output, Guid target, int port, int line, int column)
    {
        if (wired.TryGetValue((target, port), out var first))
        {
            if (patch.IncomingTo(target, port) is { } wire && wire.SourceNode == source && wire.SourcePort == output) return;

            Complain(IssueCode.WiredTwice, line, column,
                $"'{SocketName(target, port)}' is already wired on line {first}. A socket takes one wire.");
            return;
        }

        wired[(target, port)] = line;
        patch.Connect(source, output, target, port);
    }

    /// <summary>A socket as the text would say it: the module's name, a dot and the socket.</summary>
    private string SocketName(Guid node, int port) =>
        patch.Find(node) is { } instance && modules.Get(instance.TypeId) is { } def
            ? (named.GetValueOrDefault(node) ?? def.Name) + "." + def.Inputs[port].Name.Replace(' ', '_')
            : "this socket";

    /// <summary>
    /// Sets a knob, having first asked whether the socket has one and whether it
    /// reads numbers on the scale this one was written on.
    /// </summary>
    private void Knob(NodeInstance node, NodeDef def, int port, Figure figure, int line, int column)
    {
        if (!Settable(node, def, port, line, column)) return;

        var spec = def.Inputs[port];

        if (!Reads(spec, figure, line, column)) return;

        node.InputValues[port] = (float)figure.Amount;
        turned[(node.Id, port)] = line;

        // Only once a knob has actually been set, so that a refused number is
        // not offered as a place to write another one into. By the socket's own
        // spelling, because that is what a caller asking for it will have.
        written[(node.Id, spec.Name.Replace(' ', '_'))] = figure.Where;
    }

    /// <summary>
    /// Has a socket follow a panel knob, over the range the text gives or, where
    /// it gives none, the socket's own.
    /// </summary>
    private void Link(NodeInstance node, NodeDef def, int port, Dial dial, int line, int column)
    {
        if (!Settable(node, def, port, line, column)) return;

        var spec = def.Inputs[port];
        ControlLink link;

        if (dial is { Low: { } low, High: { } high })
        {
            if (!Reads(spec, low, line, column) || !Reads(spec, high, line, column)) return;

            link = new ControlLink(dial.Control.Id, (float)low.Amount, (float)high.Amount)
            {
                Knee = dial.Knee is { } knee ? (float)knee.Amount : spec.Knee,
            };
        }
        else
        {
            link = ControlLink.For(dial.Control.Id, spec, node.InputValues[port]);
        }

        ControlMap.Link(node, port, link);
        turned[(node.Id, port)] = line;
    }

    /// <summary>Whether a socket's knob may be set here, said where it may not.</summary>
    private bool Settable(NodeInstance node, NodeDef def, int port, int line, int column)
    {
        if (port < 0 || port >= def.Inputs.Count) return false;

        // One number per knob: a second would win without a word, and the first
        // would read as though it still counted.
        if (turned.TryGetValue((node.Id, port), out var first))
        {
            Complain(IssueCode.KnobSetTwice, line, column,
                $"'{SocketName(node.Id, port)}' is already set on line {first}. A knob is set once.");
            return false;
        }

        var spec = def.Inputs[port];

        // A normalled socket is already carrying something and the stored value
        // is never read, so a number here would be a knob nobody can turn
        // (ADR-0050). The same refusal the assistant's set_knobs makes.
        if (modules.Normalled(spec) is { } driver)
        {
            Complain(IssueCode.NormalledSocket, line, column,
                $"'{spec.Name}' is normalled to {driver} and has no knob. "
                + "Patch a Value in if it really should stand still.");
            return false;
        }

        return true;
    }

    /// <summary>Whether a number is written the way a socket reads, said where it is not.</summary>
    private bool Reads(PortSpec spec, Figure figure, int line, int column)
    {
        var wanted = figure.Style switch
        {
            NumberStyle.Note => PortDisplay.Note,
            NumberStyle.Duration => PortDisplay.Duration,
            _ => spec.Display,
        };

        if (wanted != spec.Display)
        {
            var written = figure.Style == NumberStyle.Note ? "a note" : "a length of time";

            Complain(IssueCode.WrongLiteral, line, column, $"'{spec.Name}' is not read as {written}.");
            return false;
        }

        // A bare number on a socket that holds time is the trap the literal was
        // added to remove: the socket holds a power of ten, so "attack: 0.01"
        // meaning ten milliseconds is a second, and the drum is a drone. Nothing
        // about the value says which was meant, so the complaint says both.
        if (spec.Display == PortDisplay.Duration && figure.Style == NumberStyle.Plain)
        {
            Complain(IssueCode.BareDuration, line, column,
                $"'{spec.Name}' is a length of time, and a bare number on one is a power of ten: "
                + $"{Number(figure.Amount)} means {spec.Format((float)figure.Amount)}. "
                + $"Write {Literal(figure.Amount)} if you meant {Number(figure.Amount)} seconds.");
            return false;
        }

        if (!double.IsFinite(figure.Amount))
        {
            Complain(IssueCode.OutOfRange, line, column, $"'{spec.Name}' cannot hold that.");
            return false;
        }

        return true;
    }

    /// <summary>A number as it was written, for saying it back in a complaint.</summary>
    private static string Number(double value) =>
        value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// <paramref name="seconds"/> written as the literal that would mean it, for
    /// offering back to somebody who wrote a bare number meaning seconds.
    /// </summary>
    private static string Literal(double seconds)
    {
        var (scale, unit) = Math.Abs(seconds) switch
        {
            < 1e-3d => (1e6d, "us"),
            < 1d => (1e3d, "ms"),
            _ => (1d, "s"),
        };

        return Number(seconds * scale) + unit;
    }

    // --- what a module carries -----------------------------------------------

    private void File(Value placed, NodeDef def, string path, int line, int column)
    {
        if (placed is not Placed node || patch.Find(node.Id) is not { } instance) return;

        if (def.Extra<SampleExtra>() is not null) SampleExtra.Set(instance, path);
        else if (def.Extra<PictureExtra>() is not null) PictureExtra.Set(instance, path);
        else Complain(IssueCode.NoFile, line, column, $"'{def.Name}' names no file.");
    }

    /// <summary>
    /// The notes or the scale a block spells, and the one adjustment alternation
    /// asks for.
    /// </summary>
    /// <param name="opened">Where the block's '[' is, which what is wrong inside it is counted from.</param>
    private void Carry(Value placed, NodeDef def, string block, int line, int column, (int Line, int Column) opened)
    {
        if (placed is not Placed value || patch.Find(value.Id) is not { } node) return;

        if (def.Extra<StepsExtra>() is { } steps)
        {
            var read = StepNotation.Read(block, steps.Spec.Display == PortDisplay.Note, opened.Line, opened.Column, issues);

            StepsExtra.Set(node, read.Steps);

            // '<a b>' was unrolled into a longer list, so the list has to be
            // read more slowly for the pattern to take the time it did.
            if (read.RateDivisor > 1) Slower(node, def, read.RateDivisor, line, column);

            return;
        }

        if (def.Extra<ScaleExtra>() is not null)
        {
            ScaleExtra.Set(node, StepNotation.Classes(block, opened.Line, opened.Column, issues));
            return;
        }

        Complain(IssueCode.NoBlock, line, column, $"'{def.Name}' carries nothing a block could say.");
    }

    /// <summary>Divides a sequencer's rate, by the knob where there is one and by a Multiply where there is not.</summary>
    private void Slower(NodeInstance node, NodeDef def, int by, int line, int column)
    {
        var rate = Find(def.Inputs, "rate");
        if (rate < 0) return;

        if (patch.IncomingTo(node.Id, rate) is not { } wire)
        {
            node.InputValues[rate] /= by;
            return;
        }

        if (Module("math.mul", line, column) is not { } mul) return;

        var scale = NodeInstance.Create(mul, 0d, 0d, Next());
        patch.Nodes.Add(scale);

        scale.InputValues[1] = 1f / by;

        patch.Connect(wire.SourceNode, wire.SourcePort, scale.Id, 0);
        patch.Connect(scale.Id, 0, node.Id, rate);
    }

    /// <summary>Whether a <c>keyboard</c> line has been read already, so a second is said rather than obeyed.</summary>
    private bool laid;

    /// <summary>Lays the computer keyboard out, once — there is one keyboard.</summary>
    private void Lay(KeyboardStatement statement)
    {
        if (laid)
        {
            Complain(IssueCode.SaidTwice, statement.Line, statement.Column,
                "the keyboard is already laid out further up. A patch has one keyboard, so it says so once.");
            return;
        }

        laid = true;
        patch.Keyboard = statement.Scale is { } block ? Scale(block, statement.BlockLine, statement.BlockColumn) : null;
    }

    /// <summary>
    /// The notes of a scale from its tonic, <c>[ D E F G A B C ]</c>, which have to
    /// be one of the scales an Auto Chord builds in.
    /// </summary>
    private KeyboardScale? Scale(string block, int line, int column)
    {
        var said = issues.Count;
        var notes = StepNotation.Classes(block, line, column, issues);

        // A word that is not a note has been said already, and the scale it spoils is not worth saying too.
        if (issues.Count > said) return null;

        if (notes.Count == 0)
        {
            Complain(IssueCode.UnknownScale, line, column,
                "the keyboard's scale has no notes. Spell one from its tonic, as in 'keyboard scale [ D E F G A B C ]'.");
            return null;
        }

        var tonic = notes[0];
        var classes = Pitch.Scale(notes.Select(note => (note - tonic + Pitch.Classes) % Pitch.Classes));

        if (Chords.Scales.FirstOrDefault(scale => scale.Classes.SequenceEqual(classes)) is { } mode)
            return new KeyboardScale(tonic, mode.Id);

        Complain(IssueCode.UnknownScale, line, column,
            $"{string.Join(" ", notes.Select(Pitch.ClassName))} is not a scale from {Pitch.ClassName(tonic)}. "
            + "The keyboard plays the seven-note scales an Auto Chord builds in, from the first note.");
        return null;
    }

    /// <summary>Says what the patch is for, once.</summary>
    private void Describe(DescriptionStatement statement)
    {
        if (patch.Description is not null)
        {
            Complain(IssueCode.SaidTwice, statement.Line, statement.Column,
                "the patch is already described further up. It has one description, so it says so once.");
            return;
        }

        patch.Describe(statement.Text);
    }

    /// <summary>Says who made the patch, once.</summary>
    private void Credit(AuthorStatement statement)
    {
        if (patch.Author is not null)
        {
            Complain(IssueCode.SaidTwice, statement.Line, statement.Column,
                "the patch is already credited further up. It has one author line, so it says so once.");
            return;
        }

        patch.Credit(statement.Text);
    }

    /// <summary>Tags the patch, once.</summary>
    private void Tag(TagsStatement statement)
    {
        if (patch.Tags is not null)
        {
            Complain(IssueCode.SaidTwice, statement.Line, statement.Column,
                "the patch is already tagged further up. It has one tags line, so it says so once.");
            return;
        }

        patch.Tag(statement.Tags);
    }

    /// <summary>
    /// The field a name means, where the module declares one. A plugin's fields
    /// are named arguments like any knob (ADR-0055), addressed by their key
    /// rather than their label for the reason a module is addressed by its type
    /// id — a label is free to be reworded.
    /// </summary>
    private static (NodeExtra Owner, ExtraField Field)? Field(NodeDef def, string name)
    {
        foreach (var extra in def.Extras)
            foreach (var field in extra.Fields)
                if (Same(field.Key, name) || Same(field.Label, name)) return (extra, field);

        return null;
    }

    /// <summary>What a field was set to, ready to be put on the node once it exists.</summary>
    private JsonNode? Setting(ExtraField field, Argument argument, Scope scope)
    {
        if (Bind(argument.Value, scope) is not { } value) return null;

        JsonNode? written = value switch
        {
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            Figure figure when field is ExtraField.Toggle => JsonValue.Create(figure.Amount != 0d),
            Figure figure => JsonValue.Create((float)figure.Amount),
            Named named when field is ExtraField.Text { Multiline: true } => JsonValue.Create(named.Path.Replace(PatchPrinter.LineBreak, '\n')),
            Named named => JsonValue.Create(named.Path),
            _ => null,
        };

        if (written is null)
            Complain(IssueCode.FieldNeedsValue, argument.Line, argument.Column, $"'{field.Label}' is set to a value, not to a signal.");

        return written;
    }

    /// <summary>
    /// Puts a field onto the instance, beside whatever else its extra carries
    /// (ADR-0061) rather than in place of it.
    /// </summary>
    private static void Apply(NodeInstance node, NodeExtra owner, ExtraField field, JsonNode value)
    {
        var state = node.StateOf(owner.Key) as JsonObject ?? new JsonObject();

        state[field.Key] = value;
        node.SetState(owner.Key, state);
    }

    // --- defs -----------------------------------------------------------------

    private Value? Expand(DefStatement macro, CallExpr call, Scope scope, Value? piped)
    {
        if (!expanding.Add(macro.Name))
        {
            return Refuse(IssueCode.DefCallsItself, call.Line, call.Column,
                $"'{macro.Name}' calls itself, and a def is stamped out rather than run.");
        }

        // Set once the arguments are bound, because those belong to the caller's
        // statement rather than to the def — see Stamping.
        (string Where, int Placed, int Unnamed)? outer = null;

        try
        {
            var arguments = new List<Value>();

            // What is piped in is the first parameter, or the one '_' stands in.
            var placed = call.Arguments.Count(a => Placeholder(a.Value));

            if (placed > 1)
                return Refuse(IssueCode.PlaceholderTwice, call.Line, call.Column, "'_' is written twice, and a pipe brings one signal.");

            if (placed == 1 && piped is null)
                return Refuse(IssueCode.PlaceholderMisplaced, call.Line, call.Column, "'_' stands for what is piped in, and nothing is.");

            if (piped is not null && placed == 0) arguments.Add(piped);

            foreach (var argument in call.Arguments)
            {
                if (Placeholder(argument.Value)) arguments.Add(piped!);
                else if (!Piped(argument) && Bind(argument.Value, scope) is { } value) arguments.Add(value);
            }

            if (arguments.Count != macro.Parameters.Count)
            {
                return Refuse(IssueCode.DefArity, call.Line, call.Column,
                    $"'{macro.Name}' takes {macro.Parameters.Count} arguments and {arguments.Count} were given.");
            }

            // Every call stamps out its own copy, so every call is its own
            // segment: two calls in one statement place two sets of modules and
            // the names have to tell them apart. Numbered, because a call site
            // has nothing else to be known by — which is why moving one call
            // above another in the same statement renames both.
            outer = Enter(Stamping(macro.Name));

            // A fresh scope off the top, not off the caller's: a def sees its
            // parameters and the defs, and nothing of wherever it was called
            // from. Every call stamps out its own copy of the body, so two calls
            // share no module — a value that should be shared is passed in.
            var inner = new Scope(null);

            for (var i = 0; i < macro.Parameters.Count; i++) inner.Set(macro.Parameters[i], arguments[i], macro.Line);

            foreach (var statement in macro.Body) Run(statement, inner);

            if (macro.Results is { } several)
            {
                var items = new List<Value>();

                foreach (var result in several)
                    if (Bind(result, inner) is { } item) items.Add(item);

                return items.Count == several.Count ? new Several(items) : null;
            }

            return macro.Result is null ? null : Bind(macro.Result, inner);
        }
        finally
        {
            expanding.Remove(macro.Name);

            if (outer is { } saved) Leave(saved);
        }
    }

    /// <summary>What to call one stamping of a def, told apart from the next by number.</summary>
    private string Stamping(string name) => name + "~" + unnamedHere++;

    // --- looking things up ----------------------------------------------------

    /// <summary>
    /// Whether a name is a module or a def, asked without complaining about it.
    /// </summary>
    private bool Known(string name) => defs.ContainsKey(name) || moduleNames.Knows(name);

    private NodeDef? Module(string name, int line, int column)
    {
        if (moduleNames.Find(name, out var refusal, out var code) is { } def) return def;

        // Already said, once, by the requires line: the module is likely the
        // missing plugin's, and naming it again says nothing new.
        if (code == IssueCode.UnknownModule && missing.Count > 0) return null;

        Complain(code, line, column, refusal);
        return null;
    }

    /// <summary>
    /// A socket by name, with a space in the catalog's spelling standing for an
    /// underscore in the language's — <c>gate length</c> is <c>gate_length</c>.
    /// </summary>
    private static int Find(IReadOnlyList<PortSpec> ports, string name)
    {
        for (var i = 0; i < ports.Count; i++)
            if (Same(ports[i].Name, name)) return i;

        return -1;
    }

    private static bool Same(string port, string written) =>
        string.Equals(port.Replace(' ', '_'), written, StringComparison.OrdinalIgnoreCase);

    private static string List(IReadOnlyList<PortSpec> ports) =>
        ports.Count == 0 ? "none" : string.Join(", ", ports.Select(p => $"'{p.Name.Replace(' ', '_')}'"));

    /// <summary>The node and socket a written name points at, for a knob or a wire.</summary>
    private (NodeInstance Node, NodeDef Def, int Port)? Input(NameExpr target, Scope scope)
    {
        if (target.Port is null)
        {
            Complain(IssueCode.SocketUnsaid, target.Line, target.Column, "say which socket this is.");
            return null;
        }

        NodeInstance node;
        NodeDef def;

        if (target.Name == "out")
        {
            node = patch.Output;
            def = modules.Require(NodeCatalog.OutputTypeId);
        }
        else if (scope.Find(target.Name) is Placed placed && patch.Find(placed.Id) is { } found)
        {
            node = found;
            def = placed.Def;
        }
        else if (scope.Find(target.Name) is Failed)
        {
            return null;
        }
        else if (scope.Find(target.Name) is not null)
        {
            // A name bound to one output, a number or a def's several results:
            // it is there, and it is not something with sockets.
            Complain(IssueCode.NotAModule, target.Line, target.Column, $"'{target.Name}' is not a module, so it has no sockets.");
            return null;
        }
        else
        {
            Unknown(target);
            return null;
        }

        var port = Find(def.Inputs, target.Port);

        if (port >= 0)
        {
            // 'out.left' is the Output named, and the Output is a module with a
            // panel of its own — so the words are somewhere to click as well.
            mentions.Add((new Site(target.Line, target.Column), node.Id));

            return (node, def, port);
        }

        Complain(IssueCode.UnknownSocket, target.Line, target.Column,
            $"'{def.Name}' has no socket called '{target.Port}'. It has {List(def.Inputs)}.");

        return null;
    }
}
