namespace Flyback.Core.Graph;

/// <summary>
/// Places every node of a patch so the signal reads left to right and the wires
/// between them can be seen.
/// </summary>
/// <remarks>
/// <para>
/// A layered drawing, in the four stages the technique is usually written in:
/// cut the edges that run backwards, put every node in a column by how far along
/// the chain it is, order each column so that the fewest wires cross, then place
/// the nodes down the column so that a wire meets its two sockets as level as it
/// can. See ADR-0044 for why this rather than a relaxation.
/// </para>
/// <para>
/// It lives here rather than in the editor because two callers want it and only
/// one of them has a canvas: the button on the toolbar, and the assistant's
/// workbench, which places nodes for a model that never thinks about
/// coordinates. What the editor supplies is <see cref="Metrics"/> — the only
/// thing here that is really the view's business.
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
    public readonly record struct Metrics(
        double Width,
        double HeaderHeight,
        double RowHeight,
        double FooterPadding,
        double ColumnGap,
        double RowGap)
    {
        /// <summary>
        /// The editor's own numbers, for a caller that has no editor. Pinned to
        /// the real ones by a test rather than by a reference, because the shape
        /// of a node is the view's to decide and the engine should not be asking.
        /// </summary>
        public static Metrics Default => new(196d, 26d, 20d, 8d, 108d, 40d);

        public double Height(NodeDef def) =>
            HeaderHeight + (def.Inputs.Count + def.Outputs.Count) * RowHeight + FooterPadding;

        /// <summary>How far below a node's top edge one of its output sockets sits.</summary>
        public double OutputPort(int index) => HeaderHeight + (index + 0.5d) * RowHeight;

        /// <summary>The same for an input, which is below every output — the Blender order.</summary>
        public double InputPort(NodeDef def, int index) =>
            HeaderHeight + (def.Outputs.Count + index + 0.5d) * RowHeight;
    }

    /// <summary>How many times the ordering is swept up and back down the columns.</summary>
    private const int Sweeps = 4;

    /// <summary>Where the leftmost column starts, so nothing lands on the canvas edge.</summary>
    private const double Margin = 40d;

    /// <summary>
    /// Moves every node of <paramref name="patch"/>. Nothing else about the
    /// patch is touched — no wire is added, removed or rerouted — so this is
    /// always safe to run and always exactly undoable by putting the old
    /// coordinates back.
    /// </summary>
    /// <param name="patch">The patch to place. Modified in place.</param>
    /// <param name="modules">Which catalogue the type ids mean, defaulting to the installed one.</param>
    /// <param name="metrics">How big the nodes are, defaulting to the editor's own.</param>
    public static void Arrange(Patch patch, ModuleCatalog? modules = null, Metrics? metrics = null)
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

        if (defs.Count == 0) return;

        var (into, outOf) = Wires(patch, defs);
        var columns = Columns(patch, defs, into, outOf);

        Order(columns, into, outOf, patch);
        Place(patch, defs, into, columns, size);
    }

    /// <summary>
    /// The wires, indexed both ways and with the backward ones dropped.
    /// </summary>
    /// <remarks>
    /// A patch may hold a cycle, and only through a cycle breaker
    /// (<see cref="NodeDef.IsCycleBreaker"/>) — which is what
    /// <see cref="Patch.WouldCycle"/> enforces and what makes the back edges
    /// free to find here. Every other layered drawing has to guess at a set of
    /// edges to reverse; this one is told. Cutting the wires that leave a
    /// breaker leaves a graph that is acyclic by construction, and it cuts them
    /// where the meaning already is: what leaves a Unit Delay is the previous
    /// evaluation, so it is not part of this one's chain.
    /// </remarks>
    private static (ILookup<Guid, Connection> Into, ILookup<Guid, Connection> OutOf) Wires(
        Patch patch,
        Dictionary<Guid, NodeDef> defs)
    {
        var forward = patch.Connections
            .Where(wire =>
                defs.ContainsKey(wire.SourceNode)
                && defs.ContainsKey(wire.TargetNode)
                && !defs[wire.SourceNode].IsCycleBreaker)
            .ToArray();

        return (
            forward.ToLookup(wire => wire.TargetNode),
            forward.ToLookup(wire => wire.SourceNode));
    }

    /// <summary>
    /// Which column each node belongs in, as a list of columns left to right.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The column is the longest path forward from anything with nothing feeding
    /// it, so a node sits one place to the right of the furthest-along thing it
    /// reads. Longest rather than shortest because a wire must never run
    /// backwards: with the shortest path a node fed by both a source and a long
    /// chain would sit beside the source, and the chain would have to reach back
    /// to find it.
    /// </para>
    /// <para>
    /// Two kinds of node are placed by hand afterwards. The Output is pinned to
    /// the last column whatever its path length says, because it is the end of
    /// the patch and reads as the end wherever the arithmetic puts it. And a
    /// node with no wires at all goes in a column of its own before the first,
    /// rather than among the sources it is not one of.
    /// </para>
    /// </remarks>
    private static List<List<Guid>> Columns(
        Patch patch,
        Dictionary<Guid, NodeDef> defs,
        ILookup<Guid, Connection> into,
        ILookup<Guid, Connection> outOf)
    {
        var rank = new Dictionary<Guid, int>();
        foreach (var node in patch.Nodes)
            if (defs.ContainsKey(node.Id))
                Rank(node.Id, []);

        var loose = patch.Nodes
            .Where(n => defs.ContainsKey(n.Id) && !into[n.Id].Any() && !outOf[n.Id].Any())
            .Select(n => n.Id)
            .ToHashSet();

        var last = rank.Where(r => !loose.Contains(r.Key))
            .Select(r => r.Value)
            .DefaultIfEmpty(0)
            .Max();

        if (patch.FirstOf(NodeCatalog.OutputTypeId) is { } sink && rank.ContainsKey(sink.Id))
            rank[sink.Id] = last;

        // One column before the first for anything wired to nothing, which is
        // why every placed rank is shifted up by one.
        var columns = new List<List<Guid>>();
        for (var i = 0; i <= last + 1; i++) columns.Add([]);

        foreach (var node in patch.Nodes)
        {
            if (!defs.ContainsKey(node.Id)) continue;
            columns[loose.Contains(node.Id) ? 0 : rank[node.Id] + 1].Add(node.Id);
        }

        // The column kept for the unwired is usually empty, and an empty column
        // is a gap the width of a module with nothing in it. Only that one can
        // be: a longest path leaves no holes behind it, since a node at rank two
        // is fed by something at rank one by definition.
        columns.RemoveAll(column => column.Count == 0);

        return columns;

        // Guarded against a cycle the compiler would refuse but a half-built
        // patch may still be holding: a node already being measured scores
        // zero rather than recursing, exactly as the workbench's own walk does.
        int Rank(Guid id, HashSet<Guid> walking)
        {
            if (rank.TryGetValue(id, out var known)) return known;
            if (!walking.Add(id)) return 0;

            var furthest = 0;
            foreach (var wire in into[id])
                furthest = Math.Max(furthest, Rank(wire.SourceNode, walking) + 1);

            walking.Remove(id);
            return rank[id] = furthest;
        }
    }

    /// <summary>
    /// Orders each column so that as few wires cross as possible, by the median
    /// heuristic: a node wants to sit level with the middle of what it is wired
    /// to, and sweeping that wish up and down the columns settles it.
    /// </summary>
    /// <remarks>
    /// The starting order is where the nodes already are, so a patch that is
    /// nearly right is tidied rather than rearranged and a node the user dragged
    /// to the top stays near the top. It is still one answer per input — the
    /// same patch in the same positions lays out the same way every time, which
    /// is the property a relaxation cannot offer.
    /// </remarks>
    private static void Order(
        List<List<Guid>> columns,
        ILookup<Guid, Connection> into,
        ILookup<Guid, Connection> outOf,
        Patch patch)
    {
        var at = patch.Nodes.Select((n, i) => (n, i)).ToDictionary(p => p.n.Id, p => (p.n.Y, p.i));

        foreach (var column in columns)
            column.Sort((a, b) => at[a].CompareTo(at[b]));

        var index = new Dictionary<Guid, int>();
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

        // Sorted on the median of wherever a node's neighbours sit in the
        // neighbouring column. A node with no neighbours there has no wish, and
        // keeps the place it had — the standard treatment, and the one that
        // stops an unwired socket dragging a whole column about.
        void Settle(List<Guid> column, ILookup<Guid, Connection> neighbours)
        {
            var wish = new Dictionary<Guid, double>();

            foreach (var id in column)
            {
                var seen = neighbours[id]
                    .Select(wire => index.GetValueOrDefault(
                        wire.SourceNode == id ? wire.TargetNode : wire.SourceNode, -1))
                    .Where(i => i >= 0)
                    .Order()
                    .ToArray();

                wish[id] = seen.Length == 0 ? index[id] : Median(seen);
            }

            // Ties broken on the order already held, so the sort is stable in
            // the way that matters: two nodes wanting the same place keep the
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
    /// Puts the coordinates on. Columns are evenly spaced across; down a column,
    /// each node is asked where it would have to be for its wires to run level,
    /// and then the column is opened out until nothing overlaps.
    /// </summary>
    private static void Place(
        Patch patch,
        Dictionary<Guid, NodeDef> defs,
        ILookup<Guid, Connection> into,
        List<List<Guid>> columns,
        Metrics size)
    {
        var nodes = patch.Nodes.Where(n => defs.ContainsKey(n.Id)).ToDictionary(n => n.Id);

        for (var c = 0; c < columns.Count; c++)
        {
            var column = columns[c];
            if (column.Count == 0) continue;

            var x = Margin + c * (size.Width + size.ColumnGap);
            var wanted = new double[column.Count];

            for (var i = 0; i < column.Count; i++)
            {
                nodes[column[i]].X = x;
                wanted[i] = Wanted(column[i], i);
            }

            // Opened out in the order the column is already in, which is the
            // order the crossing sweep chose: a node is put where it asked for
            // unless the one above has taken the room, and then it goes under it.
            var y = new double[column.Count];
            var lowest = double.MinValue;

            for (var i = 0; i < column.Count; i++)
            {
                y[i] = Math.Max(wanted[i], lowest);
                lowest = y[i] + size.Height(defs[column[i]]) + size.RowGap;
            }

            // Pushing down to make room drags the whole column down with it, so
            // the shift is taken back out afterwards. What is kept is the
            // spacing; what is not is the accumulated drift.
            var drift = 0d;
            for (var i = 0; i < column.Count; i++) drift += y[i] - wanted[i];
            drift /= column.Count;

            for (var i = 0; i < column.Count; i++)
                nodes[column[i]].Y = y[i] - drift;

            // Where this node would sit for its wires to arrive level. Averaged
            // over the wires it has, because a node with three inputs cannot
            // line up with all three and the middle is the least wrong place.
            //
            // A node with no wire coming in has nothing to line up with and
            // simply stacks. That is never half a column: a node is in column
            // two or beyond exactly because something feeds it, so the ones
            // without are the sources and the unwired, and those are whole
            // columns of their own. The stack is deliberately tighter than a
            // node is tall — opening out below puts the real distance in, and
            // this only has to say which order they go in.
            double Wanted(Guid id, int i)
            {
                var def = defs[id];
                var level = 0d;
                var count = 0;

                foreach (var wire in into[id])
                {
                    if (!nodes.TryGetValue(wire.SourceNode, out var from)) continue;
                    if (!defs.TryGetValue(wire.SourceNode, out var fromDef)) continue;

                    // The socket on the far end is already placed: columns run
                    // left to right and every forward wire comes from the left.
                    var socket = from.Y + size.OutputPort(Math.Clamp(wire.SourcePort, 0, Math.Max(fromDef.Outputs.Count - 1, 0)));

                    level += socket - size.InputPort(def, Math.Clamp(wire.TargetPort, 0, Math.Max(def.Inputs.Count - 1, 0)));
                    count++;
                }

                return count > 0 ? level / count : i * size.RowGap;
            }
        }
    }
}
