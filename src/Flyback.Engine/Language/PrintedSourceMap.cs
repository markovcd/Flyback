using Flyback.Core.Graph;
using Flyback.Core.Language.Ast;
using Flyback.Core.Language.Ast.Expressions;
using Flyback.Core.Language.Ast.Statements;

namespace Flyback.Core.Language;

/// <summary>Re-reads printed source to locate the modules and editable values in it.</summary>
internal static class PrintedSourceMap
{
    /// <remarks>
    /// The writer supplies the calls in output order; parsing supplies their
    /// positions. Matching those lists keeps mapping independent of line layout.
    /// </remarks>
    internal static SourceMap Create(
        string source,
        Patch patch,
        ModuleCatalog modules,
        PatchPrintPlan plan,
        IReadOnlyList<Guid> order)
    {
        var issues = new List<LanguageIssue>();
        var read = new Parser(StatementTokens.ForParsing(Lexer.Scan(source, issues)), issues).Parse();

        if (issues.Count > 0) return SourceMap.Empty;

        var written = new List<Expr>();
        var mentioned = new List<NameExpr>();
        var turns = new List<KnobStatement>();
        var principals = new List<Expr>();

        foreach (var statement in read) Gather(statement, written, mentioned, turns, principals);

        // A ranged panel knob looks like a call but does not place a module.
        var panel = read.OfType<PanelStatement>().Select(statement => statement.Name).ToHashSet(StringComparer.Ordinal);

        written.RemoveAll(expr => expr is CallExpr call && panel.Contains(call.Target));
        written.Sort((a, b) => a.Line == b.Line ? a.Column - b.Column : a.Line - b.Line);

        // A mismatch means the printer's call order cannot safely be mapped.
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

            if (written[i] is not CallExpr call) continue;
            if (patch.Find(node) is not { } instance || modules.Get(instance.TypeId) is not { } def) continue;

            // A function inside a sum belongs to its enclosing Expression module.
            if (instance.TypeId == NodeCatalog.ExpressionTypeId && call.Target is not ("expression" or NodeCatalog.ExpressionTypeId)) continue;

            calls[node] = site;

            foreach (var argument in call.Arguments)
            {
                if (argument.Name is null) continue;
                if (Canonical(def, argument.Name) is not { } name) continue;

                values[(node, name)] = Wrote(argument.Value);
            }
        }

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

        var knobs = plan.Panel.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

        foreach (var knob in read.OfType<PanelStatement>())
            if (knobs.TryGetValue(knob.Name, out var control))
                values[(control, PatchPrinter.PanelKnob)] = Wrote(knob.Value);

        var blocks = new List<(Site Where, Guid Group)>();

        foreach (var block in read.OfType<GroupStatement>())
        {
            var inside = new List<Expr>();

            foreach (var statement in block.Body) Gather(statement, inside, [], [], []);

            if (inside.FirstOrDefault(placed.ContainsKey) is { } call && patch.GroupOf(placed[call])?.Id is { } group)
                blocks.Add((new Site(block.Line, block.Column), group));
        }

        var named = plan.Names.ToDictionary(pair => pair.Key, pair => pair.Value);

        if (sink is not null) named[sink.Id] = "out";

        return new SourceMap(
            source,
            mentions,
            calls,
            values,
            named,
            principals.Where(placed.ContainsKey).Select(call => placed[call]).ToHashSet(),
            blocks);
    }

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

            case GroupStatement group:
                foreach (var inside in group.Body) Gather(inside, calls, names, turns, principals);
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

            // Only the outer operator places an Expression for the whole sum.
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

    private static Expr? Principal(Expr expr) => expr switch
    {
        PipeExpr pipe => Principal(pipe.Stage) ?? Principal(pipe.Source),
        CallExpr call => call,
        BinaryExpr or NegateExpr when Signalled(expr) => expr,
        _ => null,
    };

    private static bool Signalled(Expr expr) => expr switch
    {
        NumberExpr => false,
        NegateExpr negate => Signalled(negate.Value),
        BinaryExpr binary => Signalled(binary.Left) || Signalled(binary.Right),
        _ => true,
    };

    private static Site? Wrote(Expr expr) => expr switch
    {
        NumberExpr number => new Site(number.Line, number.Column),
        NegateExpr negate when negate.Value is NumberExpr => new Site(negate.Line, negate.Column),
        TextExpr text => new Site(text.Line, text.Column),
        _ => null,
    };

    private static string? Canonical(NodeDef def, string written)
    {
        foreach (var port in def.Inputs)
            if (string.Equals(port.Name.Replace(' ', '_'), written, StringComparison.OrdinalIgnoreCase))
                return port.Name.Replace(' ', '_');

        foreach (var extra in def.Extras)
            foreach (var field in extra.Fields)
                if (string.Equals(field.Key, written, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(field.Label, written, StringComparison.OrdinalIgnoreCase))
                    return field.Key;

        return null;
    }
}
