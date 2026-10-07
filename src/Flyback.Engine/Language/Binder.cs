using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Graph;
using Flyback.Engine.Language.Ast;
using Flyback.Engine.Language.Ast.Expressions;
using Flyback.Engine.Language.Ast.Statements;
using Flyback.Engine.Language.Values;

namespace Flyback.Engine.Language;

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
    private readonly Issues issues;
    private readonly Wiring wiring;
    private readonly Patch patch;
    private readonly NodeIdentity identity;

    private readonly ModuleNames moduleNames;
    private readonly Dictionary<string, DefStatement> defs = new(StringComparer.Ordinal);
    private readonly HashSet<string> expanding = new(StringComparer.Ordinal);

    private readonly SourceSites sourceSites = new();
    private readonly Carrying carrying;
    private readonly PatchLines lines;

    private readonly Boxes boxes;

    /// <summary>Plugins a <c>requires</c> line named that this build does not have.</summary>
    private readonly List<string> missing = [];

    /// <summary>The line each top-level name is bound on, for saying so where one is read above it.</summary>
    private readonly Dictionary<string, int> boundOn = [];

    public Binder(ModuleCatalog modules, List<LanguageIssue> issues)
    {
        this.modules = modules;
        this.issues = new Issues(issues);

        wiring = new Wiring(modules, this.issues, sourceSites);
        patch = wiring.Patch;
        identity = wiring.Identity;
        carrying = new Carrying(modules, wiring, this.issues);
        lines = new PatchLines(patch, this.issues);
        boxes = new Boxes(patch, this.issues, sourceSites);

        moduleNames = new ModuleNames(modules);
    }

    /// <summary>
    /// Where the text says each of the things it built, for a caret that has to
    /// name a module and a knob that has to be written back. Only the binder can
    /// say this: a patch carries nothing about the file it came from.
    /// </summary>
    public SourceMap Map(string source) => sourceSites.Map(source);

    /// <summary>The patch these statements describe, laid out and ready to compile.</summary>
    public Patch Build(IReadOnlyList<Statement> statements)
    {
        // Named rather than left to chance like the rest of it, because the
        // Output is the one module every patch has and the one every rebuild
        // must recognise as the same one.
        patch.EnsureOutput(modules, NodeIdentity.FromName("out"));

        // The one module nothing places, so the one whose knobs can only ever be
        // written as a statement of their own.
        sourceSites.Name(patch.Output.Id, "out");

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

        boxes.Draw();

        // A call to a Maths module the Expression stands for, and the sums and
        // calls around it, arrive as the Expressions a preset's do (ADR-0109).
        var into = new Dictionary<Guid, Guid>();
        ExpressionFusion.Fuse(patch, modules, into);
        sourceSites.Folded(into);

        // Positions are not in the language, so they are worked out afterwards
        // by the same layout the editor uses on a pasted fragment (ADR-0044).
        PatchLayout.Arrange(patch, modules);

        return patch;
    }

    private void Unknown(NameExpr expr)
    {
        if (boundOn.TryGetValue(expr.Name, out var line) && line > expr.Line)
        {
            issues.Complain(IssueCode.UsedBeforeBound, expr.Line, expr.Column,
                $"'{expr.Name}' is bound on line {line}, below where it is read. Bind it before reading it.");
            return;
        }

        if (expr.Port is null && StatementWord(expr.Name) is { } example)
        {
            issues.Complain(IssueCode.UnknownName, expr.Line, expr.Column,
                $"'{expr.Name}' starts a statement only with what it says after it: {example}.");
            return;
        }

        issues.Complain(IssueCode.UnknownName, expr.Line, expr.Column, $"nothing here is called '{expr.Name}'.");
    }

    /// <summary>What a line starting with this word of the language looks like, for a name that is one.</summary>
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

    private static string? Builtin(string name) => name switch
    {
        "t" => "the clock",
        "x" or "y" or "radius" or "angle" or "aspect" => "one of the picture's coordinates",
        "out" => "the Output",
        "_" => "what a pipe brings in",
        "let" or "def" or "group" => "a word of the language",
        _ => null,
    };

    /// <summary>
    /// Whether <paramref name="name"/> may be bound here, said where it may not.
    /// A name means one thing, so no reader has to work out which binding a word
    /// reaches.
    /// </summary>
    private bool Free(string name, Scope scope, int line, int column)
    {
        if (Builtin(name) is { } what)
        {
            issues.Complain(IssueCode.ReservedName, line, column, $"'{name}' is already {what}. Call this something else.");
            return false;
        }

        if (scope.Entry(name) is { } first)
        {
            issues.Complain(IssueCode.BoundTwice, line, column, $"'{name}' is already bound on line {first.Line}. A name is bound once.");
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
        var outer = identity.Enter(Naming(statement));

        Ran(statement, scope);

        identity.Leave(outer);
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
        LengthStatement => "length",
        DescriptionStatement => "description",
        AuthorStatement => "author",
        TagsStatement => "tags",
        PipelineStatement pipeline => Ending(pipeline.Value) ?? identity.Anonymous(),
        _ => identity.Anonymous(),
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

    private void Ran(Statement statement, Scope scope)
    {
        switch (statement)
        {
            case DefStatement def:
                if (!defs.TryAdd(def.Name, def))
                    issues.Complain(IssueCode.DefTwice, def.Line, def.Column, $"'{def.Name}' is already the name of a def.");

                for (var i = 0; i < def.Parameters.Count; i++)
                {
                    var (parameter, fallback, _, _) = def.Parameters[i];

                    if (Builtin(parameter) is { } what)
                        issues.Complain(IssueCode.ReservedName, def.Line, def.Column, $"'{parameter}' is already {what}. Call this something else.");
                    else if (def.Parameters.Take(i).Any(p => p.Name == parameter))
                        issues.Complain(IssueCode.BoundTwice, def.Line, def.Column, $"'{def.Name}' takes two parameters called '{parameter}'.");

                    if (fallback is not null && !Constant(fallback))
                    {
                        issues.Complain(IssueCode.DefaultNotAValue, fallback.Line, fallback.Column,
                            $"a default is a number, a note, a duration or a text. Pass '{parameter}' a signal as an argument instead.");
                    }
                    else if (fallback is null && def.Parameters.Take(i).FirstOrDefault(p => p.Default is not null) is { } earlier)
                    {
                        issues.Complain(IssueCode.DefaultBeforeRequired, def.Parameters[i].Line, def.Parameters[i].Column,
                            $"'{parameter}' has no default and comes after '{earlier.Name}', which has one. Put it first.");
                    }
                }

                break;

            case LetStatement let:
                if (!Free(let.Name, scope, let.Line, let.Column)) break;

                if (Bind(let.Value, scope) is { } value)
                {
                    Label(value, let.Name);
                    scope.Set(let.Name, value, let.Line);
                    Owns(value);

                    if (value is Placed placed) sourceSites.Name(placed.Id, let.Name);
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
                lines.Lay(keyboard);
                break;

            case LengthStatement length:
                lines.Last(length);
                break;

            case DescriptionStatement description:
                lines.Describe(description);
                break;

            case AuthorStatement author:
                lines.Credit(author);
                break;

            case TagsStatement tags:
                lines.Tag(tags);
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

    // --- names and source ownership ------------------------------------------

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
        if (value is Placed placed) sourceSites.Own(placed.Id);
        else if (value is Socket socket) sourceSites.Own(socket.Id);
    }

    /// <summary>Notes that <paramref name="expr"/> is a word naming a module, or counts a number read through a name.</summary>
    private void Mention(Expr expr, Value value)
    {
        if (value is Figure { Where: { } where }) sourceSites.ReadThrough(where);

        var id = value switch
        {
            Placed placed => placed.Id,
            Socket socket => socket.Id,
            _ => Guid.Empty,
        };

        if (id != Guid.Empty) sourceSites.Mention(new Site(expr.Line, expr.Column), id);
    }

    private void Destructure(LetTupleStatement statement, Scope scope)
    {
        for (var i = 0; i < statement.Names.Count; i++)
        {
            var name = statement.Names[i];

            if (!Free(name, scope, statement.Line, statement.Column)) return;

            if (statement.Names.Take(i).Contains(name, StringComparer.Ordinal))
            {
                issues.Complain(IssueCode.BoundTwice, statement.Line, statement.Column, $"'{name}' is written twice. A name is bound once.");
                return;
            }
        }

        if (Bind(statement.Value, scope) is not { } value) return;

        if (value is not Several several)
        {
            issues.Complain(IssueCode.TupleMismatch, statement.Line, statement.Column,
                "this hands back one thing, so it cannot be taken apart into several.");
            return;
        }

        if (several.Items.Count != statement.Names.Count)
        {
            issues.Complain(IssueCode.TupleMismatch, statement.Line, statement.Column,
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
            wiring.Link(node, def, port, dial, statement.Line, statement.Column);
            return;
        }

        if (value is not Figure figure)
        {
            issues.Complain(IssueCode.KnobNeedsNumber, statement.Line, statement.Column,
                "a knob takes a number or a panel knob. Use '<-' to wire a signal into it.");
            return;
        }

        wiring.Knob(node, def, port, figure, statement.Line, statement.Column);
    }

    private void Backwards(BackWireStatement statement, Scope scope)
    {
        if (Input(statement.Target, scope) is not var (node, _, port)) return;
        if (Bind(statement.Value, scope) is not { } value) return;

        wiring.Feed(value, 0, node.Id, port, statement.Line, statement.Column);
    }

    /// <summary>
    /// Takes a module out of the signal path — see <see cref="NodeInstance.Off"/>.
    /// </summary>
    private void Switch(OffStatement statement, Scope scope)
    {
        var target = statement.Target;

        if (target.Name == "out")
        {
            issues.Complain(IssueCode.OutputCannotBeOff, target.Line, target.Column,
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
            issues.Complain(IssueCode.NotAModule, target.Line, target.Column,
                $"'{target.Name}' is not a module, so there is nothing to switch off.");
            return;
        }

        sourceSites.Mention(new Site(target.Line, target.Column), node.Id);
        node.Off = true;
    }

    /// <summary>The plugins a patch says it needs, checked against the ones this build has.</summary>
    private void Require(RequiresStatement statement)
    {
        foreach (var plugin in statement.Plugins)
        {
            if (modules.HasProvider(plugin) || missing.Contains(plugin, StringComparer.Ordinal)) continue;

            missing.Add(plugin);

            issues.Complain(IssueCode.MissingPlugin, statement.Line, statement.Column,
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
            issues.Complain(IssueCode.PanelInDef, line, column,
                "a panel knob belongs to the patch, so it is declared outside a def and passed in.");
            return;
        }

        if (!Free(statement.Name, scope, line, column)) return;

        // A knob given a range is written like a call, so it cannot share a
        // module's name or a def's.
        if (Known(statement.Name))
        {
            issues.Complain(IssueCode.ReservedName, line, column,
                $"'{statement.Name}' is already a module's name, and a knob is called like one. "
                + $"Call this something else, such as '{statement.Name}_knob'.");
            return;
        }

        if (Bind(statement.Value, scope) is not Figure { Style: NumberStyle.Plain } resting
            || resting.Amount is < 0d or > 1d or double.NaN)
        {
            issues.Complain(IssueCode.OutOfRange, line, column,
                "a panel knob rests somewhere from 0 to 1, and the sockets that follow it say what that means to them.");
            return;
        }

        if (PanelDeclaration.Read(statement, resting, issues) is not { } control) return;

        (patch.Controls ??= []).Add(control);
        scope.Set(statement.Name, new Dial(control, statement.Name), line);

        // So a knob turned on the panel can be written back where it rests.
        sourceSites.Write(control.Id, PatchPrinter.PanelKnob, resting.Where);
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
                    issues.Complain(IssueCode.GroupInGroup, nested.Line, nested.Column,
                        "a group cannot hold another group. Close this one first.");
                    Unmade(Declared(nested.Body), scope, nested.Line);
                    break;

                case PanelStatement panel:
                    issues.Complain(IssueCode.PanelInGroup, panel.Line, panel.Column,
                        "a panel knob belongs to the whole patch, so it is declared outside a group.");
                    Unmade([panel.Name], scope, panel.Line);
                    break;

                case RequiresStatement requires:
                    issues.Complain(IssueCode.RequiresInGroup, requires.Line, requires.Column,
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
            .Where(n => !before.Contains(n.Id) && !wiring.IsShared(n.Id))
            .Select(n => n.Id);

        boxes.Block(statement.Name, new Site(statement.Line, statement.Column), made, troubled: issues.Count > said);

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
        NegateExpr or BinaryExpr => Sum(expr, scope),
        NameExpr name => Read(name, scope),
        CallExpr call => Call(call, scope, null),
        SelectExpr select => Select(select, scope, piped: null),
        PipeExpr pipe => Pipe(pipe, scope),
        RangeExpr range => Refuse(IssueCode.RangeOutsideArgument, range.Line, range.Column, "a range only means something as an argument."),
        _ => null,
    };

    private Value? Read(NameExpr expr, Scope scope)
    {
        if (expr.Port is null && Source(expr.Name) is { } value) return value;

        if (expr is { Name: "t", Port: { } port }) return Output(wiring.Clock(), port, expr.Line, expr.Column);

        if (expr.Name == "out")
        {
            return Refuse(IssueCode.OutputIsNotASource, expr.Line, expr.Column,
                "the Output has nothing to read. Pipe something into 'out.color' or 'out.left'.");
        }

        if (Placeholder(expr))
            return Refuse(IssueCode.PlaceholderMisplaced, expr.Line, expr.Column, "'_' stands for what is piped in, as a call's argument: 'socket: _'.");

        if (scope.Find(expr.Name) is not { } bound)
        {
            Unknown(expr);
            return null;
        }

        if (bound is Failed) return null;

        Mention(expr, bound);

        if (expr.Port is null) return bound;

        if (bound is not Placed)
            return Refuse(IssueCode.NotAModule, expr.Line, expr.Column, $"'{expr.Name}' is not a module, so it has no sockets.");

        return Output(bound, expr.Port, expr.Line, expr.Column);
    }

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

    private Value? Output(Value value, string name, int line, int column)
    {
        if (value is not Placed placed)
            return Refuse(IssueCode.NotAModule, line, column, $"this is not a module, so it has no output called '{name}'.");

        var port = SocketNames.Find(placed.Def.Outputs, name);

        if (port < 0)
        {
            return Refuse(IssueCode.UnknownOutput, line, column,
                $"'{placed.Def.Name}' has no output called '{name}'. It has {SocketNames.List(placed.Def.Outputs)}.");
        }

        return new Socket(placed.Id, placed.Def, port);
    }

    // --- sums ----------------------------------------------------------------

    /// <summary>
    /// A sum: a number where it is all numbers, and otherwise an Expression
    /// module reading the signals in it (ADR-0106).
    /// </summary>
    private Value? Sum(Expr expr, Scope scope) => Term(expr, scope) switch
    {
        Formulas.Operand operand => operand.Figure,
        Formulas.Signal signal => signal.Value,
        Formulas.Operation operation => Expression(operation),
        _ => null,
    };

    private Formulas.Term? Term(Expr expr, Scope scope)
    {
        switch (expr)
        {
            case NegateExpr negate:
            {
                if (Term(negate.Value, scope) is not { } value) return null;

                // The sign belongs to the number for diagnostics and write-back.
                if (value is Formulas.Operand operand)
                {
                    return new Formulas.Operand(operand.Figure with
                    {
                        Amount = -operand.Figure.Amount,
                        Where = new Site(negate.Line, negate.Column),
                    });
                }

                return Fit(new Formulas.Operation('-', value, null, negate.Line, negate.Column));
            }

            case BinaryExpr binary:
            {
                if (Term(binary.Left, scope) is not { } left) return null;
                if (Term(binary.Right, scope) is not { } right) return null;

                if (left is Formulas.Operand a && right is Formulas.Operand b)
                    return Formulas.Fold(binary.Operator, a.Figure, b.Figure, binary.Line, binary.Column, issues);

                return Fit(new Formulas.Operation(Formulas.Sign(binary.Operator), left, right, binary.Line, binary.Column));
            }

            default:
            {
                if (Bind(expr, scope) is not { } value) return null;

                if (value is Figure figure) return new Formulas.Operand(figure);

                // Each reference to a panel knob is a distinct formula input.
                if (value is Dial) return new Formulas.Signal(value, (Guid.NewGuid(), 0));

                if (Formulas.From(value) is { } from) return new Formulas.Signal(value, from);

                issues.Complain(IssueCode.NotASignal, expr.Line, expr.Column, "this is not a signal, so nothing can be wired from it.");
                return null;
            }
        }
    }

    /// <summary>Splits an operation until each Expression reads no more signals than it has sockets.</summary>
    private Formulas.Term? Fit(Formulas.Operation operation)
    {
        while (Formulas.Overflows(operation))
        {
            var left = Formulas.Inputs(operation.Left).Count;
            var right = operation.Right is null ? 0 : Formulas.Inputs(operation.Right).Count;

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

    /// <summary>An operation placed as a module and read as one signal, so the operation around it has a socket to spare.</summary>
    private Formulas.Term? Settled(Formulas.Term term) =>
        term is not Formulas.Operation operation ? term
        : Expression(operation) is { } placed && Formulas.From(placed) is { } from ? new Formulas.Signal(placed, from)
        : null;

    /// <summary>Places an operation as an Expression module carrying its formula.</summary>
    private Value? Expression(Formulas.Operation operation)
    {
        var inputs = Formulas.Inputs(operation);

        if (Formulas.Written(operation, inputs, issues) is not { } formula) return null;
        if (Module(NodeCatalog.ExpressionTypeId, operation.Line, operation.Column) is not { } def) return null;

        var placed = wiring.Place(def, [.. inputs.Select((input, socket) => (socket, input.Value))], operation.Line, operation.Column);

        wiring.Formula(placed, formula, operation.Line, operation.Column);

        return placed;
    }

    private Value? Refuse(string code, int line, int column, string message)
    {
        issues.Complain(code, line, column, message);
        return null;
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
            "aspect" => NodeCatalog.CoordAspectPort,
            _ => -1,
        };

        if (port >= 0) return wiring.Coordinates().Part(port);

        return name == "t" ? wiring.Clock().Part(0) : null;
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

            wiring.Feed(value, 0, node.Id, port, socket.Line, socket.Column);
            return value;
        }

        if (expr.Stage is CallExpr call) return Call(call, scope, value);

        // 'beats |> notes() [ A3 C4 ].gate' is the sequencer with the beats
        // arriving, and then its gate: the selector binds tighter than the pipe,
        // so it is the stage's output that is chosen and not the source's.
        if (expr.Stage is SelectExpr select) return Select(select, scope, value);

        return Refuse(IssueCode.BadStage, expr.Line, expr.Column, "only a module or a socket may follow '|>'.");
    }

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
        var wires = new List<(int Port, Value Value)>();

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
                    issues.Complain(IssueCode.PlaceholderMisplaced, argument.Line, argument.Column,
                        "'_' goes in a named argument, 'socket: _', so it says which socket.");
                }

                continue;
            }

            var port = SocketNames.Find(def.Inputs, argument.Name);

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

                issues.Complain(IssueCode.UnknownSocket, argument.Line, argument.Column,
                    $"'{def.Name}' has no socket called '{argument.Name}'. It has {SocketNames.List(def.Inputs)}.");
                continue;
            }

            if (!taken.Add(port))
            {
                issues.Complain(IssueCode.GivenTwice, argument.Line, argument.Column, $"'{argument.Name}' is given twice.");
                continue;
            }

            if (Placeholder(argument.Value))
            {
                if (piped is null)
                    issues.Complain(IssueCode.PlaceholderMisplaced, argument.Line, argument.Column, "'_' stands for what is piped in, and nothing is.");
                else if (landing is not null)
                    issues.Complain(IssueCode.PlaceholderTwice, argument.Line, argument.Column, "'_' is written twice, and a pipe brings one signal.");
                else
                    landing = port;

                continue;
            }

            if (!Piped(argument) && Bind(argument.Value, scope) is { } value)
            {
                wires.Add((port, value));
                sites[port] = new Site(Leftmost(argument.Value).Line, Leftmost(argument.Value).Column);
            }
        }

        var piping = new List<(int Port, Value Value)>();

        if (landing is { } at)
            piping.Add((at, piped!.Part(0)));
        else if (piped is not null)
        {
            var result = PipeLanding.Resolve(def, piped, taken, expr);

            if (result.Issue is { } issue)
            {
                issues.Complain(issue.Code, expr.Line, expr.Column, issue.Message);
                return null;
            }

            piping.AddRange(result.Inputs);
            foreach (var (port, _) in result.Inputs) taken.Add(port);
        }

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
                    issues.Complain(IssueCode.TooManyArguments, argument.Line, argument.Column,
                        $"'{def.Name}' has no socket left for this. It has {SocketNames.List(def.Inputs)}.");
                    break;
                }

                var port = free.Dequeue();
                taken.Add(port);

                if (!refused && Bind(part, scope) is { } value)
                {
                    wires.Add((port, value));
                    sites[port] = new Site(Leftmost(part).Line, Leftmost(part).Column);
                }
            }
        }

        var node = wiring.Place(def, [.. piping, .. wires], expr.Line, expr.Column, sites);

        if (node is Placed made)
        {
            // Where this module stands in the file. The call rather than the
            // binding, because a call is what a module is: four of them on one
            // line are four modules to point at.
            var site = new Site(expr.Line, expr.Column);

            sourceSites.Call(made.Id, site);
            sourceSites.Mention(site, made.Id);

            if (patch.Find(made.Id) is { } instance)
                foreach (var (owner, field, setting, where) in fields)
                {
                    Apply(instance, owner, field, setting);

                    // By the key rather than by whichever of key and label the
                    // file happened to use, since a label is free to be reworded
                    // and a caller asking for the field will have the key.
                    sourceSites.Write(made.Id, field.Key, where);
                }
        }

        foreach (var (path, line, column) in paths) carrying.File(node, def, path, line, column);
        if (expr.Block is { } block) carrying.Block(node, def, block, expr.Line, expr.Column, (expr.BlockLine, expr.BlockColumn));

        return node;
    }

    /// <summary>Whether an argument is <c>_</c>, which stands for what is piped in.</summary>
    private static bool Placeholder(Expr value) => value is NameExpr { Name: "_", Port: null };

    /// <summary>Whether an expression is written wholly in literals, and so places no module.</summary>
    private static bool Constant(Expr expr) => expr switch
    {
        NumberExpr or TextExpr => true,
        NegateExpr negate => Constant(negate.Value),
        BinaryExpr binary => Constant(binary.Left) && Constant(binary.Right),
        _ => false,
    };

    /// <summary>
    /// Whether an argument holds a pipeline, said where it does. A pipeline is a
    /// statement's spine, so one inside an argument is written as a <c>let</c>
    /// above and a name here, and every edit stays a one-line edit.
    /// </summary>
    private bool Piped(Argument argument)
    {
        if (!Pipes(argument.Value)) return false;

        var start = Leftmost(argument.Value);

        issues.Complain(IssueCode.PipelineInArgument, start.Line, start.Column,
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
                if (SocketNames.Same(field.Key, name) || SocketNames.Same(field.Label, name)) return (extra, field);

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
            issues.Complain(IssueCode.FieldNeedsValue, argument.Line, argument.Column, $"'{field.Label}' is set to a value, not to a signal.");

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
        // statement rather than to the def — see NodeIdentity.Stamping.
        NodeIdentity.Position? outer = null;

        try
        {
            if (Arguments(macro, call, scope, piped) is not { } arguments) return null;

            // Every call stamps out its own copy, so every call is its own
            // segment: two calls in one statement place two sets of modules and
            // the names have to tell them apart. Numbered, because a call site
            // has nothing else to be known by — which is why moving one call
            // above another in the same statement renames both.
            outer = identity.Enter(identity.Stamping(macro.Name));

            // A fresh scope off the top, not off the caller's: a def sees its
            // parameters and the defs, and nothing of wherever it was called
            // from. Every call stamps out its own copy of the body, so two calls
            // share no module — a value that should be shared is passed in.
            var inner = new Scope(null);

            for (var i = 0; i < macro.Parameters.Count; i++) inner.Set(macro.Parameters[i].Name, arguments[i], macro.Line);

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

            if (outer is { } saved) identity.Leave(saved);
        }
    }

    /// <summary>
    /// What each of a def's parameters gets at one call: named arguments claim
    /// theirs, the pipe lands on <c>in</c> or else the first parameter left,
    /// the rest go in order, and a default fills what nothing gave.
    /// </summary>
    private Value[]? Arguments(DefStatement macro, CallExpr call, Scope scope, Value? piped)
    {
        var parameters = macro.Parameters;
        var slots = Enumerable.Repeat(-1, call.Arguments.Count).ToArray();
        var given = new bool[parameters.Count];
        var landing = -1;
        var sound = true;

        var placed = call.Arguments.Count(a => Placeholder(a.Value));

        if (placed > 1)
            return Refused(IssueCode.PlaceholderTwice, call, "'_' is written twice, and a pipe brings one signal.");

        if (placed == 1 && piped is null)
            return Refused(IssueCode.PlaceholderMisplaced, call, "'_' stands for what is piped in, and nothing is.");

        for (var i = 0; i < call.Arguments.Count; i++)
        {
            var argument = call.Arguments[i];

            if (argument.Name is null) continue;

            var index = IndexOf(parameters, argument.Name);

            if (index < 0)
            {
                issues.Complain(IssueCode.UnknownParameter, argument.Line, argument.Column,
                    $"'{macro.Name}' has no parameter called '{argument.Name}'. It has {Listed(parameters)}.");
                sound = false;
            }
            else if (given[index])
            {
                issues.Complain(IssueCode.GivenTwice, argument.Line, argument.Column, $"'{argument.Name}' is given twice.");
                sound = false;
            }
            else
            {
                given[index] = true;
                slots[i] = index;
            }
        }

        if (piped is not null && placed == 0)
        {
            var signal = IndexOf(parameters, "in");

            if (signal >= 0 && given[signal])
            {
                return Refused(IssueCode.PipeLandsNowhere, call,
                    $"'{macro.Name}': its 'in' is already given, so say where the pipe lands: '{macro.Name}(name: _)'.");
            }

            landing = signal >= 0 ? signal : Array.IndexOf(given, false);

            if (landing < 0)
                return Refused(IssueCode.NoSocketFree, call, $"'{macro.Name}' has no parameter free for what is arriving.");

            given[landing] = true;
        }

        var free = new Queue<int>(Enumerable.Range(0, parameters.Count).Where(p => !given[p]));
        var extra = 0;

        for (var i = 0; i < call.Arguments.Count; i++)
        {
            if (call.Arguments[i].Name is not null) continue;

            if (free.Count == 0)
            {
                extra++;
                continue;
            }

            slots[i] = free.Dequeue();
            given[slots[i]] = true;
        }

        if (extra > 0)
        {
            return Refused(IssueCode.DefArity, call,
                $"'{macro.Name}' takes {parameters.Count} arguments and {parameters.Count + extra} were given.");
        }

        var missing = Enumerable.Range(0, parameters.Count).Where(p => !given[p] && parameters[p].Default is null).ToList();

        if (sound && missing.Count > 0)
        {
            return Refused(IssueCode.DefArity, call,
                $"'{macro.Name}' needs {string.Join(" and ", missing.Select(p => $"'{parameters[p].Name}'"))}.");
        }

        // Bound in the order written, since that is the order a call's modules are named in.
        var values = new Value?[parameters.Count];

        if (landing >= 0) values[landing] = piped;

        for (var i = 0; i < call.Arguments.Count; i++)
        {
            var argument = call.Arguments[i];

            if (slots[i] < 0) continue;

            if (Placeholder(argument.Value)) values[slots[i]] = piped;
            else if (!Piped(argument) && Bind(argument.Value, scope) is { } value) values[slots[i]] = value;
            else sound = false;
        }

        for (var p = 0; p < parameters.Count; p++)
        {
            if (given[p] || parameters[p].Default is not { } fallback) continue;

            if (Bind(fallback, new Scope(null)) is { } value) values[p] = value;
            else sound = false;
        }

        return sound ? [.. values.Select(value => value!)] : null;
    }

    private static int IndexOf(IReadOnlyList<Parameter> parameters, string name)
    {
        for (var i = 0; i < parameters.Count; i++)
            if (parameters[i].Name == name) return i;

        return -1;
    }

    private static string Listed(IReadOnlyList<Parameter> parameters) =>
        parameters.Count == 0 ? "none" : string.Join(", ", parameters.Select(p => $"'{p.Name}'"));

    private Value[]? Refused(string code, CallExpr call, string message)
    {
        issues.Complain(code, call.Line, call.Column, message);
        return null;
    }

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

        issues.Complain(code, line, column, refusal);
        return null;
    }

    /// <summary>The node and socket a written name points at, for a knob or a wire.</summary>
    private (NodeInstance Node, NodeDef Def, int Port)? Input(NameExpr target, Scope scope)
    {
        if (target.Port is null)
        {
            issues.Complain(IssueCode.SocketUnsaid, target.Line, target.Column, "say which socket this is.");
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
            issues.Complain(IssueCode.NotAModule, target.Line, target.Column, $"'{target.Name}' is not a module, so it has no sockets.");
            return null;
        }
        else
        {
            Unknown(target);
            return null;
        }

        var port = SocketNames.Find(def.Inputs, target.Port);

        if (port >= 0)
        {
            // 'out.left' is the Output named, and the Output is a module with a
            // panel of its own — so the words are somewhere to click as well.
            sourceSites.Mention(new Site(target.Line, target.Column), node.Id);

            return (node, def, port);
        }

        issues.Complain(IssueCode.UnknownSocket, target.Line, target.Column,
            $"'{def.Name}' has no socket called '{target.Port}'. It has {SocketNames.List(def.Inputs)}.");

        return null;
    }
}
