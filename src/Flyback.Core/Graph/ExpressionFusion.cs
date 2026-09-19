using System.Globalization;
using System.Text.Json.Nodes;

namespace Flyback.Core.Graph;

/// <summary>
/// Folds Maths modules and Expressions into Expressions: each chain whose modules
/// feed only the next becomes one formula over what is wired into it, and every
/// Maths module of one or two inputs becomes an Expression of its own.
/// </summary>
/// <remarks>
/// Exact by construction (ADR-0108, ADR-0109): a formula lowers through the emit
/// functions of the modules it names, in the order their walk would have, so a
/// fused patch is the same program. A number is written into the formula, except
/// one a panel knob follows and two either side of an operator — the formula's
/// reader would add those together as floats, and the program never did — which
/// stay on sockets. A chain stops at a module of more than two inputs, a module
/// on a loop, a named module, a box's edge, a fifth signal, a formula
/// too long to read, and a part the formula would then compute once where the
/// modules computed it twice.
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

    /// <summary>The operators whose two numbers the formula's reader adds up as it reads.</summary>
    private static readonly HashSet<string> Folding = ["math.add", "math.sub", "math.mul", "math.div"];

    /// <summary>
    /// Whether a module is one an Expression stands for: a Maths module that lowers
    /// to ops alone and has no more than two inputs.
    /// </summary>
    public static bool Retired(NodeDef def) =>
        def.TypeId.StartsWith("math.", StringComparison.Ordinal)
        && def.TypeId != NodeCatalog.ExpressionTypeId
        && def.Inputs.Count <= MostInputs
        && def.Outputs.Count == 1
        && def.Extras.Count == 0;

    /// <summary>
    /// The formula a retired module is, over its inputs as sockets: <c>a * b</c>
    /// for a Multiply, <c>floor(a)</c> for a Floor.
    /// </summary>
    public static string Template(NodeDef retired)
    {
        if (Operators.TryGetValue(retired.TypeId, out var sign)) return $"a {sign} b";
        if (retired.TypeId == "math.neg") return "-a";

        var letters = Formula.Sockets[..retired.Inputs.Count].Select(letter => letter.ToString());

        return $"{retired.TypeId["math.".Length..]}({string.Join(", ", letters)})";
    }

    /// <summary>
    /// An Expression standing for a retired module where somebody asked for one:
    /// its formula over sockets, and the module's knobs as they rest. Null for a
    /// module an Expression does not stand for.
    /// </summary>
    public static NodeInstance? Standing(NodeDef retired, ModuleCatalog modules, double x, double y)
    {
        if (!Retired(retired) || modules.Get(NodeCatalog.ExpressionTypeId) is not { } expression) return null;

        var node = NodeInstance.Create(expression, x, y);
        node.SetState(FormulaExtra.StateKey, new JsonObject { [FormulaExtra.FormulaField] = Template(retired) });

        for (var port = 0; port < retired.Inputs.Count; port++) node.InputValues[port] = retired.Inputs[port].Default;

        return node;
    }

    /// <summary>Folds <paramref name="patch"/> in place, and hands it back.</summary>
    public static Patch Fuse(Patch patch, ModuleCatalog modules) => Fuse(patch, modules, null);

    /// <summary>
    /// The same, saying where each module went: every module folded away or made
    /// an Expression, and the Expression it is now part of.
    /// </summary>
    internal static Patch Fuse(Patch patch, ModuleCatalog modules, Dictionary<Guid, Guid>? into)
    {
        if (modules.Get(NodeCatalog.ExpressionTypeId) is not { } expression
            || expression.Extra<FormulaExtra>() is not { } extra)
        {
            return patch;
        }

        var names = extra.Functions.ToDictionary(pair => pair.Value.TypeId, pair => pair.Key);
        var backwards = Cycles.Backwards(patch);
        var looped = Looped(patch);
        var formulas = new Dictionary<Guid, Formula?>();

        Formula? FormulaOf(NodeInstance node) =>
            formulas.TryGetValue(node.Id, out var read)
                ? read
                : formulas[node.Id] = Formula.Read(FormulaExtra.Of(node), extra.Functions, out _);

        // An Expression with a wire into a socket its formula never reads is left
        // alone: folding it would take the wire away.
        bool Candidate(NodeInstance node) =>
            node.InputValues.All(float.IsFinite)
            && (node.TypeId == NodeCatalog.ExpressionTypeId
                ? FormulaOf(node) is not null
                    && Reads(node, expression.Inputs.Count) is var reads
                    && !patch.Connections.Any(c => c.TargetNode == node.Id && reads[c.TargetPort] == 0)
                : names.ContainsKey(node.TypeId) && modules.Get(node.TypeId) is { } def && Retired(def));

        var roots = new Queue<NodeInstance>(patch.Nodes.Where(node => Candidate(node) && Consumer(node) is null));
        var absorbed = new HashSet<Guid>();

        while (roots.TryDequeue(out var root))
        {
            var (term, taken, left) = Tree(root);
            var inputs = new List<Term>();

            Gather(term, inputs);

            // An Expression nothing folded into is left as it was written, and what
            // it turned away is folded on its own.
            if (root.TypeId == NodeCatalog.ExpressionTypeId && taken.Count == 0)
            {
                foreach (var node in left) roots.Enqueue(node);
                continue;
            }

            // One reading more signals than there are sockets stays the module it
            // is, and what feeds it is folded on its own.
            if (inputs.Count > Sockets
                || Written(term, part => Formula.Sockets[inputs.IndexOf(part)].ToString()) is not { } formula
                || Formula.Read(formula, extra.Functions, out _) is null)
            {
                foreach (var wire in patch.Connections.Where(c => c.TargetNode == root.Id).ToList())
                    if (patch.Find(wire.SourceNode) is { } source && Consumer(source) == root)
                        roots.Enqueue(source);

                continue;
            }

            absorbed.UnionWith(taken);
            foreach (var node in left) roots.Enqueue(node);

            if (into is not null)
            {
                into[root.Id] = root.Id;
                foreach (var id in taken) into[id] = root.Id;
            }

            Replace(root, formula, inputs);
        }

        foreach (var id in absorbed) patch.Remove(id);

        return patch;

        // The candidate a node would fold into: its one reader, where that is a
        // candidate in the same box reading it forwards, and the node has no name.
        NodeInstance? Consumer(NodeInstance node)
        {
            if (!Candidate(node) || node.Name is not null || looped.Contains(node.Id)) return null;
            if (patch.Connections.Count(c => c.SourceNode == node.Id) != 1) return null;

            var wire = patch.Connections.Single(c => c.SourceNode == node.Id);

            return !backwards.Contains(wire)
                && patch.Find(wire.TargetNode) is { } reader
                && Candidate(reader)
                && !looped.Contains(reader.Id)
                && ReferenceEquals(patch.GroupOf(reader.Id), patch.GroupOf(node.Id))
                    ? reader
                    : null;
        }

        // What an input is read as when nothing folds into it: what is wired in,
        // the panel knob it follows, or the number on its knob.
        Term Leaf(NodeInstance node, int port) =>
            patch.IncomingTo(node.Id, port) is { } wire
                ? new Signal(wire.SourceNode, wire.SourcePort)
                : ControlMap.Of(node, port) is { } link
                    ? new Knob(node.InputValues[port], link, node.Id, port)
                    : new Literal(node.InputValues[port]);

        // A node's value built from what each of its inputs is read as.
        Term Build(NodeInstance node, Func<int, Term> input)
        {
            if (node.TypeId != NodeCatalog.ExpressionTypeId)
                return Make(node.TypeId, [.. Enumerable.Range(0, modules.Require(node.TypeId).Inputs.Count).Select(input)]);

            return FormulaOf(node)!.Walk(
                value => new Literal(value),
                input,
                (def, arguments) => Make(def.TypeId, arguments));
        }

        // What a node would fold to, the modules that would fold into it, and the
        // readers-to-be that would not fit and stand as roots of their own.
        (Term Term, List<Guid> Taken, List<NodeInstance> Left) Tree(NodeInstance node)
        {
            var count = node.TypeId == NodeCatalog.ExpressionTypeId
                ? expression.Inputs.Count
                : modules.Require(node.TypeId).Inputs.Count;

            var decided = new Term[count];
            var taken = new List<Guid>();
            var left = new List<NodeInstance>();
            var reads = Reads(node, count);

            for (var port = 0; port < count; port++)
            {
                decided[port] = Leaf(node, port);

                if (patch.IncomingTo(node.Id, port) is not { } wire
                    || backwards.Contains(wire)
                    || patch.Find(wire.SourceNode) is not { } source
                    || Consumer(source) != node)
                {
                    continue;
                }

                // A socket read twice would put the branch in the formula twice.
                if (reads[port] > 1)
                {
                    left.Add(source);
                    continue;
                }

                var inner = Tree(source);
                var at = port;

                Term Trial(Term here) => Build(node, p => p < at ? decided[p] : p == at ? here : Leaf(node, p));

                var trial = Trial(inner.Term);
                var needed = new List<Term>();

                Gather(trial, needed);

                if (needed.Count <= Sockets
                    && Written(trial, _ => "a") is { Length: <= Longest }
                    && Repeats(trial) == Repeats(Trial(decided[port])) + Repeats(inner.Term))
                {
                    decided[port] = inner.Term;
                    taken.Add(source.Id);
                    taken.AddRange(inner.Taken);
                    left.AddRange(inner.Left);
                    continue;
                }

                left.Add(source);
            }

            return (Build(node, port => decided[port]), taken, left);
        }

        // How many times a node's formula reads each socket; once each for a module.
        int[] Reads(NodeInstance node, int count)
        {
            var reads = new int[count];

            if (node.TypeId != NodeCatalog.ExpressionTypeId)
            {
                Array.Fill(reads, 1);
                return reads;
            }

            FormulaOf(node)!.Walk(
                _ => 0,
                socket =>
                {
                    if (socket < count) reads[socket]++;
                    return 0;
                },
                (_, _) => 0);

            return reads;
        }

        // Swaps a root for an Expression under the same id and name, so its
        // readers, its box and its place on the canvas are the Expression's.
        void Replace(NodeInstance root, string formula, List<Term> inputs)
        {
            var fused = NodeInstance.Create(expression, root.X, root.Y, root.Id);
            fused.Name = root.Name;
            fused.SetState(FormulaExtra.StateKey, new JsonObject { [FormulaExtra.FormulaField] = formula });

            patch.Nodes[patch.Nodes.IndexOf(root)] = fused;
            patch.Connections.RemoveAll(c => c.TargetNode == root.Id);
            patch.GroupOf(root.Id)?.Exposed.RemoveAll(s => s.Node == root.Id && !s.IsOutput);

            for (var socket = 0; socket < inputs.Count; socket++)
            {
                switch (inputs[socket])
                {
                    case Signal signal:
                        patch.Connect(signal.Node, signal.Port, root.Id, socket);
                        break;

                    case Knob knob:
                        fused.InputValues[socket] = knob.Value;
                        if (knob.Link is { } link) ControlMap.Link(fused, socket, link);
                        break;
                }
            }
        }

        string? Written(Term term, Func<Term, string> socket)
        {
            switch (term)
            {
                case Literal literal:
                    return literal.Value.ToString("R", CultureInfo.InvariantCulture);

                case Signal or Knob:
                    return socket(term);

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

    /// <summary>
    /// A call, with two numbers either side of an operator put on sockets: the
    /// formula's reader would add them up as floats where the program adds them
    /// in its registers.
    /// </summary>
    private static Call Make(string typeId, IReadOnlyList<Term> arguments) =>
        Folding.Contains(typeId) && arguments is [Literal left, Literal right]
            ? new Call(typeId, [new Knob(left.Value, null, Guid.NewGuid(), 0), new Knob(right.Value, null, Guid.NewGuid(), 1)])
            : new Call(typeId, arguments);

    /// <summary>
    /// Every module on a loop. Nothing on one folds into anything, nor anything
    /// into it: which wire of a loop carries the evaluation before is decided by
    /// the shape of the graph, and a chain folded on one would move it.
    /// </summary>
    private static HashSet<Guid> Looped(Patch patch)
    {
        var next = patch.Connections
            .GroupBy(c => c.SourceNode)
            .ToDictionary(g => g.Key, g => g.Select(c => c.TargetNode).Distinct().ToList());

        var index = new Dictionary<Guid, int>();
        var low = new Dictionary<Guid, int>();
        var stack = new Stack<Guid>();
        var onStack = new HashSet<Guid>();
        var looped = new HashSet<Guid>();
        var counter = 0;

        foreach (var node in patch.Nodes)
            if (!index.ContainsKey(node.Id)) Visit(node.Id);

        return looped;

        // Tarjan's strongly connected components, iterating rather than
        // recursing, since a big preset's chains run deep.
        void Visit(Guid start)
        {
            var work = new Stack<(Guid Node, int Child)>();
            work.Push((start, 0));
            index[start] = low[start] = counter++;
            stack.Push(start);
            onStack.Add(start);

            while (work.Count > 0)
            {
                var (node, child) = work.Pop();
                var targets = next.GetValueOrDefault(node) ?? [];

                if (child < targets.Count)
                {
                    work.Push((node, child + 1));
                    var target = targets[child];

                    if (!index.ContainsKey(target))
                    {
                        index[target] = low[target] = counter++;
                        stack.Push(target);
                        onStack.Add(target);
                        work.Push((target, 0));
                    }
                    else if (onStack.Contains(target))
                    {
                        low[node] = Math.Min(low[node], index[target]);
                    }

                    continue;
                }

                if (work.Count > 0) low[work.Peek().Node] = Math.Min(low[work.Peek().Node], low[node]);

                if (low[node] != index[node]) continue;

                var component = new List<Guid>();
                Guid member;

                do
                {
                    member = stack.Pop();
                    onStack.Remove(member);
                    component.Add(member);
                }
                while (member != node);

                if (component.Count > 1 || targets.Contains(node)) looped.UnionWith(component);
            }
        }
    }

    /// <summary>The sockets a term reads, each once, in the order it first reads them.</summary>
    private static void Gather(Term term, List<Term> inputs)
    {
        switch (term)
        {
            case Signal or Knob when !inputs.Contains(term):
                inputs.Add(term);
                break;

            case Call call:
                foreach (var argument in call.Arguments) Gather(argument, inputs);
                break;
        }
    }

    /// <summary>
    /// How many of a term's calls repeat one before them: a part written twice,
    /// which the formula lowers once.
    /// </summary>
    private static int Repeats(Term term)
    {
        var seen = new HashSet<string>();
        var repeats = 0;

        Key(term);
        return repeats;

        string Key(Term part)
        {
            var key = part switch
            {
                Literal literal => literal.Value.ToString("R", CultureInfo.InvariantCulture),
                Signal signal => $"{signal.Node}:{signal.Port}",
                Knob knob => $"k{knob.Node}:{knob.Port}",
                Call call => $"{call.TypeId}({string.Join(",", call.Arguments.Select(Key))})",
                _ => "?",
            };

            if (part is Call && !seen.Add(key)) repeats++;

            return key;
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

    /// <summary>A number that stays on a socket, and the panel knob it follows if any.</summary>
    private sealed record Knob(float Value, ControlLink? Link, Guid Node, int Port) : Term;

    private sealed record Call(string TypeId, IReadOnlyList<Term> Arguments) : Term;
}
