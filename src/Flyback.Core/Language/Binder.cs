using System.Text.Json.Nodes;
using Flyback.Core.Graph;

namespace Flyback.Core.Language;

/// <summary>
/// A syntax tree to a <see cref="Patch"/>. Everything the language knows about
/// the instrument is here.
/// </summary>
/// <remarks>
/// The catalogue is the language: short names, socket names, arities and which
/// literals a socket will take are all read out of <see cref="ModuleCatalog"/>
/// as the tree is walked, so a plugin's modules are usable the moment it loads
/// and there is no table anywhere that can go stale.
/// <para>
/// Nothing here is compiled and nothing is evaluated. A <c>def</c> is expanded,
/// a number between two numbers is folded, and everything else becomes a node
/// and a wire — which is why the language needs no runtime of its own and why a
/// patch built from text is indistinguishable from one built by hand.
/// </para>
/// </remarks>
public sealed class Binder
{
    private readonly ModuleCatalog modules;
    private readonly List<LanguageIssue> issues;
    private readonly Patch patch = new();

    private readonly Dictionary<string, NodeDef> byShortName = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> ambiguous = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DefStatement> defs = new(StringComparer.Ordinal);
    private readonly HashSet<string> expanding = new(StringComparer.Ordinal);

    private Guid coordinates;
    private Guid clock;

    public Binder(ModuleCatalog modules, List<LanguageIssue> issues)
    {
        this.modules = modules;
        this.issues = issues;

        foreach (var def in modules.All)
        {
            var dot = def.TypeId.LastIndexOf('.');
            var plain = dot < 0 ? def.TypeId : def.TypeId[(dot + 1)..];

            if (!byShortName.TryAdd(plain, def)) ambiguous.Add(plain);
        }
    }

    /// <summary>The patch these statements describe, laid out and ready to compile.</summary>
    public Patch Build(IReadOnlyList<Statement> statements)
    {
        patch.EnsureOutput(modules);

        var scope = new Scope(null);

        foreach (var statement in statements) Run(statement, scope);

        // Positions are not in the language, so they are worked out afterwards
        // by the same layout the editor uses on a pasted fragment (ADR-0044).
        PatchLayout.Arrange(patch, modules);

        return patch;
    }

    // --- what a name is worth ------------------------------------------------

    /// <summary>Anything a name or an expression can stand for while binding.</summary>
    private abstract record Value;

    /// <summary>A number, which becomes a knob rather than a module.</summary>
    private sealed record Figure(double Amount, NumberStyle Style) : Value;

    /// <summary>A placed module, standing for every one of its outputs at once.</summary>
    private sealed record Placed(Guid Id, NodeDef Def) : Value;

    /// <summary>One output of a placed module.</summary>
    private sealed record Socket(Guid Id, int Port) : Value;

    /// <summary>What a def with several results hands back.</summary>
    private sealed record Several(IReadOnlyList<Value> Items) : Value;

    /// <summary>A file a module names rather than carries (ADR-0052).</summary>
    private sealed record Named(string Path) : Value;

    /// <summary>Names in sight, and the names the enclosing scope had.</summary>
    private sealed class Scope(Scope? parent)
    {
        private readonly Dictionary<string, Value> names = new(StringComparer.Ordinal);

        public void Set(string name, Value value) => names[name] = value;

        public Value? Find(string name) =>
            names.TryGetValue(name, out var value) ? value : parent?.Find(name);
    }

    private void Complain(int line, int column, string message) =>
        issues.Add(new LanguageIssue(line, column, message));

    // --- statements ----------------------------------------------------------

    private void Run(Statement statement, Scope scope)
    {
        switch (statement)
        {
            case DefStatement def:
                if (!defs.TryAdd(def.Name, def))
                    Complain(def.Line, def.Column, $"'{def.Name}' is already the name of a def.");
                break;

            case LetStatement let:
                if (Bind(let.Value, scope) is { } bound)
                {
                    Label(bound, let.Name);
                    scope.Set(let.Name, bound);
                }

                break;

            case LetTupleStatement tuple:
                Destructure(tuple, scope);
                break;

            case PipelineStatement pipeline:
                Bind(pipeline.Value, scope);
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
        }
    }

    /// <summary>
    /// Gives a node the name it was bound to, which the editor shows on it.
    /// </summary>
    /// <remarks>
    /// Only a module placed by this very binding takes the name — reading an
    /// existing one under a second name leaves the first alone, because the name
    /// on the canvas should say where the module was made rather than where it
    /// was last mentioned.
    /// </remarks>
    private void Label(Value value, string name)
    {
        if (value is not Placed placed) return;
        if (patch.Find(placed.Id) is not { Name: null } node) return;
        if (NodeCatalog.IsSink(node.TypeId)) return;

        node.Rename(placed.Def, name);
    }

    private void Destructure(LetTupleStatement statement, Scope scope)
    {
        if (Bind(statement.Value, scope) is not { } value) return;

        if (value is not Several several)
        {
            Complain(statement.Line, statement.Column,
                "this hands back one thing, so it cannot be taken apart into several.");
            return;
        }

        if (several.Items.Count != statement.Names.Count)
        {
            Complain(statement.Line, statement.Column,
                $"this hands back {several.Items.Count} things and {statement.Names.Count} names are waiting for them.");
            return;
        }

        for (var i = 0; i < statement.Names.Count; i++)
        {
            Label(several.Items[i], statement.Names[i]);
            scope.Set(statement.Names[i], several.Items[i]);
        }
    }

    private void Turn(KnobStatement statement, Scope scope)
    {
        if (Input(statement.Target, scope) is not var (node, def, port)) return;
        if (Bind(statement.Value, scope) is not { } value) return;

        if (value is not Figure figure)
        {
            Complain(statement.Line, statement.Column,
                "a knob takes a number. Use '<-' to wire a signal into it.");
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

    private void Box(GroupStatement statement, Scope scope)
    {
        var before = patch.Nodes.Select(n => n.Id).ToHashSet();
        var inner = new Scope(scope);

        foreach (var child in statement.Body) Run(child, inner);

        // Everything placed while the block was open, which is what "declared
        // inside it" means once a def has been expanded in there too.
        var made = patch.Nodes.Where(n => !before.Contains(n.Id)).Select(n => n.Id).ToList();

        if (patch.Group(made) is { } group) group.Rename(statement.Name);

        // A group is a box on the canvas and nothing more, so the names it made
        // go on being visible after it — which is what lets one group wire into
        // the next, as the largest preset does throughout.
        foreach (var child in statement.Body)
            if (child is LetStatement let && inner.Find(let.Name) is { } value)
                scope.Set(let.Name, value);
            else if (child is LetTupleStatement tuple)
                foreach (var name in tuple.Names)
                    if (inner.Find(name) is { } item) scope.Set(name, item);
    }

    // --- expressions ---------------------------------------------------------

    private Value? Bind(Expr expr, Scope scope) => expr switch
    {
        NumberExpr number => new Figure(number.Value, number.Style),
        TextExpr text => new Named(text.Value),
        NegateExpr negate => Negate(negate, scope),
        BinaryExpr binary => Arithmetic(binary, scope),
        NameExpr name => Read(name, scope),
        CallExpr call => Call(call, scope, piped: null),
        PipeExpr pipe => Pipe(pipe, scope),
        RangeExpr range => Refuse(range.Line, range.Column, "a range only means something as an argument."),
        _ => null,
    };

    private Value? Refuse(int line, int column, string message)
    {
        Complain(line, column, message);
        return null;
    }

    private Value? Negate(NegateExpr expr, Scope scope)
    {
        if (Bind(expr.Value, scope) is not { } value) return null;

        if (value is Figure figure) return figure with { Amount = -figure.Amount };

        return Module("math.neg", expr.Line, expr.Column) is { } def
            ? Place(def, [(0, value)], expr.Line, expr.Column)
            : null;
    }

    /// <summary>
    /// Infix arithmetic, which is five of the maths modules under another
    /// spelling — except between two numbers, where it is neither a module nor a
    /// wire but a knob that has already been worked out.
    /// </summary>
    private Value? Arithmetic(BinaryExpr expr, Scope scope)
    {
        if (Bind(expr.Left, scope) is not { } left) return null;
        if (Bind(expr.Right, scope) is not { } right) return null;

        if (left is Figure a && right is Figure b)
        {
            // Folded here rather than emitted, so a knob written as 1/12 is a
            // knob and not a Divide. A note or a duration is on a scale of its
            // own, so arithmetic on one is refused rather than quietly done in
            // semitones or decades.
            if (a.Style != NumberStyle.Plain || b.Style != NumberStyle.Plain)
            {
                return Refuse(expr.Line, expr.Column,
                    "arithmetic on a note or a duration would be done on a scale nobody meant.");
            }

            var folded = expr.Operator switch
            {
                TokenKind.Plus => a.Amount + b.Amount,
                TokenKind.Minus => a.Amount - b.Amount,
                TokenKind.Star => a.Amount * b.Amount,
                TokenKind.Slash => b.Amount == 0d ? 0d : a.Amount / b.Amount,
                _ => b.Amount == 0d ? 0d : a.Amount % b.Amount,
            };

            return new Figure(folded, NumberStyle.Plain);
        }

        var typeId = expr.Operator switch
        {
            TokenKind.Plus => "math.add",
            TokenKind.Minus => "math.sub",
            TokenKind.Star => "math.mul",
            TokenKind.Slash => "math.div",
            _ => "math.mod",
        };

        return Module(typeId, expr.Line, expr.Column) is { } def
            ? Place(def, [(0, left), (1, right)], expr.Line, expr.Column)
            : null;
    }

    private Value? Read(NameExpr expr, Scope scope)
    {
        // The four coordinates and the clock are one shared node each, however
        // often they are written — a patch that reads the clock in eight places
        // has one Time in it, which is what every preset does by hand.
        if (expr.Port is null && Source(expr.Name) is { } source) return source;

        if (expr.Name == "out")
        {
            return Refuse(expr.Line, expr.Column,
                "the Output has nothing to read. Pipe something into 'out.color' or 'out.left'.");
        }

        if (scope.Find(expr.Name) is not { } value)
            return Refuse(expr.Line, expr.Column, $"nothing here is called '{expr.Name}'.");

        if (expr.Port is null) return value;

        if (value is not Placed placed)
            return Refuse(expr.Line, expr.Column, $"'{expr.Name}' is not a module, so it has no sockets.");

        var port = Find(placed.Def.Outputs, expr.Port);

        if (port < 0)
        {
            return Refuse(expr.Line, expr.Column,
                $"'{placed.Def.Name}' has no output called '{expr.Port}'. It has {List(placed.Def.Outputs)}.");
        }

        return new Socket(placed.Id, port);
    }

    /// <summary>The shared Coordinates or Time a bare word stands for, if it is one.</summary>
    private Value? Source(string name)
    {
        var port = name switch
        {
            "x" => NodeCatalog.CoordXPort,
            "y" => NodeCatalog.CoordYPort,
            "radius" => 2,
            "angle" => 3,
            _ => -1,
        };

        if (port >= 0) return new Socket(Shared(ref coordinates, NodeCatalog.CoordTypeId), port);

        return name == "t" ? new Socket(Shared(ref clock, NodeCatalog.TimeTypeId), 0) : null;
    }

    private Guid Shared(ref Guid held, string typeId)
    {
        if (held != Guid.Empty) return held;

        var node = NodeInstance.Create(modules.Require(typeId), 0d, 0d);

        patch.Nodes.Add(node);
        held = node.Id;

        return held;
    }

    // --- the pipe rule -------------------------------------------------------

    private Value? Pipe(PipeExpr expr, Scope scope)
    {
        if (Bind(expr.Source, scope) is not { } value) return null;

        // A pipe into a socket is a wire into it, which is what puts a patch on
        // the screen: '|> out.color'.
        if (expr.Stage is NameExpr socket)
        {
            // A module named after a pipe with no brackets is that module,
            // placed and taking nothing but what is arriving. It is the obvious
            // way to write it in a language made of pipes, and refusing it cost
            // a whole run: the complaint landed on one line, every binding after
            // it went unread, and the errors that followed were all about names
            // that had never been made.
            if (socket.Port is null && scope.Find(socket.Name) is null && Known(socket.Name))
                return Call(new CallExpr(socket.Name, [], null, socket.Line, socket.Column), scope, value);

            if (socket.Port is null)
            {
                return Refuse(socket.Line, socket.Column,
                    $"'{socket.Name}' is a module, not a socket. Say which one to wire into.");
            }

            if (Input(socket, scope) is not var (node, _, port)) return null;

            Feed(value, 0, node.Id, port, socket.Line, socket.Column);
            return value;
        }

        if (expr.Stage is CallExpr call) return Call(call, scope, value);

        return Refuse(expr.Line, expr.Column, "only a module or a socket may follow '|>'.");
    }

    /// <summary>How many signals a value carries when it is piped.</summary>
    private int Width(Value value) => value switch
    {
        Placed placed => placed.Def.Outputs.Count,
        Several several => several.Items.Count,
        _ => 1,
    };

    /// <summary>The <paramref name="index"/>th signal of a value, for wiring.</summary>
    private Value Part(Value value, int index) => value switch
    {
        Placed placed => new Socket(placed.Id, index),
        Several several => several.Items[index],
        _ => value,
    };

    private Value? Call(CallExpr expr, Scope scope, Value? piped)
    {
        if (defs.TryGetValue(expr.Target, out var macro)) return Expand(macro, expr, scope, piped);

        if (Module(expr.Target, expr.Line, expr.Column) is not { } def) return null;

        if (NodeCatalog.IsSink(def.TypeId))
        {
            return Refuse(expr.Line, expr.Column,
                "every patch already has its Output. Wire into 'out.color' or 'out.left'.");
        }

        var taken = new HashSet<int>();
        var wiring = new List<(int Port, Value Value)>();
        var paths = new List<(string Path, int Line, int Column)>();
        var fields = new List<(NodeExtra Owner, ExtraField Field, JsonNode Value)>();

        // Named arguments first, because what they claim decides where
        // everything else can go.
        foreach (var argument in expr.Arguments)
        {
            if (argument.Name is null) continue;

            var port = Find(def.Inputs, argument.Name);

            if (port < 0)
            {
                if (Field(def, argument.Name) is var (owner, field))
                {
                    if (Setting(field, argument, scope) is { } written)
                        fields.Add((owner, field, written));

                    continue;
                }

                Complain(argument.Line, argument.Column,
                    $"'{def.Name}' has no socket called '{argument.Name}'. It has {List(def.Inputs)}.");
                continue;
            }

            if (!taken.Add(port))
            {
                Complain(argument.Line, argument.Column, $"'{argument.Name}' is given twice.");
                continue;
            }

            if (Bind(argument.Value, scope) is { } value) wiring.Add((port, value));
        }

        var piping = new List<(int Port, Value Value)>();

        if (piped is not null && !Land(def, piped, taken, expr, piping)) return null;

        // Whatever the pipe and the named arguments left, in order.
        var free = new Queue<int>(Enumerable.Range(0, def.Inputs.Count).Where(i => !taken.Contains(i)));

        foreach (var argument in expr.Arguments)
        {
            if (argument.Name is not null) continue;

            if (argument.Value is TextExpr text)
            {
                paths.Add((text.Value, argument.Line, argument.Column));
                continue;
            }

            // A range is two arguments written as one, which is what makes
            // remap(-2..2, 0..1) four sockets and two commas.
            var parts = argument.Value is RangeExpr range ? new[] { range.Low, range.High } : [argument.Value];

            foreach (var part in parts)
            {
                if (free.Count == 0)
                {
                    Complain(argument.Line, argument.Column,
                        $"'{def.Name}' has no socket left for this. It has {List(def.Inputs)}.");
                    break;
                }

                var port = free.Dequeue();
                taken.Add(port);

                if (Bind(part, scope) is { } value) wiring.Add((port, value));
            }
        }

        var node = Place(def, [.. piping, .. wiring], expr.Line, expr.Column);

        if (node is Placed made && patch.Find(made.Id) is { } instance)
            foreach (var (owner, field, written) in fields)
                Apply(instance, owner, field, written);

        foreach (var (path, line, column) in paths) File(node, def, path, line, column);
        if (expr.Block is { } block) Carry(node, def, block, expr.Line, expr.Column);

        return node;
    }

    /// <summary>
    /// Where a pipe lands, which is the whole of the language's shape.
    /// </summary>
    /// <remarks>
    /// A socket named exactly <c>in</c> wins when the call did not name it. That
    /// is not a convenience: <c>math.smoothstep</c> is
    /// <c>[edge0, edge1, in]</c> and <c>math.step</c> is <c>[edge, in]</c>, so
    /// "the first socket left" would wire the signal into an edge, read
    /// perfectly, and mean something else. Three shipped presets do exactly this
    /// and would all have been wrong.
    /// </remarks>
    private bool Land(NodeDef def, Value piped, HashSet<int> taken, CallExpr expr, List<(int, Value)> into)
    {
        var signal = Find(def.Inputs, "in");

        // 'in' is one signal by definition, so a module that has one is a scalar
        // position and takes the source's first output — the same thing a bare
        // name means anywhere else a single value is wanted. A MIDI In has four
        // outputs and 'keys |> clamp(36, 84)' means its pitch, which falls out
        // of this rather than being a case anyone had to add.
        if (signal >= 0 && !taken.Contains(signal))
        {
            taken.Add(signal);
            into.Add((signal, Part(piped, 0)));

            return true;
        }

        var free = Enumerable.Range(0, def.Inputs.Count).Where(i => !taken.Contains(i)).ToList();

        if (free.Count == 0)
        {
            Complain(expr.Line, expr.Column, $"'{def.Name}' has no socket free for what is arriving.");
            return false;
        }

        // A position takes two, and it is the only thing that does. 'x' and 'y'
        // next to each other are what the engine itself calls a position — the
        // pair ADR-0050 normals to Coordinates together — so a Space or a
        // Pattern chains off another without either end saying so, and nothing
        // else in the catalogue quietly swallows more than one signal.
        //
        // The alternative was to forward every output a source has, and the
        // Sequence preset is what rules it out: 'steps |> note()' would have put
        // the sequencer's gate into Note's octave and its index into the cents,
        // which reads perfectly and is not a tune.
        var position = free.Count >= 2
            && Same(def.Inputs[free[0]].Name, "x")
            && Same(def.Inputs[free[1]].Name, "y")
            && Width(piped) >= 2;

        var count = position ? 2 : 1;

        for (var i = 0; i < count; i++)
        {
            taken.Add(free[i]);
            into.Add((free[i], Part(piped, i)));
        }

        return true;
    }

    // --- placing and wiring --------------------------------------------------

    private Value Place(NodeDef def, IReadOnlyList<(int Port, Value Value)> inputs, int line, int column)
    {
        var node = NodeInstance.Create(def, 0d, 0d);
        patch.Nodes.Add(node);

        foreach (var (port, value) in inputs)
        {
            if (value is Figure figure) Knob(node, def, port, figure, line, column);
            else Feed(value, 0, node.Id, port, line, column);
        }

        return new Placed(node.Id, def);
    }

    /// <summary>Wires one signal of <paramref name="value"/> into a socket.</summary>
    private void Feed(Value value, int index, Guid target, int port, int line, int column)
    {
        switch (value)
        {
            case Socket socket:
                patch.Connect(socket.Id, socket.Port, target, port);
                break;

            case Placed placed:
                patch.Connect(placed.Id, 0, target, port);
                break;

            case Several several when several.Items.Count > index:
                Feed(several.Items[index], 0, target, port, line, column);
                break;

            case Figure figure:
                if (patch.Find(target) is { } node && modules.Get(node.TypeId) is { } def)
                    Knob(node, def, port, figure, line, column);

                break;

            default:
                Complain(line, column, "this is not a signal, so nothing can be wired from it.");
                break;
        }
    }

    /// <summary>
    /// Sets a knob, having first asked whether the socket has one and whether it
    /// reads numbers on the scale this one was written on.
    /// </summary>
    private void Knob(NodeInstance node, NodeDef def, int port, Figure figure, int line, int column)
    {
        if (port < 0 || port >= def.Inputs.Count) return;

        var spec = def.Inputs[port];

        // A normalled socket is already carrying something and the stored value
        // is never read, so a number here would be a knob nobody can turn
        // (ADR-0050). The same refusal the assistant's set_knobs makes.
        if (modules.Normalled(spec) is { } driver)
        {
            Complain(line, column,
                $"'{spec.Name}' is normalled to {driver} and has no knob. "
                + "Patch a Value in if it really should stand still.");
            return;
        }

        var wanted = figure.Style switch
        {
            NumberStyle.Note => PortDisplay.Note,
            NumberStyle.Duration => PortDisplay.Duration,
            _ => spec.Display,
        };

        if (wanted != spec.Display)
        {
            var written = figure.Style == NumberStyle.Note ? "a note" : "a length of time";

            Complain(line, column, $"'{spec.Name}' is not read as {written}.");
            return;
        }

        // A bare number on a socket that holds time is the trap the literal was
        // added to remove, and allowing it left the trap open: the socket holds
        // a power of ten, so an envelope written as "attack: 0.01" meaning ten
        // milliseconds is a second, and what was meant to be a drum is a drone.
        // Nothing about the value says which was meant, so the only place to
        // catch it is here, and the complaint says both readings.
        if (spec.Display == PortDisplay.Duration && figure.Style == NumberStyle.Plain)
        {
            Complain(line, column,
                $"'{spec.Name}' is a length of time, and a bare number on one is a power of ten: "
                + $"{Number(figure.Amount)} means {spec.Format((float)figure.Amount)}. "
                + $"Write {Literal(figure.Amount)} if you meant {Number(figure.Amount)} seconds.");
            return;
        }

        if (!double.IsFinite(figure.Amount))
        {
            Complain(line, column, $"'{spec.Name}' cannot hold that.");
            return;
        }

        node.InputValues[port] = (float)figure.Amount;
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
        else Complain(line, column, $"'{def.Name}' names no file.");
    }

    /// <summary>
    /// The notes or the scale a block spells, and the one adjustment alternation
    /// asks for.
    /// </summary>
    private void Carry(Value placed, NodeDef def, string block, int line, int column)
    {
        if (placed is not Placed value || patch.Find(value.Id) is not { } node) return;

        if (def.Extra<StepsExtra>() is { } steps)
        {
            var read = StepNotation.Read(block, steps.Spec.Display == PortDisplay.Note, line, issues);

            StepsExtra.Set(node, read.Steps);

            // '<a b>' was unrolled into a longer list, so the list has to be
            // read more slowly for the pattern to take the time it did.
            if (read.RateDivisor > 1) Slower(node, def, read.RateDivisor, line, column);

            return;
        }

        if (def.Extra<ScaleExtra>() is not null)
        {
            ScaleExtra.Set(node, Classes(block, line));
            return;
        }

        Complain(line, column, $"'{def.Name}' carries nothing a block could say.");
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

        var scale = NodeInstance.Create(mul, 0d, 0d);
        patch.Nodes.Add(scale);

        scale.InputValues[1] = 1f / by;

        patch.Connect(wire.SourceNode, wire.SourcePort, scale.Id, 0);
        patch.Connect(scale.Id, 0, node.Id, rate);
    }

    /// <summary>The pitch classes a scale block names, by letter or by number.</summary>
    private List<int> Classes(string block, int line)
    {
        var classes = new List<int>();

        foreach (var word in block.Split([' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            // A class is a note with no octave, so it is read as one in the
            // octave that starts at zero and then reduced.
            if (Lexer.Note(word + "0") is { } note)
            {
                classes.Add(((int)note % Pitch.Classes + Pitch.Classes) % Pitch.Classes);
                continue;
            }

            if (int.TryParse(word, out var number) && number is >= 0 and < Pitch.Classes)
            {
                classes.Add(number);
                continue;
            }

            issues.Add(new LanguageIssue(line, 1, $"'{word}' is not a note of the octave."));
        }

        return classes;
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
            Figure figure when field is ExtraField.Toggle => JsonValue.Create(figure.Amount != 0d),
            Figure figure => JsonValue.Create((float)figure.Amount),
            Named named => JsonValue.Create(named.Path),
            _ => null,
        };

        if (written is null)
            Complain(argument.Line, argument.Column, $"'{field.Label}' is set to a value, not to a signal.");

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
            return Refuse(call.Line, call.Column,
                $"'{macro.Name}' calls itself, and a def is stamped out rather than run.");
        }

        try
        {
            var arguments = new List<Value>();

            if (piped is not null) arguments.Add(piped);

            foreach (var argument in call.Arguments)
            {
                if (Bind(argument.Value, scope) is { } value) arguments.Add(value);
            }

            if (arguments.Count != macro.Parameters.Count)
            {
                return Refuse(call.Line, call.Column,
                    $"'{macro.Name}' takes {macro.Parameters.Count} arguments and {arguments.Count} were given.");
            }

            // A fresh scope off the top, not off the caller's: a def sees its
            // parameters and the defs, and nothing of wherever it was called
            // from. Every call stamps out its own copy of the body, so two calls
            // share no module — a value that should be shared is passed in.
            var inner = new Scope(null);

            for (var i = 0; i < macro.Parameters.Count; i++) inner.Set(macro.Parameters[i], arguments[i]);

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
        }
    }

    // --- looking things up ----------------------------------------------------

    /// <summary>
    /// Whether a name is a module or a def, asked without complaining about it.
    /// </summary>
    private bool Known(string name) =>
        defs.ContainsKey(name) || modules.Get(name) is not null
        || byShortName.ContainsKey(name) || ambiguous.Contains(name);

    private NodeDef? Module(string name, int line, int column)
    {
        if (modules.Get(name) is { } exact) return exact;

        if (ambiguous.Contains(name))
        {
            var both = modules.All
                .Where(d => d.TypeId.EndsWith('.' + name) || d.TypeId == name)
                .Select(d => d.TypeId)
                .Order(StringComparer.Ordinal);

            Complain(line, column,
                $"'{name}' could be {string.Join(" or ", both)}. Write the one you mean in full.");

            return null;
        }

        if (byShortName.TryGetValue(name, out var def)) return def;

        Complain(line, column, $"there is no module called '{name}'.{Nearest(name)}");
        return null;
    }

    /// <summary>The closest name there is, where one is close enough to be worth offering.</summary>
    private string Nearest(string name)
    {
        var best = byShortName.Keys
            .Select(k => (Name: k, Distance: Distance(k, name)))
            .Where(k => k.Distance <= Math.Max(1, name.Length / 3))
            .OrderBy(k => k.Distance)
            .ThenBy(k => k.Name, StringComparer.Ordinal)
            .Select(k => k.Name)
            .FirstOrDefault();

        return best is null ? string.Empty : $" Did you mean '{best}'?";
    }

    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= b.Length; j++)
            {
                var swap = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;

                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + swap);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    /// <summary>
    /// A socket by name, with a space in the catalogue's spelling standing for an
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
            Complain(target.Line, target.Column, "say which socket this is.");
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
        else
        {
            Complain(target.Line, target.Column, $"nothing here is called '{target.Name}'.");
            return null;
        }

        var port = Find(def.Inputs, target.Port);

        if (port >= 0) return (node, def, port);

        Complain(target.Line, target.Column,
            $"'{def.Name}' has no socket called '{target.Port}'. It has {List(def.Inputs)}.");

        return null;
    }
}
