namespace Flyback.Core.Graph;

/// <summary>
/// Places every node of a patch so the signal reads left to right and the wires
/// between them can be seen.
/// </summary>
/// <remarks>
/// A layered drawing in the usual four stages: cut the edges that run backwards,
/// put every node in a column by how far along the chain it is, order each
/// column so the fewest wires cross, then place the nodes down the column so a
/// wire meets its two sockets as level as it can. See ADR-0044.
/// <para>
/// What those stages place is a block rather than a module, and a group is one
/// block whatever is inside it: placing its modules one at a time would spread
/// them down whichever columns their own wires ask for, and the box drawn round
/// them would reach across half the patch. A group is laid out among itself
/// first, so it has a tidy inside and a size, and is moved whole thereafter.
/// </para>
/// <para>
/// Here rather than in the editor because two callers want it and only one has a
/// canvas: the toolbar button, and the assistant's workbench. What the editor
/// supplies is <see cref="Metrics"/>.
/// </para>
/// </remarks>
public static class PatchLayout
{
    /// <summary>
    /// How big a node is and how much room to leave around it. Every distance
    /// the layout uses, so that the one caller with a canvas can hand over the
    /// canvas's own numbers and the one without can take these.
    /// </summary>
    /// <param name="Width">How wide a node is drawn.</param>
    /// <param name="HeaderHeight">The title bar above the first socket row.</param>
    /// <param name="RowHeight">One socket row.</param>
    /// <param name="FooterPadding">What is left below the last socket row.</param>
    /// <param name="ColumnGap">Clear space between one column of nodes and the next, for the wires to cross.</param>
    /// <param name="RowGap">The least clear space between two nodes in a column.</param>
    /// <param name="GroupPadding">The ring drawn round a group that is open, on all four sides.</param>
    /// <param name="GroupHandleHeight">The strip above that ring, which the group's name is written on.</param>
    public readonly record struct Metrics(
        double Width,
        double HeaderHeight,
        double RowHeight,
        double FooterPadding,
        double ColumnGap,
        double RowGap,
        double GroupPadding,
        double GroupHandleHeight)
    {
        /// <summary>
        /// The editor's own numbers, for a caller that has no editor. Pinned to
        /// the real ones by a test rather than by a reference, because the shape
        /// of a node is the view's to decide and the engine should not be asking.
        /// </summary>
        public static Metrics Default => new(196d, 26d, 20d, 8d, 108d, 40d, 24d, 20d);

        public double Height(NodeDef def) =>
            HeaderHeight + (def.Inputs.Count + def.Outputs.Count) * RowHeight + FooterPadding;

        /// <summary>How far below a node's top edge one of its output sockets sits.</summary>
        public double OutputPort(int index) => HeaderHeight + (index + 0.5d) * RowHeight;

        /// <summary>The same for an input, which is below every output — the Blender order.</summary>
        public double InputPort(NodeDef def, int index) =>
            HeaderHeight + (def.Outputs.Count + index + 0.5d) * RowHeight;

        /// <summary>
        /// How tall the box of a collapsed group is: a header, a row for every
        /// socket on its edge, and a floor to stand on whatever crosses it.
        /// </summary>
        public double GroupHeight(GroupSockets sockets) =>
            HeaderHeight + Math.Max(sockets.Rows, 1) * RowHeight + FooterPadding;

        /// <summary>How far below that box's top edge one of its socket rows sits.</summary>
        public double GroupPort(GroupSockets sockets, int row, bool isOutput) =>
            HeaderHeight + ((isOutput ? row : sockets.Outputs.Count + row) + 0.5d) * RowHeight;
    }

    /// <summary>How many times the ordering is swept up and back down the columns.</summary>
    private const int Sweeps = 4;

    /// <summary>
    /// One thing the layout moves, and the only thing it knows how to move: a
    /// module in no group, or a group with everything in it at once. Rigid — the
    /// stages below decide where a block goes and never what is inside one.
    /// </summary>
    private sealed class Block
    {
        /// <summary>The modules moved together, and where each sits inside.</summary>
        public required (NodeInstance Node, double X, double Y)[] Holds { get; init; }

        public required double Width { get; init; }

        public required double Height { get; init; }

        /// <summary>
        /// How far below the block's top edge a wire meets one of its ports,
        /// given the module, the port and which side it is.
        /// </summary>
        /// <remarks>
        /// A question the block answers rather than a rule the placement applies,
        /// which is how the placement avoids learning what a group is. A collapsed
        /// group answers differently: its wires meet rows on the box, and the
        /// modules those rows stand for are behind it.
        /// </remarks>
        public required Func<Guid, int, bool, double> Meets { get; init; }

        /// <summary>Where its top edge was, which is where the ordering starts from.</summary>
        public required double Was { get; init; }

        /// <summary>Its place in the patch, to break a tie in that ordering.</summary>
        public required int Index { get; init; }

        public double X { get; set; }

        public double Y { get; set; }

        /// <summary>Writes the coordinates on, which is the only edit any of this makes.</summary>
        public void Put()
        {
            foreach (var (node, x, y) in Holds)
            {
                node.X = X + x;
                node.Y = Y + y;
            }
        }
    }

    /// <summary>
    /// A wire between two blocks, with both of the sockets it meets already
    /// measured against the blocks they are on.
    /// </summary>
    private readonly record struct Link(int From, int To, double Leaves, double Arrives);

    /// <summary>
    /// Moves every node of <paramref name="patch"/>. No wire is added, removed or
    /// rerouted, so this is always safe to run and exactly undoable by putting the
    /// old coordinates back.
    /// </summary>
    /// <param name="patch">The patch to place. Modified in place.</param>
    /// <param name="modules">Which catalogue the type ids mean, defaulting to the installed one.</param>
    /// <param name="metrics">How big the nodes are, defaulting to the editor's own.</param>
    /// <returns>
    /// Whether the drawing fits the canvas. What came out is correct either way,
    /// but <see cref="NodeInstance.X"/> holds every coordinate inside the canvas,
    /// so a drawing that does not fit arrives with its far edges folded onto the
    /// boundary and stacked.
    /// </returns>
    public static bool Arrange(Patch patch, ModuleCatalog? modules = null, Metrics? metrics = null)
    {
        var catalog = modules ?? NodeCatalog.Current;
        var size = metrics ?? Metrics.Default;

        // A node whose module is not installed is left where it is: it cannot be
        // measured, and moving it to a guessed height would scatter the one
        // thing a patch from a missing plugin still has going for it.
        var defs = new Dictionary<Guid, NodeDef>();
        foreach (var node in patch.Nodes)
            if (catalog.Get(node.TypeId) is { } def)
                defs[node.Id] = def;

        // And a group holding one of those is left alone whole rather than moved
        // in part. A box is drawn from where all of its modules are, so placing
        // some of them and not the rest slides the box off the ones that stayed.
        var groups = new List<NodeGroup>();

        foreach (var group in patch.Groups ?? [])
        {
            if (group.Members.Count == 0) continue;

            if (group.Members.All(defs.ContainsKey)) groups.Add(group);
            else foreach (var id in group.Members) defs.Remove(id);
        }

        if (defs.Count == 0) return true;

        var nodes = patch.Nodes.Where(n => defs.ContainsKey(n.Id)).ToDictionary(n => n.Id);

        // Where each group was, taken before anything moves: the ordering starts
        // from where things already are, and laying a group out among itself is
        // the first thing that changes that.
        var was = groups.ToDictionary(g => g.Id, g => g.Members.Min(id => nodes[id].Y));

        foreach (var group in groups) Inside(patch, nodes, defs, group, size);

        var blocks = new List<Block>();
        var of = new Dictionary<Guid, int>();
        var kept = groups.ToHashSet();

        for (var i = 0; i < patch.Nodes.Count; i++)
        {
            var node = patch.Nodes[i];

            if (!defs.ContainsKey(node.Id) || of.ContainsKey(node.Id)) continue;

            if (patch.GroupOf(node.Id) is { } group && kept.Contains(group))
            {
                foreach (var id in group.Members) of[id] = blocks.Count;
                blocks.Add(Boxed(patch, nodes, defs, group, size, was[group.Id], i));
            }
            else
            {
                of[node.Id] = blocks.Count;
                blocks.Add(Lone(node, defs[node.Id], size, i));
            }
        }

        var sink = patch.FirstOf(NodeCatalog.OutputTypeId) is { } output
            ? of.GetValueOrDefault(output.Id, -1)
            : -1;

        Lay(blocks, Links(patch, defs, blocks, of), sink, size);

        return Settle(blocks);
    }

    /// <summary>
    /// Puts the finished drawing in the middle of the canvas, and says whether it
    /// fits on one. The placement runs from a corner because a column is easier
    /// to reason about running one way; the middle is where that corner goes,
    /// since it is the only choice that uses the whole canvas.
    /// </summary>
    private static bool Settle(List<Block> blocks)
    {
        var left = blocks.Min(block => block.X);
        var top = blocks.Min(block => block.Y);
        var right = blocks.Max(block => block.X + block.Width);
        var bottom = blocks.Max(block => block.Y + block.Height);

        var across = (left + right) / 2;
        var down = (top + bottom) / 2;

        foreach (var block in blocks)
        {
            block.X -= across;
            block.Y -= down;
            block.Put();
        }

        return right - left <= NodeInstance.Across * 2 && bottom - top <= NodeInstance.Down * 2;
    }

    /// <summary>
    /// Lays a group out among itself, using only the wires that stay inside it.
    /// Run first, because the block that stands for a group is measured from
    /// where its modules end up — and a preset declares no coordinates at all
    /// (ADR-0070), so a group that skipped this would arrive as a pile at the
    /// origin.
    /// </summary>
    private static void Inside(
        Patch patch,
        Dictionary<Guid, NodeInstance> nodes,
        Dictionary<Guid, NodeDef> defs,
        NodeGroup group,
        Metrics size)
    {
        var blocks = new List<Block>();
        var of = new Dictionary<Guid, int>();

        for (var i = 0; i < group.Members.Count; i++)
        {
            of[group.Members[i]] = i;
            blocks.Add(Lone(nodes[group.Members[i]], defs[group.Members[i]], size, i));
        }

        // No sink to pin: Patch.Group leaves the Output out of every group.
        Lay(blocks, Links(patch, defs, blocks, of), sink: -1, size);

        foreach (var block in blocks) block.Put();
    }

    /// <summary>
    /// The wires between blocks, with the ones that run backwards dropped.
    /// </summary>
    /// <remarks>
    /// A patch may hold a cycle only through a cycle breaker
    /// (<see cref="NodeDef.IsCycleBreaker"/>), so unlike every other layered
    /// drawing this one is told which edges to reverse rather than guessing. It
    /// cuts them where the meaning already is: what leaves a Unit Delay is the
    /// previous evaluation. Asked of the module rather than the block, so a
    /// breaker inside a group also cuts what leaves the group. A wire with both
    /// ends on one block is dropped too — it was accounted for when the group was
    /// laid out among itself.
    /// </remarks>
    private static List<Link> Links(
        Patch patch,
        Dictionary<Guid, NodeDef> defs,
        List<Block> blocks,
        Dictionary<Guid, int> of)
    {
        var links = new List<Link>();

        foreach (var wire in patch.Connections)
        {
            if (!of.TryGetValue(wire.SourceNode, out var from)) continue;
            if (!of.TryGetValue(wire.TargetNode, out var to)) continue;
            if (from == to) continue;
            if (defs[wire.SourceNode].IsCycleBreaker) continue;

            links.Add(new Link(
                from,
                to,
                blocks[from].Meets(wire.SourceNode, wire.SourcePort, true),
                blocks[to].Meets(wire.TargetNode, wire.TargetPort, false)));
        }

        return links;
    }

    /// <summary>A module in no group, where the block is the module.</summary>
    private static Block Lone(NodeInstance node, NodeDef def, Metrics size, int index) => new()
    {
        Holds = [(node, 0d, 0d)],
        Width = size.Width,
        Height = size.Height(def),
        Meets = (_, port, isOutput) => isOutput
            ? size.OutputPort(Held(port, def.Outputs.Count))
            : size.InputPort(def, Held(port, def.Inputs.Count)),
        Was = node.Y,
        Index = index,
    };

    /// <summary>
    /// A group, sized as it is drawn rather than as it is made of.
    /// </summary>
    /// <remarks>
    /// Open, it is a ring round its modules with a strip above, and its wires meet
    /// the modules themselves. Shut, it is one box, its wires meet rows on the
    /// box, and the modules behind it are put at the box's corner and left to
    /// overhang: nothing paints, points at or frames them, so reserving canvas for
    /// them would be reserving it for a picture nobody is looking at. What that
    /// costs is that opening one is a good moment to press the button again.
    /// </remarks>
    private static Block Boxed(
        Patch patch,
        Dictionary<Guid, NodeInstance> nodes,
        Dictionary<Guid, NodeDef> defs,
        NodeGroup group,
        Metrics size,
        double was,
        int index)
    {
        var left = group.Members.Min(id => nodes[id].X);
        var top = group.Members.Min(id => nodes[id].Y);

        if (group.Collapsed)
        {
            var sockets = patch.SocketsOf(group);

            return new Block
            {
                Holds = [.. group.Members.Select(id => (nodes[id], nodes[id].X - left, nodes[id].Y - top))],
                Width = size.Width,
                Height = size.GroupHeight(sockets),
                Meets = (node, port, isOutput) =>
                {
                    var socket = new GroupSocket(node, port, isOutput);
                    var row = isOutput ? sockets.IndexOfOutput(socket) : sockets.IndexOfInput(socket);

                    // Every crossing wire has a row by construction, since that
                    // is what SocketsOf reads the wires for. A hand-edited file
                    // that manages otherwise gets the top row rather than an
                    // answer off the box altogether.
                    return size.GroupPort(sockets, Math.Max(row, 0), isOutput);
                },
                Was = was,
                Index = index,
            };
        }

        var right = group.Members.Max(id => nodes[id].X + size.Width);
        var bottom = group.Members.Max(id => nodes[id].Y + size.Height(defs[id]));

        var inset = size.GroupPadding;
        var below = size.GroupPadding + size.GroupHandleHeight;

        return new Block
        {
            Holds =
            [
                .. group.Members.Select(id =>
                    (nodes[id], nodes[id].X - left + inset, nodes[id].Y - top + below)),
            ],
            Width = right - left + 2 * inset,
            Height = bottom - top + inset + below,
            Meets = (node, port, isOutput) => nodes[node].Y - top + below + (isOutput
                ? size.OutputPort(Held(port, defs[node].Outputs.Count))
                : size.InputPort(defs[node], Held(port, defs[node].Inputs.Count))),
            Was = was,
            Index = index,
        };
    }

    /// <summary>
    /// A port index held to the ports the module actually has, since a
    /// hand-edited file may name one it does not.
    /// </summary>
    private static int Held(int port, int count) => Math.Clamp(port, 0, Math.Max(count - 1, 0));

    /// <summary>
    /// The drawing itself, over blocks that have already been sized: a column
    /// each by how far along the chain they are, ordered so that the fewest
    /// wires cross, and then placed down the column.
    /// </summary>
    private static void Lay(List<Block> blocks, List<Link> links, int sink, Metrics size)
    {
        if (blocks.Count == 0) return;

        var into = links.ToLookup(link => link.To);
        var outOf = links.ToLookup(link => link.From);
        var columns = Columns(blocks.Count, into, outOf, sink);

        Order(columns, blocks, into, outOf);
        Place(columns, blocks, into, size);
    }

    /// <summary>
    /// Which column each block belongs in, as a list of columns left to right.
    /// </summary>
    /// <remarks>
    /// The column is the longest path forward from anything with nothing feeding
    /// it. Longest rather than shortest because a wire must never run backwards:
    /// with the shortest path a block fed by both a source and a long chain would
    /// sit beside the source. Two are placed by hand afterwards — the Output is
    /// pinned to the last column because it is the end of the patch, and a block
    /// with no wires goes in a column of its own before the first.
    /// </remarks>
    private static List<List<int>> Columns(
        int count,
        ILookup<int, Link> into,
        ILookup<int, Link> outOf,
        int sink)
    {
        var rank = new Dictionary<int, int>();
        for (var block = 0; block < count; block++) Rank(block, []);

        var loose = Enumerable.Range(0, count)
            .Where(block => !into[block].Any() && !outOf[block].Any())
            .ToHashSet();

        var last = rank.Where(r => !loose.Contains(r.Key))
            .Select(r => r.Value)
            .DefaultIfEmpty(0)
            .Max();

        if (sink >= 0) rank[sink] = last;

        // One column before the first for anything wired to nothing, which is
        // why every placed rank is shifted up by one.
        var columns = new List<List<int>>();
        for (var i = 0; i <= last + 1; i++) columns.Add([]);

        for (var block = 0; block < count; block++)
            columns[loose.Contains(block) ? 0 : rank[block] + 1].Add(block);

        // The column kept for the unwired is usually empty, and an empty column
        // is a gap the width of a module with nothing in it. Only that one can
        // be: a longest path leaves no holes behind it, since a block at rank
        // two is fed by something at rank one by definition.
        columns.RemoveAll(column => column.Count == 0);

        return columns;

        // Guarded against a cycle the compiler would refuse but a half-built
        // patch may still be holding: a block already being measured scores
        // zero rather than recursing, exactly as the workbench's own walk does.
        int Rank(int block, HashSet<int> walking)
        {
            if (rank.TryGetValue(block, out var known)) return known;
            if (!walking.Add(block)) return 0;

            var furthest = 0;
            foreach (var link in into[block])
                furthest = Math.Max(furthest, Rank(link.From, walking) + 1);

            walking.Remove(block);
            return rank[block] = furthest;
        }
    }

    /// <summary>
    /// Orders each column so that as few wires cross as possible, by the median
    /// heuristic: a block wants to sit level with the middle of what it is wired
    /// to, and sweeping that wish up and down the columns settles it.
    /// </summary>
    /// <remarks>
    /// The starting order is where the blocks already are, so a patch that is
    /// nearly right is tidied rather than rearranged. Still one answer per input,
    /// which is the property a relaxation cannot offer.
    /// </remarks>
    private static void Order(
        List<List<int>> columns,
        List<Block> blocks,
        ILookup<int, Link> into,
        ILookup<int, Link> outOf)
    {
        foreach (var column in columns)
            column.Sort((a, b) =>
                (blocks[a].Was, blocks[a].Index).CompareTo((blocks[b].Was, blocks[b].Index)));

        var index = new Dictionary<int, int>();
        Reindex();

        for (var sweep = 0; sweep < Sweeps; sweep++)
        {
            for (var c = 1; c < columns.Count; c++) Settle(columns[c], into);
            Reindex();

            for (var c = columns.Count - 2; c >= 0; c--) Settle(columns[c], outOf);
            Reindex();
        }

        void Reindex()
        {
            foreach (var column in columns)
                for (var i = 0; i < column.Count; i++)
                    index[column[i]] = i;
        }

        // Sorted on the median of wherever a block's neighbours sit in the
        // neighbouring column. A block with no neighbours there has no wish, and
        // keeps the place it had — the standard treatment, and the one that
        // stops an unwired socket dragging a whole column about.
        void Settle(List<int> column, ILookup<int, Link> neighbours)
        {
            var wish = new Dictionary<int, double>();

            foreach (var block in column)
            {
                var seen = neighbours[block]
                    .Select(link => index.GetValueOrDefault(link.From == block ? link.To : link.From, -1))
                    .Where(i => i >= 0)
                    .Order()
                    .ToArray();

                wish[block] = seen.Length == 0 ? index[block] : Median(seen);
            }

            // Ties broken on the order already held, so the sort is stable in
            // the way that matters: two blocks wanting the same place keep the
            // one they had rather than swapping on every sweep.
            column.Sort((a, b) => wish[a] != wish[b]
                ? wish[a].CompareTo(wish[b])
                : index[a].CompareTo(index[b]));
        }

        static double Median(int[] sorted) => sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2d;
    }

    /// <summary>
    /// Puts the coordinates on. Each column is as wide as the widest block in
    /// it; down a column, each block is asked where it would have to be for its
    /// wires to run level, and then the column is opened out until nothing
    /// overlaps.
    /// </summary>
    private static void Place(
        List<List<int>> columns,
        List<Block> blocks,
        ILookup<int, Link> into,
        Metrics size)
    {
        var x = 0d;

        foreach (var column in columns)
        {
            var wanted = new double[column.Count];

            for (var i = 0; i < column.Count; i++)
            {
                blocks[column[i]].X = x;
                wanted[i] = Wanted(column[i], i);
            }

            // Opened out in the order the column is already in, which is the
            // order the crossing sweep chose: a block is put where it asked for
            // unless the one above has taken the room, and then it goes under it.
            var y = new double[column.Count];
            var lowest = double.MinValue;

            for (var i = 0; i < column.Count; i++)
            {
                y[i] = Math.Max(wanted[i], lowest);
                lowest = y[i] + blocks[column[i]].Height + size.RowGap;
            }

            // Pushing down to make room drags the whole column down with it, so
            // the shift is taken back out afterwards. What is kept is the
            // spacing; what is not is the accumulated drift.
            var drift = 0d;
            for (var i = 0; i < column.Count; i++) drift += y[i] - wanted[i];
            drift /= column.Count;

            for (var i = 0; i < column.Count; i++) blocks[column[i]].Y = y[i] - drift;

            // A column of ordinary modules is a column one module wide, which is
            // the even spacing this has always been — until a group standing
            // open in one makes it wider.
            x += column.Max(block => blocks[block].Width) + size.ColumnGap;
        }

        // Where this block would sit for its wires to arrive level, averaged over
        // the wires it has: a block with three inputs cannot line up with all
        // three, and the middle is the least wrong place.
        //
        // A block with no wire coming in stacks instead, and never half a column:
        // anything in column two or beyond has something feeding it, so the ones
        // without are whole columns of sources. The stack is tighter than a module
        // is tall, because opening out below puts the real distance in.
        double Wanted(int block, int i)
        {
            var level = 0d;
            var count = 0;

            // The socket on the far end is already placed: columns run left to
            // right and every forward wire comes from the left.
            foreach (var link in into[block])
            {
                level += blocks[link.From].Y + link.Leaves - link.Arrives;
                count++;
            }

            return count > 0 ? level / count : i * size.RowGap;
        }
    }
}
