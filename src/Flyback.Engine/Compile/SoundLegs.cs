using Flyback.Core.Compile;

namespace Flyback.Engine.Compile;

/// <summary>
/// Where a sound's program is cut into legs: runs of ops that cores take in turn over a
/// stretch of samples, each handing on to the next (ADR-0230).
/// </summary>
/// <remarks>
/// A cut may fall only where nothing flows back across it from one evaluation to the
/// next: a cell read before its write, or a register read before the op that writes it
/// this evaluation. Everything else flows forward, so a later leg can run behind an
/// earlier one and compute what one core would, to the bit. The ops are put in an order
/// that keeps each such knot together and everything else as the program had it, so the
/// knots are as small as they can be and a cut can fall between them.
/// </remarks>
internal sealed class SoundLegs
{
    /// <summary>A program cheaper than this runs whole: a leg would cost more in handing on than it saves.</summary>
    internal const double Least = 1500d;

    private SoundLegs(int[] order, int[] cuts, int[][] handed, int[][] settled, int[] constants, int[][] written, LegCells[] cells)
    {
        Order = order;
        Cuts = cuts;
        Handed = handed;
        Settled = settled;
        Constants = constants;
        Written = written;
        Cells = cells;
    }

    /// <summary>The order the ops run in, as their places in the program.</summary>
    public int[] Order { get; }

    /// <summary>The first place in <see cref="Order"/> of each leg, and the program's length last.</summary>
    public int[] Cuts { get; }

    /// <summary>
    /// For each cut after the first, the registers written before it that an op after
    /// it reads, or that hold the program's output: what one leg hands the next for
    /// every evaluation. A register only a constant writes is left out, as each leg's
    /// own bank holds it already.
    /// </summary>
    public int[][] Handed { get; }

    /// <summary>
    /// For each cut after the first, what crosses it that follows only from constants and
    /// what is played in, and so is the same for a whole buffer: handed on once a buffer.
    /// </summary>
    public int[][] Settled { get; }

    /// <summary>The registers only a constant writes, which every leg's bank is given before it runs.</summary>
    public int[] Constants { get; }

    /// <summary>The registers each leg writes, for the bank one core would have been left with.</summary>
    public int[][] Written { get; }

    /// <summary>The memory cells each leg owns, which no other leg touches.</summary>
    public LegCells[] Cells { get; }

    public int Count => Cuts.Length - 1;

    /// <summary>The legs for <paramref name="program"/>, or null when it runs best whole.</summary>
    /// <param name="most">The most legs to cut it into.</param>
    /// <param name="least">What the program must cost before it is cut at all, against an add.</param>
    public static SoundLegs? For(Op[] program, (int From, int To) outputs, Repeats inProgram, int most, double least)
    {
        if (most < 2 || program.Length == 0) return null;

        var order = Ordered(program, inProgram);
        Op[] ops = [.. order.Select(i => program[i])];
        var repeats = inProgram.Reordered(order);

        var cost = new double[ops.Length + 1];
        for (var i = 0; i < ops.Length; i++)
            cost[i + 1] = cost[i] + (repeats.Repeated(i) ? 0d : Cost(ops[i].Code));

        var total = cost[^1];
        if (total < least) return null;

        var allowed = Allowed(ops, repeats);
        var constants = ConstantRegisters(ops, repeats);
        var settled = SettledRegisters(ops, repeats, constants);
        var crossing = Crossing(ops, outputs, repeats, [.. constants, .. settled]);

        int[]? chosen = null;
        var fastest = total;

        for (var legs = 2; legs <= most; legs++)
        {
            var cuts = Balanced(cost, allowed, crossing, legs);
            if (cuts is null) continue;

            var slowest = Slowest(cost, crossing, cuts);

            // Another leg is another core kept busy, so it has to earn its place.
            if (slowest < fastest * 0.9d)
            {
                chosen = cuts;
                fastest = slowest;
            }
        }

        // A cut that leaves one leg with most of the work saves too little to pay for itself.
        if (chosen is null || fastest > total * 0.75d) return null;

        return new SoundLegs(
            order,
            chosen,
            [.. chosen[1..^1].Select(cut => HandedAt(ops, outputs, repeats, cut).Except(constants).Except(settled).Order().ToArray())],
            [.. chosen[1..^1].Select(cut => HandedAt(ops, outputs, repeats, cut).Intersect(settled).Order().ToArray())],
            [.. constants.Order()],
            [.. Enumerable.Range(0, chosen.Length - 1).Select(l => WrittenIn(ops, repeats, chosen[l], chosen[l + 1]))],
            CellsOf(program, order, chosen));
    }

    /// <summary>Whether a leg may begin at each op, and at the end.</summary>
    private static bool[] Allowed(Op[] ops, Repeats repeats)
    {
        var n = ops.Length;
        var together = new int[n];
        for (var i = 0; i < n; i++) together[i] = i;

        var lastWriter = new Dictionary<int, int>();
        for (var i = 0; i < n; i++)
            if (!repeats.Repeated(i))
                for (var w = 0; w < OpShape.Outputs(ops[i].Code); w++)
                    lastWriter[ops[i].Out + w] = i;

        var written = new HashSet<int>();

        // A cell's reads and writes sit in one leg, as what one evaluation leaves there the next one reads.
        var cells = new Dictionary<(bool Plane, int Slot), (int First, int Last)>();

        for (var i = 0; i < n; i++)
        {
            if (repeats.Repeated(i)) continue;

            var op = ops[i];

            // Read before this evaluation writes it: the last evaluation's value.
            foreach (var read in OpShape.Reads(op))
            {
                var register = repeats.Resolve(read);
                if (!written.Contains(register) && lastWriter.TryGetValue(register, out var writer) && writer >= i)
                    together[i] = Math.Max(together[i], writer);
            }

            for (var w = 0; w < OpShape.Outputs(op.Code); w++) written.Add(op.Out + w);

            (bool, int)? cell = op.Code switch
            {
                OpCode.UnitRead or OpCode.UnitWrite or OpCode.ClockWrite => (false, (int)op.K),
                OpCode.PlaneRead or OpCode.PlaneWrite => (true, (int)op.K),
                _ => null,
            };

            if (cell is { } key)
                cells[key] = cells.TryGetValue(key, out var span) ? (span.First, i) : (i, i);
        }

        foreach (var (first, last) in cells.Values) together[first] = Math.Max(together[first], last);

        var allowed = new bool[n + 1];
        allowed[0] = allowed[n] = true;

        var reach = -1;
        for (var i = 0; i < n; i++)
        {
            reach = Math.Max(reach, together[i]);
            allowed[i + 1] = reach <= i;
        }

        return allowed;
    }

    /// <summary>
    /// Cuts for <paramref name="legs"/> legs that leave the slowest as light as they can,
    /// counting what each hands on, or null where there are too few places to cut.
    /// </summary>
    /// <remarks>
    /// Starts from the places nearest an even split, then moves each cut within a window
    /// round it while that lightens the heavier of its two legs: a cut a little off the
    /// middle that hands on half as much is the faster one.
    /// </remarks>
    private static int[]? Balanced(double[] cost, bool[] allowed, int[] crossing, int legs)
    {
        var n = cost.Length - 1;
        var total = cost[n];
        var cuts = new int[legs + 1];
        cuts[legs] = n;

        for (var l = 1; l < legs; l++)
        {
            var target = total * l / legs;
            var best = -1;
            var nearest = double.MaxValue;

            for (var at = cuts[l - 1] + 1; at < n; at++)
            {
                if (!allowed[at]) continue;

                var distance = Math.Abs(cost[at] - target);
                if (distance < nearest)
                {
                    nearest = distance;
                    best = at;
                }
            }

            if (best < 0) return null;
            cuts[l] = best;
        }

        var window = total / legs / 4d;

        for (var pass = 0; pass < 3; pass++)
        {
            for (var l = 1; l < legs; l++)
            {
                var best = cuts[l];
                var lightest = Math.Max(Leg(cost, crossing, cuts[l - 1], best), Leg(cost, crossing, best, cuts[l + 1]));

                for (var at = cuts[l - 1] + 1; at < cuts[l + 1]; at++)
                {
                    if (!allowed[at] || Math.Abs(cost[at] - cost[cuts[l]]) > window) continue;

                    var heavier = Math.Max(Leg(cost, crossing, cuts[l - 1], at), Leg(cost, crossing, at, cuts[l + 1]));
                    if (heavier < lightest)
                    {
                        lightest = heavier;
                        best = at;
                    }
                }

                cuts[l] = best;
            }
        }

        return cuts;
    }

    private static double Slowest(double[] cost, int[] crossing, int[] cuts)
    {
        var slowest = 0d;
        for (var l = 0; l + 1 < cuts.Length; l++) slowest = Math.Max(slowest, Leg(cost, crossing, cuts[l], cuts[l + 1]));
        return slowest;
    }

    /// <summary>What a leg from <paramref name="from"/> to <paramref name="to"/> costs, with what it takes in and hands on.</summary>
    private static double Leg(double[] cost, int[] crossing, int from, int to) =>
        cost[to] - cost[from] + Handing * (crossing[from] + crossing[to]);

    /// <summary>What handing one register on costs each side of a cut, against an add: the other core's cache is a long way off.</summary>
    private const double Handing = 2.5d;

    /// <summary>How many registers each cut would hand on, for every place in the program at once.</summary>
    private static int[] Crossing(Op[] ops, (int From, int To) outputs, Repeats repeats, HashSet<int> constant)
    {
        var n = ops.Length;
        var firstWrite = new Dictionary<int, int>();
        var lastRead = new Dictionary<int, int>();

        for (var i = 0; i < n; i++)
        {
            if (repeats.Repeated(i)) continue;

            foreach (var read in OpShape.Reads(ops[i])) lastRead[repeats.Resolve(read)] = i;
            for (var w = 0; w < OpShape.Outputs(ops[i].Code); w++) firstWrite.TryAdd(ops[i].Out + w, i);
        }

        for (var register = outputs.From; register < outputs.To; register++) lastRead[register] = n;

        // Handed at cut c when first written before c and read at c or after: c in (write, read].
        var change = new int[n + 2];
        foreach (var (register, write) in firstWrite)
        {
            if (constant.Contains(register) || !lastRead.TryGetValue(register, out var read) || read <= write) continue;

            change[write + 1]++;
            change[read + 1]--;
        }

        var crossing = new int[n + 1];
        var running = 0;
        for (var c = 0; c <= n; c++)
        {
            running += change[c];
            crossing[c] = running;
        }

        crossing[0] = crossing[n] = 0;
        return crossing;
    }

    private static HashSet<int> ConstantRegisters(Op[] ops, Repeats repeats)
    {
        var writers = new HashSet<int>();
        var constant = new HashSet<int>();

        for (var i = 0; i < ops.Length; i++)
        {
            if (repeats.Repeated(i)) continue;

            for (var w = 0; w < OpShape.Outputs(ops[i].Code); w++)
            {
                var register = ops[i].Out + w;
                if (writers.Add(register) && ops[i].Code is OpCode.Const) constant.Add(register);
                else constant.Remove(register);
            }
        }

        return constant;
    }

    private static LegCells[] CellsOf(Op[] program, int[] order, int[] cuts)
    {
        var numbering = new IlEmitter.Numbering(program);
        var cells = new LegCells[cuts.Length - 1];

        for (var l = 0; l < cells.Length; l++)
        {
            var (lines, phases) = (new SortedSet<int>(), new SortedSet<int>());
            var (units, planes, traces) = (new SortedSet<int>(), new SortedSet<int>(), new SortedSet<int>());

            for (var at = cuts[l]; at < cuts[l + 1]; at++)
            {
                var i = order[at];
                var op = program[i];

                switch (OpShape.Owns(op.Code))
                {
                    case OpMemory.Line: lines.Add(numbering.Lines[i]); break;
                    case OpMemory.Cell: phases.Add(numbering.Cells[i]); break;
                }

                switch (op.Code)
                {
                    case OpCode.UnitRead or OpCode.UnitWrite or OpCode.ClockWrite: units.Add((int)op.K); break;
                    case OpCode.PlaneRead or OpCode.PlaneWrite: planes.Add((int)op.K); break;
                    case OpCode.Tap: traces.Add((int)op.K); break;
                }
            }

            cells[l] = new LegCells([.. lines], [.. phases], [.. units], [.. planes], [.. traces]);
        }

        return cells;
    }

    /// <summary>
    /// An order for the ops that computes what the program does, with every knot of ops
    /// that must share a leg in one run, and otherwise as near the program's own as it can.
    /// </summary>
    /// <remarks>
    /// Each op must follow what it reads, the last write of what it writes, and the reads
    /// of what it overwrites. A register read before this evaluation writes it, and a
    /// cell, tie their ops into a knot: a strongly connected component, which keeps its
    /// ops in the program's order. The knots are then laid out in the order the
    /// program first reaches each.
    /// </remarks>
    private static int[] Ordered(Op[] ops, Repeats repeats)
    {
        var n = ops.Length;
        var after = new List<int>[n];
        for (var i = 0; i < n; i++) after[i] = [];

        void Edge(int from, int to)
        {
            if (from != to) after[from].Add(to);
        }

        var firstWriter = new Dictionary<int, int>();
        var lastWriter = new Dictionary<int, int>();
        for (var i = 0; i < n; i++)
            if (!repeats.Repeated(i))
                for (var w = 0; w < OpShape.Outputs(ops[i].Code); w++)
                {
                    firstWriter.TryAdd(ops[i].Out + w, i);
                    lastWriter[ops[i].Out + w] = i;
                }

        var writer = new Dictionary<int, int>();
        var readers = new Dictionary<int, List<int>>();
        var cells = new Dictionary<(bool Plane, int Slot), int>();

        for (var i = 0; i < n; i++)
        {
            if (repeats.Repeated(i)) continue;

            var op = ops[i];

            foreach (var read in OpShape.Reads(op))
            {
                var register = repeats.Resolve(read);

                if (writer.TryGetValue(register, out var source))
                {
                    Edge(source, i);
                }
                else if (lastWriter.TryGetValue(register, out var last))
                {
                    // The last evaluation's value: read before the first write, and with the last.
                    Edge(last, i);
                    Edge(i, firstWriter[register]);
                }

                if (!readers.TryGetValue(register, out var list)) readers[register] = list = [];
                list.Add(i);
            }

            for (var w = 0; w < OpShape.Outputs(op.Code); w++)
            {
                var register = op.Out + w;
                if (writer.TryGetValue(register, out var previous)) Edge(previous, i);

                if (readers.TryGetValue(register, out var list))
                {
                    foreach (var reader in list) Edge(reader, i);
                    list.Clear();
                }

                writer[register] = i;
            }

            (bool, int)? cell = op.Code switch
            {
                OpCode.UnitRead or OpCode.UnitWrite or OpCode.ClockWrite => (false, (int)op.K),
                OpCode.PlaneRead or OpCode.PlaneWrite => (true, (int)op.K),
                _ => null,
            };

            if (cell is { } key)
            {
                if (cells.TryGetValue(key, out var earlier))
                {
                    Edge(earlier, i);
                    Edge(i, earlier);
                }

                cells[key] = i;
            }
        }

        var knot = Knots(after, out var knots);

        // The knots in an order their edges allow, the one the program reaches first first.
        var first = new int[knots];
        Array.Fill(first, int.MaxValue);
        var members = new List<int>[knots];
        for (var k = 0; k < knots; k++) members[k] = [];

        for (var i = 0; i < n; i++)
        {
            first[knot[i]] = Math.Min(first[knot[i]], i);
            members[knot[i]].Add(i);
        }

        var waiting = new int[knots];
        var next = new HashSet<int>[knots];
        for (var k = 0; k < knots; k++) next[k] = [];

        for (var i = 0; i < n; i++)
            foreach (var j in after[i])
                if (knot[i] != knot[j] && next[knot[i]].Add(knot[j]))
                    waiting[knot[j]]++;

        var ready = new PriorityQueue<int, int>();
        for (var k = 0; k < knots; k++)
            if (waiting[k] == 0) ready.Enqueue(k, first[k]);

        var order = new List<int>(n);

        while (ready.TryDequeue(out var k, out _))
        {
            order.AddRange(members[k]);

            foreach (var j in next[k])
                if (--waiting[j] == 0) ready.Enqueue(j, first[j]);
        }

        return [.. order];
    }

    /// <summary>The strongly connected component of each op, numbered from nought, without recursion.</summary>
    private static int[] Knots(List<int>[] after, out int count)
    {
        var n = after.Length;
        var index = new int[n];
        var low = new int[n];
        var knot = new int[n];
        var onStack = new bool[n];
        Array.Fill(index, -1);

        var stack = new Stack<int>();
        var walk = new Stack<(int Node, int Edge)>();
        var counter = 0;
        count = 0;

        for (var root = 0; root < n; root++)
        {
            if (index[root] >= 0) continue;

            walk.Push((root, 0));

            while (walk.TryPop(out var frame))
            {
                var (node, edge) = frame;

                if (edge == 0)
                {
                    index[node] = low[node] = counter++;
                    stack.Push(node);
                    onStack[node] = true;
                }

                if (edge < after[node].Count)
                {
                    walk.Push((node, edge + 1));
                    var to = after[node][edge];

                    if (index[to] < 0) walk.Push((to, 0));
                    else if (onStack[to]) low[node] = Math.Min(low[node], index[to]);

                    continue;
                }

                if (low[node] == index[node])
                {
                    int member;
                    do
                    {
                        member = stack.Pop();
                        onStack[member] = false;
                        knot[member] = count;
                    }
                    while (member != node);

                    count++;
                }

                if (walk.TryPeek(out var parent)) low[parent.Node] = Math.Min(low[parent.Node], low[node]);
            }
        }

        return knot;
    }

    private static int[] WrittenIn(Op[] ops, Repeats repeats, int from, int to)
    {
        var written = new SortedSet<int>();

        for (var i = from; i < to; i++)
            if (!repeats.Repeated(i))
                for (var w = 0; w < OpShape.Outputs(ops[i].Code); w++)
                    written.Add(ops[i].Out + w);

        return [.. written];
    }

    /// <summary>
    /// Registers that follow from nothing but constants and the values played in, each
    /// written once: they hold one value for a whole buffer.
    /// </summary>
    private static HashSet<int> SettledRegisters(Op[] ops, Repeats repeats, HashSet<int> constants)
    {
        var writes = new Dictionary<int, int>();
        foreach (var op in ops)
            for (var w = 0; w < OpShape.Outputs(op.Code); w++)
                writes[op.Out + w] = writes.GetValueOrDefault(op.Out + w) + 1;

        var settled = new HashSet<int>();
        var written = new HashSet<int>();

        for (var i = 0; i < ops.Length; i++)
        {
            var op = ops[i];
            if (repeats.Repeated(i)) continue;

            var steady = OpShape.Outputs(op.Code) == 1
                && writes[op.Out] == 1
                && op.Code switch
                {
                    OpCode.Const or OpCode.LoadLive or OpCode.LoadAspect => true,
                    OpCode.LoadT or OpCode.LoadX or OpCode.LoadY or OpCode.LoadFeedbackAge
                        or OpCode.UnitRead or OpCode.PlaneRead => false,
                    _ => !OpShape.Kept(op.Code)
                        && OpShape.Reads(op).All(read => written.Contains(repeats.Resolve(read)) && settled.Contains(repeats.Resolve(read))),
                };

            for (var w = 0; w < OpShape.Outputs(op.Code); w++) written.Add(op.Out + w);
            if (steady) settled.Add(op.Out);
        }

        settled.ExceptWith(constants);
        return settled;
    }

    private static SortedSet<int> HandedAt(Op[] ops, (int From, int To) outputs, Repeats repeats, int cut)
    {
        var before = new HashSet<int>();
        for (var i = 0; i < cut; i++)
            if (!repeats.Repeated(i))
                for (var w = 0; w < OpShape.Outputs(ops[i].Code); w++)
                    before.Add(ops[i].Out + w);

        var handed = new SortedSet<int>();

        for (var i = cut; i < ops.Length; i++)
            if (!repeats.Repeated(i))
                foreach (var read in OpShape.Reads(ops[i]))
                    handed.Add(repeats.Resolve(read));

        for (var register = outputs.From; register < outputs.To; register++) handed.Add(register);

        handed.IntersectWith(before);
        return handed;
    }

    /// <summary>Roughly what an op costs against an add, which is all a cut needs to land near the middle.</summary>
    private static double Cost(OpCode code) => code switch
    {
        OpCode.Const or OpCode.Copy => 0.5d,
        OpCode.Mix or OpCode.Step or OpCode.Clamp or OpCode.Floor or OpCode.Sign => 1.5d,
        OpCode.UnitRead or OpCode.UnitWrite or OpCode.ClockWrite or OpCode.Fract => 3d,
        OpCode.Sqrt or OpCode.Smoothstep or OpCode.Hypot => 6d,
        OpCode.Div or OpCode.Exp => 8d,
        OpCode.Phase or OpCode.Pow => 12d,
        OpCode.Mod => 15d,
        OpCode.Log => 20d,
        OpCode.Noise3 => 25d,
        OpCode.Sin or OpCode.Cos => 30d,
        OpCode.Delay or OpCode.Allpass => 35d,
        OpCode.Tan or OpCode.Atan2 => 45d,
        _ => 1d,
    };
}
