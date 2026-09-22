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

    /// <summary>
    /// What one run of the layout came to.
    /// </summary>
    /// <param name="Fitted">
    /// Whether the drawing was put on the canvas. False only where every box was
    /// shut and it was still too big, and then nothing was moved at all.
    /// </param>
    /// <param name="Shut">
    /// The groups closed to make it fit, in the order they were closed, and empty
    /// for the ordinary patch that fits as it stands.
    /// </param>
    public readonly record struct Arrangement(bool Fitted, IReadOnlyList<NodeGroup> Shut);

    /// <summary>
    /// What one pass came to: whether the drawing was small enough to be put on the
    /// canvas, and the box worth shutting if it was not.
    /// </summary>
    private readonly record struct Pass(bool Fitted, NodeGroup? Worst);

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
    /// Moves every node of <paramref name="patch"/>, shutting a box where the
    /// drawing will not fit the canvas without it. No wire is added, removed or
    /// rerouted, so this is always safe to run and exactly undoable by putting the
    /// old coordinates and the old boxes back.
    /// </summary>
    /// <param name="patch">The patch to place. Modified in place.</param>
    /// <param name="modules">Which catalog the type ids mean, defaulting to the installed one.</param>
    /// <param name="metrics">How big the nodes are, defaulting to the editor's own.</param>
    /// <param name="only">
    /// The modules to place, or null for every one of them. Given a few, the rest of
    /// the patch is not touched and the drawing lands on the middle of where those
    /// few were rather than on the middle of the canvas, so a corner of a patch
    /// tidies itself where it stands — over a module nobody picked, if that is where
    /// it falls. A box with a module outside the set in it is left alone whole, since
    /// a box is drawn from where all of its modules are. See ADR-0110.
    /// </param>
    /// <remarks>
    /// A drawing wider than the canvas is narrowed by shutting a box rather than by
    /// squeezing, because there is nothing there to squeeze: an open group is a ring
    /// round a sub-drawing of its own, so a row of long ones can want half again the
    /// room there is, and closing every gap in the drawing to nothing does not win
    /// that back. Shut, a box is one module wide and the columns are otherwise
    /// untouched — a group is one block either way, so what changes is a width and
    /// not the shape of the drawing. Which box goes is <see cref="Worst"/>'s to say,
    /// and the fewest go. See ADR-0092.
    /// <para>
    /// And where there is nothing left to shut and it is still too big, nothing
    /// moves at all. <see cref="NodeInstance.X"/> holds every coordinate inside the
    /// canvas, so writing that drawing would fold its far edges onto the boundary
    /// and stack them — and a heap against the edge is worse than the tangle the
    /// button was pressed on. It is the caller's to say so instead.
    /// </para>
    /// </remarks>
    public static Arrangement Arrange(
        Patch patch,
        ModuleCatalog? modules = null,
        Metrics? metrics = null,
        IReadOnlySet<Guid>? only = null)
    {
        var catalog = modules ?? NodeCatalog.Current;
        var size = metrics ?? Metrics.Default;

        // Where the drawing goes. Read before the first pass, because a pass moves
        // the very modules it would be measured from.
        var middle = only is null ? default : Middle(patch, Placed(patch, catalog, only), size);

        // Where everything was, to put back if it turns out there is no drawing to
        // be had. Taken before the first pass rather than inside one, because a
        // pass has laid out the inside of every group before it knows the answer.
        var before = patch.Nodes.Select(node => (Node: node, node.X, node.Y)).ToArray();
        var shut = new List<NodeGroup>();

        while (true)
        {
            var pass = Once(patch, catalog, size, only, middle);

            if (pass.Fitted) return new Arrangement(true, shut);

            if (pass.Worst is not { } box)
            {
                foreach (var (node, x, y) in before) (node.X, node.Y) = (x, y);
                foreach (var group in shut) group.Collapsed = false;

                return new Arrangement(false, []);
            }

            box.Collapsed = true;
            shut.Add(box);
        }
    }

    /// <summary>
    /// One go at the whole drawing, over the boxes as they now stand.
    /// </summary>
    private static Pass Once(
        Patch patch,
        ModuleCatalog catalog,
        Metrics size,
        IReadOnlySet<Guid>? only,
        (double X, double Y) middle)
    {
        var defs = Placed(patch, catalog, only);

        if (defs.Count == 0) return new Pass(true, null);

        var groups = (patch.Groups ?? [])
            .Where(group => group.Members.Count > 0 && group.Members.All(defs.ContainsKey))
            .ToList();

        var nodes = patch.Nodes.Where(n => defs.ContainsKey(n.Id)).ToDictionary(n => n.Id);

        // Where each group was, taken before anything moves: the ordering starts
        // from where things already are, and laying a group out among itself is
        // the first thing that changes that.
        var was = groups.ToDictionary(g => g.Id, g => g.Members.Min(id => nodes[id].Y));

        foreach (var group in groups) Inside(patch, nodes, defs, group, size);

        var blocks = new List<Block>();
        var of = new Dictionary<Guid, int>();
        var kept = groups.ToHashSet();

        // Every box standing open, and the block it is drawn as, which is what the
        // retry above chooses between. Kept by block rather than by width, because
        // which box is worth shutting is a question about columns.
        var open = new List<(NodeGroup Group, int Block)>();

        for (var i = 0; i < patch.Nodes.Count; i++)
        {
            var node = patch.Nodes[i];

            if (!defs.ContainsKey(node.Id) || of.ContainsKey(node.Id)) continue;

            if (patch.GroupOf(node.Id) is { } group && kept.Contains(group))
            {
                foreach (var id in group.Members) of[id] = blocks.Count;
                blocks.Add(Boxed(patch, nodes, defs, group, size, was[group.Id], i));

                if (!group.Collapsed) open.Add((group, blocks.Count - 1));
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

        if (Settle(blocks, middle)) return new Pass(true, null);

        return new Pass(false, Worst(blocks, open, size));
    }

    /// <summary>
    /// Which modules a pass places: the ones the catalog knows, and of those the
    /// ones <paramref name="only"/> names where it names any.
    /// </summary>
    /// <remarks>
    /// A module whose plugin is missing is left where it is: it cannot be measured,
    /// and moving it to a guessed height would scatter the one thing a patch from a
    /// missing plugin still has going for it. A group holding one of those — or, for
    /// a selection, holding a module nobody picked — is left alone whole rather than
    /// moved in part, since a box is drawn from where all of its modules are and
    /// placing some of them slides the box off the ones that stayed.
    /// </remarks>
    private static Dictionary<Guid, NodeDef> Placed(
        Patch patch,
        ModuleCatalog catalog,
        IReadOnlySet<Guid>? only)
    {
        var defs = new Dictionary<Guid, NodeDef>();

        foreach (var node in patch.Nodes)
            if ((only is null || only.Contains(node.Id)) && catalog.Get(node.TypeId) is { } def)
                defs[node.Id] = def;

        foreach (var group in patch.Groups ?? [])
            if (group.Members.Count > 0 && !group.Members.All(defs.ContainsKey))
                foreach (var id in group.Members)
                    defs.Remove(id);

        return defs;
    }

    /// <summary>
    /// The middle of where <paramref name="defs"/>'s modules are now, which is where
    /// the drawing made of them goes back.
    /// </summary>
    /// <remarks>
    /// Measured off what <see cref="Settle"/> lands — a box as its padded ring, a
    /// shut one as the one block at its corner — so a drawing already laid out
    /// goes back exactly where it is.
    /// </remarks>
    private static (double X, double Y) Middle(Patch patch, Dictionary<Guid, NodeDef> defs, Metrics size)
    {
        double left = double.MaxValue, top = double.MaxValue;
        double right = double.MinValue, bottom = double.MinValue;

        var boxed = new HashSet<Guid>();

        foreach (var group in patch.Groups ?? [])
        {
            var members = patch.Nodes.Where(n => group.Members.Contains(n.Id) && defs.ContainsKey(n.Id)).ToList();

            if (members.Count == 0) continue;

            boxed.UnionWith(members.Select(n => n.Id));

            var x = members.Min(n => n.X);
            var y = members.Min(n => n.Y);

            if (group.Collapsed)
            {
                Take(x, y, x + size.Width, y + size.GroupHeight(patch.SocketsOf(group)));
                continue;
            }

            Take(
                x - size.GroupPadding,
                y - size.GroupPadding - size.GroupHandleHeight,
                members.Max(n => n.X + size.Width) + size.GroupPadding,
                members.Max(n => n.Y + size.Height(defs[n.Id])) + size.GroupPadding);
        }

        foreach (var node in patch.Nodes)
            if (!boxed.Contains(node.Id) && defs.TryGetValue(node.Id, out var def))
                Take(node.X, node.Y, node.X + size.Width, node.Y + size.Height(def));

        return left > right ? default : ((left + right) / 2, (top + bottom) / 2);

        void Take(double l, double t, double r, double b)
        {
            left = Math.Min(left, l);
            top = Math.Min(top, t);
            right = Math.Max(right, r);
            bottom = Math.Max(bottom, b);
        }
    }

    /// <summary>
    /// Which open box to shut, of those there are: the one its own column would
    /// narrow the most for.
    /// </summary>
    /// <remarks>
    /// A column is as wide as the widest block in it, so shutting a box that is not
    /// the widest in its own column narrows the drawing by nothing whatever — and
    /// the widest box in a patch very often shares a column with another as wide.
    /// What the column falls to is the next widest block in it, or one module, since
    /// that is what the box becomes.
    /// <para>
    /// Where nothing would gain anything — two open boxes of one width in one column
    /// — the widest goes anyway, because shutting either makes the other worth
    /// shutting and standing still is not on offer. The column is read off the
    /// placement rather than carried down to here: every block in a column sits at
    /// one x.
    /// </para>
    /// </remarks>
    private static NodeGroup? Worst(
        List<Block> blocks,
        List<(NodeGroup Group, int Block)> open,
        Metrics size)
    {
        if (open.Count == 0) return null;

        // The two widest blocks in each column, which between them are how wide it
        // is and how wide it would be without its widest.
        var columns = new Dictionary<double, (double Widest, double Next)>();

        foreach (var block in blocks)
        {
            var (widest, next) = columns.GetValueOrDefault(block.X, (0d, 0d));

            columns[block.X] = block.Width > widest
                ? (block.Width, widest)
                : (widest, Math.Max(next, block.Width));
        }

        return open
            .Select(box => (box.Group, Gain: Gain(box.Block), blocks[box.Block].Width))
            .OrderByDescending(box => box.Gain)
            .ThenByDescending(box => box.Width)
            .First()
            .Group;

        double Gain(int block)
        {
            var (widest, next) = columns[blocks[block].X];

            return blocks[block].Width < widest ? 0d : widest - Math.Max(next, size.Width);
        }
    }

    /// <summary>
    /// Puts the finished drawing down with its middle on <paramref name="middle"/>,
    /// or writes nothing and says it does not fit the canvas. The placement runs
    /// from a corner because a column is easier to reason about running one way;
    /// landing it by the middle is what lets a drawing of part of a patch go back
    /// where that part came from, and the middle of the canvas is the only choice
    /// that uses the whole of it for the rest.
    /// </summary>
    /// <remarks>
    /// Measured before a coordinate is written, which is the whole of how a drawing
    /// too big for the canvas leaves the patch alone: a coordinate is held inside
    /// the canvas as it is set, so there is no writing one down and reading it back
    /// to find out. A drawing that fits but hangs over an edge is slid back in for
    /// the same reason, since the clamp would stack it against the boundary.
    /// </remarks>
    private static bool Settle(List<Block> blocks, (double X, double Y) middle)
    {
        var left = blocks.Min(block => block.X);
        var top = blocks.Min(block => block.Y);
        var right = blocks.Max(block => block.X + block.Width);
        var bottom = blocks.Max(block => block.Y + block.Height);

        var across = (right - left) / 2;
        var down = (bottom - top) / 2;

        if (across > NodeInstance.Across || down > NodeInstance.Down) return false;

        var x = Math.Clamp(middle.X, -NodeInstance.Across + across, NodeInstance.Across - across);
        var y = Math.Clamp(middle.Y, -NodeInstance.Down + down, NodeInstance.Down - down);

        foreach (var block in blocks)
        {
            block.X += x - (left + right) / 2;
            block.Y += y - (top + bottom) / 2;
            block.Put();
        }

        return true;
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
    /// The wires that run backwards are the ones a layered drawing would have to
    /// guess at, and this one is told: <see cref="Cycles.Backwards"/> has already
    /// picked them, and they are where the meaning is cut too — what a backward
    /// wire carries is the previous evaluation. Asked of the wire rather than the
    /// block, so a loop inside a group also cuts what leaves the group. A wire
    /// with both ends on one block is dropped too — it was accounted for when the
    /// group was laid out among itself.
    /// </remarks>
    private static List<Link> Links(
        Patch patch,
        Dictionary<Guid, NodeDef> defs,
        List<Block> blocks,
        Dictionary<Guid, int> of)
    {
        var links = new List<Link>();
        var backwards = Cycles.Backwards(patch);

        foreach (var wire in patch.Connections)
        {
            if (!of.TryGetValue(wire.SourceNode, out var from)) continue;
            if (!of.TryGetValue(wire.TargetNode, out var to)) continue;
            if (from == to) continue;
            if (backwards.Contains(wire)) continue;

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
            // Indexed again after every column rather than after every pass, so each
            // column is sorted against the order its neighbour has now. Against the
            // order it had a pass ago, the last sweep leaves pairs that disagree: a
            // block at the top of its column fed from the bottom of the one before,
            // which opening the column out then turns into a gap as tall as both.
            for (var c = 1; c < columns.Count; c++)
            {
                Settle(columns[c], into);
                Reindex();
            }

            for (var c = columns.Count - 2; c >= 0; c--)
            {
                Settle(columns[c], outOf);
                Reindex();
            }
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
            // ReSharper disable once CompareOfFloatsByEqualityOperator
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

            var y = Opened(column, wanted);

            for (var i = 0; i < column.Count; i++) blocks[column[i]].Y = y[i];

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

        // The column opened out in the order it is already in, which is the order
        // the crossing sweep chose, so that nothing overlaps and every block is as
        // near where it asked to be as the ones around it allow.
        //
        // Blocks that would overlap are pooled into a run, stacked, and the run is
        // put where its members asked on average; a run that then reaches the one
        // above joins it. That is the least total movement the order permits. What
        // it is not is "push everything under the first and take the mean shift
        // back out", which agrees with it when nothing collides and when everything
        // does, and between the two throws a block that had room a long way off to
        // pay for a crowd that had none — one module above a canvas-height of gap.
        double[] Opened(List<int> column, double[] wanted)
        {
            // How far under the top of its run each block sits if the run is tight,
            // taken out of what it asked for so that a run is one number.
            var under = new double[column.Count];
            for (var i = 1; i < column.Count; i++)
                under[i] = under[i - 1] + blocks[column[i - 1]].Height + size.RowGap;

            var runs = new List<(double Sum, int Count)>();

            for (var i = 0; i < column.Count; i++)
            {
                var run = (Sum: wanted[i] - under[i], Count: 1);

                while (runs.Count > 0 && runs[^1].Sum / runs[^1].Count > run.Sum / run.Count)
                {
                    run = (run.Sum + runs[^1].Sum, run.Count + runs[^1].Count);
                    runs.RemoveAt(runs.Count - 1);
                }

                runs.Add(run);
            }

            var y = new double[column.Count];
            var at = 0;

            foreach (var (sum, count) in runs)
                for (var end = at + count; at < end; at++)
                    y[at] = sum / count + under[at];

            return y;
        }
    }
}
