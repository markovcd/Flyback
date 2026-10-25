using System.Text.Json.Nodes;
using Flyback.Core.Graph;
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

    /// <summary>Whether a group block is open, whose box holds whatever a def call inside it makes.</summary>
    private bool inBlock;

    private readonly SourceSites sourceSites = new();
    private readonly Carrying carrying;
    private readonly PatchLines lines;

    private readonly Boxes boxes;

    /// <summary>Plugins a <c>requires</c> line named that this build does not have.</summary>
    private readonly List<string> missing = [];

    /// <summary>The line each top-level name is bound on, for saying so where one is read above it.</summary>
    private readonly Dictionary<string, int> boundOn = [];

    /// <summary>Every pass the patch's loops have stamped out so far, against <see cref="MostPasses"/>.</summary>
    private int passes;

    /// <summary>
    /// The most passes one patch's loops may stamp out between them. Text is outside
    /// input, and a loop over a million items would place a million modules.
    /// </summary>
    internal const int MostPasses = 4096;

    /// <summary>The word that adds a list up, where nothing else is called it.</summary>
    private const string SumWord = "sum";

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

        moduleNames = ModuleNames.For(modules);
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

        // Each line for itself rather than its group's, so a name read above
        // its binding is pointed at the line that binds it.
        foreach (var statement in statements.SelectMany(s => s is GroupStatement group ? group.Body : [s]))
            foreach (var name in statement.Binds) boundOn.TryAdd(name, statement.Line);

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

    private string? Builtin(string name) => name switch
    {
        "t" => "the clock",
        "out" => "the Output",
        "_" => "what a pipe brings in",
        "let" or "def" or "group" or "for" => "a word of the language",
        _ => Coordinate(name) >= 0 ? "one of the picture's coordinates" : null,
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
        var outer = identity.Enter(statement.Naming ?? identity.Anonymous());

        Ran(statement, scope);

        identity.Leave(outer);
    }

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

                    if (fallback is not null && !fallback.Constant)
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
                    boxes.Name(value, let.Name);
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

        if (Unzipped(several, statement.Names.Count) is { } lists) several = lists;

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

    /// <summary>
    /// A list whose items are all tuples of <paramref name="parts"/>, as one list per part,
    /// or null for anything else.
    /// </summary>
    private static Several? Unzipped(Several list, int parts)
    {
        if (!list.Listed || !list.Items.All(item => item is Several { Listed: false } tuple && tuple.Items.Count == parts)) return null;

        return new Several([.. Enumerable.Range(0, parts)
            .Select(part => new Several([.. list.Items.Select(item => ((Several)item).Items[part])]) { Listed = true })]);
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
        var outerBlock = inBlock;

        inBlock = true;

        foreach (var child in statement.Body)
        {
            switch (child)
            {
                // A box is drawn round modules, and each of these is about the
                // whole patch or the panel, which no box holds.
                case GroupStatement nested:
                    issues.Complain(IssueCode.GroupInGroup, nested.Line, nested.Column,
                        "a group cannot hold another group. Close this one first.");
                    Unmade(nested.Binds, scope, nested.Line);
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

        inBlock = outerBlock;

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
        foreach (var name in statement.Body.Where(child => child is not GroupStatement).SelectMany(child => child.Binds))
            if (inner.Entry(name) is { } item) scope.Set(name, item.Value, item.Line);
    }

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
        RangeExpr range => Refuse(IssueCode.RangeOutsideArgument, range.Line, range.Column, "a range only means something as an argument, or as what a loop counts over."),
        ListExpr list => Items(list.Items, scope) is { } items ? items with { Listed = true } : null,
        TupleExpr tuple => Items(tuple.Items, scope),
        ForExpr loop => Loop(loop, scope),
        _ => null,
    };

    /// <summary>A list or a tuple, item by item; null where any item fails.</summary>
    private Several? Items(IReadOnlyList<Expr> items, Scope scope)
    {
        var values = new List<Value>(items.Count);
        var sound = true;

        foreach (var item in items)
        {
            if (Bind(item, scope) is { } value) values.Add(value);
            else sound = false;
        }

        return sound ? new Several(values) : null;
    }

    private Value? Read(NameExpr expr, Scope scope)
    {
        if (expr.Port is null && Source(expr.Name) is { } value) return value;

        if (expr is { Name: "t", Port: { } port }) return Output(wiring.Clock(), port, expr.Line, expr.Column);

        if (expr.Name == "out")
        {
            return Refuse(IssueCode.OutputIsNotASource, expr.Line, expr.Column,
                "the Output has nothing to read. Pipe something into 'out.color' or 'out.left'.");
        }

        if (expr.Placeholder)
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
                return Bind(expr, scope) is { } bound ? Term(bound, expr.Line, expr.Column) : null;
        }
    }

    /// <summary>A value as a term of a sum: a number, or a signal read once.</summary>
    private Formulas.Term? Term(Value value, int line, int column)
    {
        if (value is Figure figure) return new Formulas.Operand(figure);

        // Each reference to a panel knob is a distinct formula input.
        if (value is Dial) return new Formulas.Signal(value, (Guid.NewGuid(), 0));

        if (Formulas.From(value) is { } from) return new Formulas.Signal(value, from);

        issues.Complain(IssueCode.NotASignal, line, column, "this is not a signal, so nothing can be wired from it.");
        return null;
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
        if (name == "t") return wiring.Clock().Part(0);

        var port = Coordinate(name);

        return port < 0 ? null : wiring.Coordinates().Part(port);
    }

    /// <summary>The output of the Coordinates a bare word reads, or -1 where the word is not one of them.</summary>
    private int Coordinate(string name)
    {
        var outputs = modules.Require(NodeCatalog.CoordTypeId).Outputs;

        for (var i = 0; i < outputs.Count; i++)
            if (outputs[i].Name == name) return i;

        return -1;
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
        if (expr.Target == SumWord && scope.Find(SumWord) is null && !moduleNames.Knows(SumWord)) return Summed(expr, scope, piped);

        if (Module(expr.Target, expr.Line, expr.Column) is not { } def) return null;

        if (NodeCatalog.IsSink(def.TypeId))
        {
            return Refuse(IssueCode.OutputIsNotASource, expr.Line, expr.Column,
                "every patch already has its Output. Wire into 'out.color' or 'out.left'.");
        }

        var arguments = new CallArguments();

        NamedArguments(expr, def, scope, piped, arguments);
        if (!PipeLands(expr, def, piped, arguments)) return null;
        FreeArguments(expr, def, scope, arguments);

        var node = Place(expr, def, arguments);

        foreach (var (path, line, column) in arguments.Paths) carrying.File(node, def, path, line, column);
        if (expr.Block is { } block) carrying.Block(node, def, block, expr.Line, expr.Column, (expr.BlockLine, expr.BlockColumn));

        return node;
    }

    /// <summary>What a call's arguments come to, gathered a phase at a time before the module is placed.</summary>
    private sealed class CallArguments
    {
        public HashSet<int> Taken { get; } = [];
        public List<(int Port, Value Value)> Piping { get; } = [];
        public List<(int Port, Value Value)> Wires { get; } = [];

        /// <summary>Where each argument's value is written, so a complaint about one points at it.</summary>
        public Dictionary<int, Site> Sites { get; } = [];
        public List<(string Path, int Line, int Column)> Paths { get; } = [];
        public List<(NodeExtra Owner, ExtraField Field, JsonNode Value, Site Where)> Fields { get; } = [];

        /// <summary>The socket a named <c>_</c> gave what is piped in, if one did.</summary>
        public int? Landing { get; set; }
    }

    /// <summary>
    /// The named arguments, first because what they claim decides where everything
    /// else can go: a socket by name, a field of the module, or <c>_</c> for the pipe.
    /// </summary>
    private void NamedArguments(CallExpr expr, NodeDef def, Scope scope, Value? piped, CallArguments arguments)
    {
        foreach (var argument in expr.Arguments)
        {
            if (argument.Name is null)
            {
                if (argument.Value.Placeholder)
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

                        arguments.Fields.Add((owner, field, setting, where));
                    }

                    continue;
                }

                issues.Complain(IssueCode.UnknownSocket, argument.Line, argument.Column,
                    $"'{def.Name}' has no socket called '{argument.Name}'. It has {SocketNames.List(def.Inputs)}.");
                continue;
            }

            if (!arguments.Taken.Add(port))
            {
                issues.Complain(IssueCode.GivenTwice, argument.Line, argument.Column, $"'{argument.Name}' is given twice.");
                continue;
            }

            if (argument.Value.Placeholder)
            {
                if (piped is null)
                    issues.Complain(IssueCode.PlaceholderMisplaced, argument.Line, argument.Column, "'_' stands for what is piped in, and nothing is.");
                else if (arguments.Landing is not null)
                    issues.Complain(IssueCode.PlaceholderTwice, argument.Line, argument.Column, "'_' is written twice, and a pipe brings one signal.");
                else
                    arguments.Landing = port;

                continue;
            }

            if (!Piped(argument) && Bind(argument.Value, scope) is { } value)
            {
                arguments.Wires.Add((port, value));
                arguments.Sites[port] = new Site(argument.Value.Leftmost.Line, argument.Value.Leftmost.Column);
            }
        }
    }

    /// <summary>
    /// Where what is piped in lands: the socket a named <c>_</c> chose, or the ones
    /// <see cref="PipeLanding"/> works out. False where the pipe has nowhere to go,
    /// said as an issue.
    /// </summary>
    private bool PipeLands(CallExpr expr, NodeDef def, Value? piped, CallArguments arguments)
    {
        if (arguments.Landing is { } at)
        {
            arguments.Piping.Add((at, piped!.Part(0)));
            return true;
        }

        if (piped is null) return true;

        var result = PipeLanding.Resolve(def, piped, arguments.Taken, expr);

        if (result.Issue is { } issue)
        {
            issues.Complain(issue.Code, expr.Line, expr.Column, issue.Message);
            return false;
        }

        arguments.Piping.AddRange(result.Inputs);
        foreach (var (port, _) in result.Inputs) arguments.Taken.Add(port);
        return true;
    }

    /// <summary>The unnamed arguments, into whatever sockets the pipe and the named ones left, in order.</summary>
    private void FreeArguments(CallExpr expr, NodeDef def, Scope scope, CallArguments arguments)
    {
        var free = new Queue<int>(Enumerable.Range(0, def.Inputs.Count).Where(i => !arguments.Taken.Contains(i)));

        foreach (var argument in expr.Arguments)
        {
            if (argument.Name is not null || argument.Value.Placeholder) continue;

            if (argument.Spread)
            {
                Spread(argument, def, scope, free, arguments);
                continue;
            }

            if (argument.Value is TextExpr text)
            {
                arguments.Paths.Add((text.Value, argument.Line, argument.Column));
                continue;
            }

            // A text reached through a name, a loop's or a def's, names a file as the literal would.
            if (argument.Value is NameExpr { Port: null } word && scope.Find(word.Name) is Named named)
            {
                Mention(word, named);
                arguments.Paths.Add((named.Path, argument.Line, argument.Column));
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
                arguments.Taken.Add(port);

                if (!refused && Bind(part, scope) is { } value)
                {
                    arguments.Wires.Add((port, value));
                    arguments.Sites[port] = new Site(part.Leftmost.Line, part.Leftmost.Column);
                }
            }
        }
    }

    /// <summary><c>...list</c>: each item into the next free socket, a tuple's parts one socket each.</summary>
    private void Spread(Argument argument, NodeDef def, Scope scope, Queue<int> free, CallArguments arguments)
    {
        if (Piped(argument) || Bind(argument.Value, scope) is not { } value) return;

        if (value is not Several list)
        {
            issues.Complain(IssueCode.NotAList, argument.Line, argument.Column,
                "'...' spreads a list over the sockets left free, and this is one thing. Write it without the dots.");
            return;
        }

        var site = new Site(argument.Value.Leftmost.Line, argument.Value.Leftmost.Column);

        foreach (var part in list.Items.SelectMany(item => item is Several tuple ? tuple.Items : [item]))
        {
            if (part is Named named)
            {
                arguments.Paths.Add((named.Path, argument.Line, argument.Column));
                continue;
            }

            if (free.Count == 0)
            {
                issues.Complain(IssueCode.TooManyArguments, argument.Line, argument.Column,
                    $"'{def.Name}' has no socket left for the rest of this list. It has {SocketNames.List(def.Inputs)}.");
                return;
            }

            var port = free.Dequeue();
            arguments.Taken.Add(port);
            arguments.Wires.Add((port, part));
            arguments.Sites[port] = site;
        }
    }

    /// <summary>
    /// <c>sum(list)</c>, or a list piped into <c>sum()</c>: the items added up in order, as
    /// <c>a + b + c</c> would be (ADR-0208).
    /// </summary>
    private Value? Summed(CallExpr expr, Scope scope, Value? piped)
    {
        var (line, column) = (expr.Line, expr.Column);
        const string Shape = "'sum' adds up one list: 'sum(voices)', or 'voices |> sum()'.";

        Value? list;

        if (expr.Block is not null) return Refuse(IssueCode.NoBlock, expr.BlockLine, expr.BlockColumn, "'sum' carries no block. " + Shape);

        if (piped is not null && expr.Arguments.Count == 0) list = piped;
        else if (piped is null && expr.Arguments is [{ Name: null, Spread: false } only])
            list = Piped(only) ? null : Bind(only.Value, scope);
        else return Refuse(IssueCode.NotAList, line, column, Shape);

        if (list is null) return null;

        if (list is not Several { Items.Count: > 0 } several)
        {
            return Refuse(IssueCode.NotAList, line, column, list is Several
                ? "there is nothing in this list to add up."
                : "'sum' adds up a list, and this is one thing. Give it a list, [a, b], or a loop.");
        }

        Formulas.Term? total = null;

        foreach (var item in several.Items)
        {
            if (Term(item, line, column) is not { } term) return null;

            total = total switch
            {
                null => term,
                Formulas.Operand a when term is Formulas.Operand b => Formulas.Fold(TokenKind.Plus, a.Figure, b.Figure, line, column, issues),
                _ => Fit(new Formulas.Operation('+', total, term, line, column)),
            };

            if (total is null) return null;
        }

        return total switch
        {
            Formulas.Operand operand => operand.Figure,
            Formulas.Signal signal => signal.Value,
            Formulas.Operation operation => Expression(operation),
            _ => null,
        };
    }

    // --- loops ----------------------------------------------------------------

    /// <summary>
    /// A loop's body stamped out once per item, each pass in a scope of its own off the
    /// loop's, worth the list of what each pass ends with (ADR-0208).
    /// </summary>
    private Value? Loop(ForExpr loop, Scope scope)
    {
        if (Each(loop.Source, scope) is not { } items) return null;

        for (var i = 0; i < loop.Names.Count; i++)
        {
            if (!Free(loop.Names[i], scope, loop.Line, loop.Column)) return null;

            if (loop.Names.Take(i).Contains(loop.Names[i], StringComparer.Ordinal))
                return Refuse(IssueCode.BoundTwice, loop.Line, loop.Column, $"'{loop.Names[i]}' is written twice. A name is bound once.");
        }

        if (passes + items.Count > MostPasses)
        {
            return Refuse(IssueCode.TooManyPasses, loop.Line, loop.Column,
                $"this would stamp the loop's body out {items.Count} times, and a patch's loops stamp out at most {MostPasses} between them.");
        }

        passes += items.Count;

        var body = Loopable(loop.Body);
        var outer = identity.Enter(identity.Stamping("for"));
        var results = new List<Value>(items.Count);

        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                var pass = identity.Enter(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var said = issues.Count;

                try
                {
                    var inner = new Scope(scope);

                    if (!Take(loop, items[i], inner)) return null;

                    foreach (var statement in body) Run(statement, inner);

                    if (loop.Results is { } several)
                    {
                        if (Items(several, inner) is { } tuple) results.Add(tuple);
                    }
                    else if (loop.Result is { } result && Bind(result, inner) is { } value)
                    {
                        results.Add(value);
                    }
                }
                finally
                {
                    identity.Leave(pass);
                }

                // A mistake in the body is the same mistake on every pass, and once is enough to say it.
                if (issues.Count > said) return null;
            }

            return new Several(results) { Listed = true };
        }
        finally
        {
            identity.Leave(outer);
        }
    }

    /// <summary>What a loop goes over: a list, or a count written as a range.</summary>
    private IReadOnlyList<Value>? Each(Expr source, Scope scope)
    {
        if (source is RangeExpr range) return Count(range, scope);

        if (Bind(source, scope) is not { } value) return null;
        if (value is Several list) return list.Items;

        issues.Complain(IssueCode.NotAList, source.Line, source.Column,
            "a loop goes over a list, [a, b, c], or a count, 1..4, and this is one thing.");
        return null;
    }

    /// <summary><c>1..4</c> as a loop counts it: every whole number from the first to the last, both ends in.</summary>
    private List<Value>? Count(RangeExpr range, Scope scope)
    {
        const string Shape = "a loop counts from one whole number up to another: 1..4.";

        if (Bind(range.Low, scope) is not Figure low || Bind(range.High, scope) is not Figure high) return null;

        if (low.Style is NumberStyle.Duration || high.Style is NumberStyle.Duration
            || low.Amount != Math.Floor(low.Amount) || high.Amount != Math.Floor(high.Amount) || high.Amount < low.Amount)
        {
            issues.Complain(IssueCode.BadCount, range.Line, range.Column, Shape);
            return null;
        }

        if (high.Amount - low.Amount >= MostPasses)
        {
            issues.Complain(IssueCode.TooManyPasses, range.Line, range.Column,
                $"a loop stamps its body out at most {MostPasses} times, and this counts {high.Amount - low.Amount + 1}.");
            return null;
        }

        var count = (int)(high.Amount - low.Amount) + 1;

        return [.. Enumerable.Range(0, count).Select(i => (Value)new Figure(low.Amount + i, low.Style))];
    }

    /// <summary>Binds a pass's names to its item, taking a tuple apart where the names are bracketed.</summary>
    private bool Take(ForExpr loop, Value item, Scope inner)
    {
        if (!loop.Tupled)
        {
            inner.Set(loop.Names[0], item, loop.Line);
            return true;
        }

        if (item is not Several tuple || tuple.Items.Count != loop.Names.Count)
        {
            var held = item is Several several ? $"{several.Items.Count} things" : "one thing";

            issues.Complain(IssueCode.TupleMismatch, loop.Line, loop.Column,
                $"an item of this list is {held}, and {loop.Names.Count} names are waiting for it.");
            return false;
        }

        for (var i = 0; i < loop.Names.Count; i++) inner.Set(loop.Names[i], tuple.Items[i], loop.Line);

        return true;
    }

    /// <summary>The statements a loop's body may repeat, each of the rest refused once.</summary>
    private List<Statement> Loopable(IReadOnlyList<Statement> body)
    {
        var kept = new List<Statement>(body.Count);

        foreach (var statement in body)
        {
            var refusal = statement switch
            {
                DefStatement => "a def is written once, outside the loop, and called inside it.",
                GroupStatement => "a group is drawn round what is inside it, so put the loop inside the group.",
                PanelStatement => "a panel knob belongs to the whole patch, so it is declared outside the loop and read inside it.",
                RequiresStatement or KeyboardStatement or LengthStatement or DescriptionStatement or AuthorStatement or TagsStatement
                    => "this is said once about the whole patch, so it is written outside the loop.",
                _ => null,
            };

            if (refusal is null) kept.Add(statement);
            else issues.Complain(IssueCode.NotInLoop, statement.Line, statement.Column, refusal);
        }

        return kept;
    }

    /// <summary>Places the module with its wires, and puts its fields and its place in the file on it.</summary>
    private Value Place(CallExpr expr, NodeDef def, CallArguments arguments)
    {
        var node = wiring.Place(def, [.. arguments.Piping, .. arguments.Wires], expr.Line, expr.Column, arguments.Sites);

        if (node is not Placed made) return node;

        // Where this module stands in the file. The call rather than the
        // binding, because a call is what a module is: four of them on one
        // line are four modules to point at.
        var site = new Site(expr.Line, expr.Column);

        sourceSites.Call(made.Id, site);
        sourceSites.Mention(site, made.Id);

        if (patch.Find(made.Id) is { } instance)
            foreach (var (owner, field, setting, where) in arguments.Fields)
            {
                Apply(instance, owner, field, setting);

                // By the key rather than by whichever of key and label the
                // file happened to use, since a label is free to be reworded
                // and a caller asking for the field will have the key.
                sourceSites.Write(made.Id, field.Key, where);
            }

        return node;
    }

    /// <summary>
    /// Whether an argument holds a pipeline, said where it does. A pipeline is a
    /// statement's spine, so one inside an argument is written as a <c>let</c>
    /// above and a name here, and every edit stays a one-line edit.
    /// </summary>
    private bool Piped(Argument argument)
    {
        if (!argument.Value.Pipes) return false;

        var start = argument.Value.Leftmost;

        issues.Complain(IssueCode.PipelineInArgument, start.Line, start.Column,
            "a pipeline cannot go inside an argument. Bind it with 'let' above and name it here.");
        return true;
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
                if (SocketNames.Same(field.Key, name) || SocketNames.Same(field.Label, name)) return (extra, field);

        return null;
    }

    /// <summary>What a field was set to, ready to be put on the node once it exists.</summary>
    private JsonNode? Setting(ExtraField field, Argument argument, Scope scope)
    {
        if (Bind(argument.Value, scope) is not { } value) return null;

        if (field is not ExtraField.Toggle && value is Figure { Amount: var amount } && !float.IsFinite((float)amount))
        {
            issues.Complain(IssueCode.OutOfRange, argument.Line, argument.Column, $"'{field.Label}' cannot hold that.");
            return null;
        }

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

            // Only the outermost call is drawn as a box, since a box holds
            // modules and never another box.
            var boxed = !inBlock && expanding.Count == 1;
            var before = boxed ? patch.Nodes.Select(n => n.Id).ToHashSet() : null;
            var where = identity.Where;
            var said = issues.Count;

            foreach (var statement in macro.Body) Run(statement, inner);

            Value? value;

            if (macro.Results is { } several)
            {
                var items = new List<Value>();

                foreach (var result in several)
                    if (Bind(result, inner) is { } item) items.Add(item);

                value = items.Count == several.Count ? new Several(items) : null;
            }
            else
            {
                value = macro.Result is null ? null : Bind(macro.Result, inner);
            }

            if (before is not null && value is not null && issues.Count == said)
            {
                var made = patch.Nodes.Where(n => !before.Contains(n.Id) && !wiring.IsShared(n.Id)).Select(n => n.Id);

                boxes.Call(macro.Name, NodeIdentity.FromName("def " + where), made, value);
            }

            return value;
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

        if (call.Arguments.FirstOrDefault(a => a.Spread) is { } spread)
        {
            issues.Complain(IssueCode.DefArity, spread.Line, spread.Column,
                $"'...' spreads a list over a module's sockets. Give '{macro.Name}' its arguments one by one, or pass it the list.");
            return null;
        }

        var placed = call.Arguments.Count(a => a.Value.Placeholder);

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

            if (argument.Value.Placeholder) values[slots[i]] = piped;
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
