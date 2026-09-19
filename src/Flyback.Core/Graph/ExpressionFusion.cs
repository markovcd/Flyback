using System.Globalization;
using System.Text.Json.Nodes;

namespace Flyback.Core.Graph;

/// <summary>
/// Folds Maths modules into Expressions: each chain whose modules feed only the
/// next becomes one formula over what is wired into it.
/// </summary>
/// <remarks>
/// Exact by construction (ADR-0108): a formula lowers through the emit functions
/// of the modules it names, in the order their walk would have, so a fused patch
/// is the same program. What is left alone is what a formula cannot hold without
/// changing the patch: a module that follows a panel knob, one read through a
/// wire that closes a loop, one with a name, one fed no wire at all and one of
/// more than two inputs; and a chain stops where a fifth signal or a formula too
/// long to read would begin.
/// </remarks>
public static class ExpressionFusion
{
    private const int Sockets = 4;

    /// <summary>
    /// The most inputs a module folded may have. A Clamp, a Mix, a Smoothstep or a
    /// Remap keeps its knobs, whose names say what each number is.
    /// </summary>
    private const int MostInputs = 2;

    /// <summary>
    /// The longest a formula folded may run, so it reads on a line of text and
    /// most of it fits a module's title.
    /// </summary>
    private const int Longest = 60;

    /// <summary>The Maths modules the language writes as an operator.</summary>
    private static readonly Dictionary<string, char> Operators = new()
    {
        ["math.add"] = '+',
        ["math.sub"] = '-',
        ["math.mul"] = '*',
        ["math.div"] = '/',
        ["math.mod"] = '%',
    };

    /// <summary>Folds <paramref name="patch"/> in place, and hands it back.</summary>
    public static Patch Fuse(Patch patch, ModuleCatalog modules)
    {
        if (modules.Get(NodeCatalog.ExpressionTypeId) is not { } expression
            || expression.Extra<FormulaExtra>() is not { } extra)
        {
            return patch;
        }

        var names = extra.Functions.ToDictionary(pair => pair.Value.TypeId, pair => pair.Key);
        var backwards = Cycles.Backwards(patch);

        bool Candidate(NodeInstance node) =>
            names.ContainsKey(node.TypeId)
            && modules.Require(node.TypeId).Inputs.Count <= MostInputs
            && node.Name is null
            && !ControlMap.All(node).Any()
            && node.InputValues.All(float.IsFinite)
            && patch.Connections.Any(c => c.TargetNode == node.Id)
            && !patch.Connections.Any(c => backwards.Contains(c) && (c.TargetNode == node.Id || c.SourceNode == node.Id));

        var roots = new Queue<NodeInstance>(patch.Nodes.Where(node => Candidate(node) && Consumer(node) is null));
        var absorbed = new HashSet<Guid>();

        while (roots.TryDequeue(out var root))
        {
            var (term, taken, left) = Tree(root);
            var inputs = new List<(Guid Node, int Port)>();

            Gather(term, inputs);

            // One reading more signals than there are sockets stays the module it
            // is, and what feeds it is folded on its own.
            if (inputs.Count > Sockets
                || Written(term, signal => Formula.Sockets[inputs.IndexOf((signal.Node, signal.Port))].ToString()) is not { } formula
                || Formula.Read(formula, extra.Functions, out _) is null)
            {
                foreach (var wire in patch.Connections.Where(c => c.TargetNode == root.Id).ToList())
                    if (patch.Find(wire.SourceNode) is { } source && Candidate(source) && Consumer(source) == root)
                        roots.Enqueue(source);

                continue;
            }

            absorbed.UnionWith(taken);
            foreach (var node in left) roots.Enqueue(node);

            Replace(root, formula, inputs);
        }

        foreach (var id in absorbed) patch.Remove(id);

        return patch;

        // The candidate a node would fold into: its one reader, where that is a
        // candidate in the same box reading it forwards.
        NodeInstance? Consumer(NodeInstance node)
        {
            if (patch.Connections.Count(c => c.SourceNode == node.Id) != 1) return null;

            var wire = patch.Connections.Single(c => c.SourceNode == node.Id);

            return patch.Find(wire.TargetNode) is { } reader
                && Candidate(reader)
                && ReferenceEquals(patch.GroupOf(reader.Id), patch.GroupOf(node.Id))
                    ? reader
                    : null;
        }

        // What a node would fold to, the modules that would fold into it, and the
        // readers-to-be that would not fit and stand as roots of their own.
        (Term Term, List<Guid> Taken, List<NodeInstance> Left) Tree(NodeInstance node)
        {
            var def = modules.Require(node.TypeId);
            var arguments = new Term[def.Inputs.Count];
            var taken = new List<Guid>();
            var left = new List<NodeInstance>();

            for (var port = 0; port < arguments.Length; port++)
            {
                if (patch.IncomingTo(node.Id, port) is not { } wire)
                {
                    arguments[port] = new Literal(node.InputValues[port]);
                    continue;
                }

                if (patch.Find(wire.SourceNode) is { } source && Candidate(source) && Consumer(source) == node)
                {
                    var inner = Tree(source);

                    // This node with the branch taken in, reading the sockets still
                    // to come as the signal or the knob each is: what it would need
                    // in sockets, and how long it would run.
                    var trial = (Term[])arguments.Clone();
                    trial[port] = inner.Term;

                    for (var later = port + 1; later < trial.Length; later++)
                        trial[later] = patch.IncomingTo(node.Id, later) is { } next
                            ? new Signal(next.SourceNode, next.SourcePort)
                            : new Literal(node.InputValues[later]);

                    var needed = new List<(Guid Node, int Port)>();
                    Gather(new Call(node.TypeId, trial), needed);

                    if (needed.Count <= Sockets && Written(new Call(node.TypeId, trial), _ => "a") is { Length: <= Longest })
                    {
                        arguments[port] = inner.Term;
                        taken.Add(source.Id);
                        taken.AddRange(inner.Taken);
                        left.AddRange(inner.Left);
                        continue;
                    }

                    left.Add(source);
                }

                arguments[port] = new Signal(wire.SourceNode, wire.SourcePort);
            }

            return (new Call(node.TypeId, arguments), taken, left);
        }

        // Swaps a root for an Expression under the same id, so its readers, its box
        // and its place on the canvas are the Expression's.
        void Replace(NodeInstance root, string formula, List<(Guid Node, int Port)> inputs)
        {
            var fused = NodeInstance.Create(expression, root.X, root.Y, root.Id);
            fused.SetState(FormulaExtra.StateKey, new JsonObject { [FormulaExtra.FormulaField] = formula });

            patch.Nodes[patch.Nodes.IndexOf(root)] = fused;
            patch.Connections.RemoveAll(c => c.TargetNode == root.Id);
            patch.GroupOf(root.Id)?.Exposed.RemoveAll(s => s.Node == root.Id && !s.IsOutput);

            for (var socket = 0; socket < inputs.Count; socket++)
                patch.Connect(inputs[socket].Node, inputs[socket].Port, root.Id, socket);
        }

        string? Written(Term term, Func<Signal, string> socket)
        {
            switch (term)
            {
                case Literal literal:
                    return literal.Value.ToString("R", CultureInfo.InvariantCulture);

                case Signal signal:
                    return socket(signal);

                case Call { TypeId: "math.neg", Arguments: [var operand] }:
                    return Written(operand, socket) is { } inner
                        ? Strength(operand) < 3 ? $"-({inner})" : $"-{inner}"
                        : null;

                case Call { Arguments: [var left, var right] } call when Operators.TryGetValue(call.TypeId, out var sign):
                {
                    if (Written(left, socket) is not { } l || Written(right, socket) is not { } r) return null;

                    var strength = Strength(call);

                    if (Strength(left) < strength) l = $"({l})";
                    if (Strength(right) <= strength) r = $"({r})";

                    return $"{l} {sign} {r}";
                }

                case Call call:
                {
                    var arguments = call.Arguments.Select(argument => Written(argument, socket)).ToList();

                    return arguments.Any(argument => argument is null)
                        ? null
                        : $"{names[call.TypeId]}({string.Join(", ", arguments)})";
                }

                default:
                    return null;
            }
        }
    }

    private static void Gather(Term term, List<(Guid Node, int Port)> inputs)
    {
        switch (term)
        {
            case Signal signal when !inputs.Contains((signal.Node, signal.Port)):
                inputs.Add((signal.Node, signal.Port));
                break;

            case Call call:
                foreach (var argument in call.Arguments) Gather(argument, inputs);
                break;
        }
    }

    /// <summary>How tightly a part holds together as written: a sum least, a value or a call most.</summary>
    private static int Strength(Term term) => term switch
    {
        Call { TypeId: "math.neg" } => 3,
        Call { TypeId: "math.add" or "math.sub" } => 1,
        Call call when Operators.ContainsKey(call.TypeId) => 2,
        Literal { Value: < 0 } => 3,
        Literal { Value: 0 } literal when float.IsNegative(literal.Value) => 3,
        _ => 4,
    };

    private abstract record Term;

    private sealed record Literal(float Value) : Term;

    private sealed record Signal(Guid Node, int Port) : Term;

    private sealed record Call(string TypeId, IReadOnlyList<Term> Arguments) : Term;
}
