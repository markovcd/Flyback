namespace Flyback.Core.Graph;

/// <summary>
/// Which wires in a patch run backwards. A patch may hold a loop, and what makes
/// one legal is that exactly one wire round it carries the previous evaluation
/// instead of this one — see ADR-0075.
/// </summary>
/// <remarks>
/// The one answer, asked by everything that has to know: the compiler puts a
/// plane on each of these, the canvas draws them dashed, the layout leaves them
/// out of what it lays in layers, and the language writes them as the back-wires
/// they are.
/// <para>
/// The walk runs backwards from the Output along the wires feeding each module,
/// taking a module's inputs in socket order — the same direction and the same
/// order the compiler resolves in. So the wire that is cut is the one that
/// reaches furthest back: in a chain fed round from its end, the return itself.
/// </para>
/// <para>
/// Rooted there rather than at "every node in turn", because the order modules
/// are listed in is not a fact about the patch — the canvas moves a module to the
/// end of the list to draw it in front, so every drag would otherwise be free to
/// move the cut, which changes what a loop is carrying and therefore what the
/// patch sounds like. What the Output cannot reach is walked afterwards in a
/// stable order of its own, since there is nothing there to prefer and nothing
/// listening either.
/// </para>
/// </remarks>
public static class Cycles
{
    /// <summary>
    /// The wires that close a loop, or an empty set for the usual patch that has
    /// none.
    /// </summary>
    public static IReadOnlySet<Connection> Backwards(Patch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);

        var backwards = new HashSet<Connection>();
        var into = new Dictionary<Guid, List<Connection>>();

        foreach (var wire in patch.Connections)
        {
            if (!into.TryGetValue(wire.TargetNode, out var list))
                into[wire.TargetNode] = list = [];

            list.Add(wire);
        }

        // By socket, which is a fact about the module, rather than by when the
        // wire happened to be drawn.
        foreach (var list in into.Values)
            list.Sort((first, second) => first.TargetPort.CompareTo(second.TargetPort));

        var finished = new HashSet<Guid>();
        var open = new HashSet<Guid>();

        foreach (var node in Roots(patch))
            Walk(node.Id);

        return backwards;

        // Written as a walk with its own stack rather than as recursion: a patch
        // is a document somebody may have drawn a thousand modules into, and the
        // depth here is the length of the longest chain in it.
        void Walk(Guid from)
        {
            if (!finished.Add(from)) return;

            var stack = new Stack<(Guid Node, int Next)>();

            stack.Push((from, 0));
            open.Add(from);

            while (stack.Count > 0)
            {
                var (node, next) = stack.Pop();
                var wires = into.TryGetValue(node, out var list) ? list : [];

                if (next >= wires.Count)
                {
                    open.Remove(node);
                    continue;
                }

                stack.Push((node, next + 1));

                var wire = wires[next];

                // A wire out of something the walk is still inside is the one
                // that closes the loop. Everything else has either been finished
                // already or is about to be.
                if (open.Contains(wire.SourceNode))
                {
                    backwards.Add(wire);
                    continue;
                }

                if (!finished.Add(wire.SourceNode)) continue;

                open.Add(wire.SourceNode);
                stack.Push((wire.SourceNode, 0));
            }
        }
    }

    /// <summary>
    /// Where the walk starts: the Output, and then whatever it could not reach.
    /// </summary>
    /// <remarks>
    /// The sink first because that is what a patch is for, and everything a loop
    /// can be heard or seen through hangs off it. The rest by id — an order
    /// nothing about editing can disturb — because a ring nothing reads has no
    /// wire worth preferring, and the only thing that matters there is that the
    /// answer does not wander while somebody is still drawing.
    /// </remarks>
    private static IEnumerable<NodeInstance> Roots(Patch patch) =>
        patch.Nodes
            .Where(node => NodeCatalog.IsSink(node.TypeId))
            .OrderBy(node => node.Id)
            .Concat(patch.Nodes
                .Where(node => !NodeCatalog.IsSink(node.TypeId))
                .OrderBy(node => node.Id));

    /// <summary>
    /// Whose the plane on <paramref name="wire"/> is, so that a loop keeps what
    /// it is carrying while the rest of the patch is edited around it.
    /// </summary>
    /// <remarks>
    /// A wire has no id of its own — a <see cref="Connection"/> is the two ends it
    /// joins and nothing else — so the name is made from those ends, the way
    /// <c>Binder</c> makes a module's id out of where in the source it was
    /// written, and cut the same way: SHA-256 to sixteen bytes, because this is a
    /// name and not a secret. Moving either end is therefore a different wire
    /// carrying a different loop, and it begins again from nothing, which is the
    /// honest answer — what it was carrying belonged to a loop that no longer
    /// exists.
    /// </remarks>
    public static Guid Owner(Connection wire)
    {
        ArgumentNullException.ThrowIfNull(wire);

        Span<byte> name = stackalloc byte[40];

        wire.SourceNode.TryWriteBytes(name[..16]);
        wire.TargetNode.TryWriteBytes(name[16..32]);
        BitConverter.TryWriteBytes(name[32..36], wire.SourcePort);
        BitConverter.TryWriteBytes(name[36..], wire.TargetPort);

        Span<byte> hash = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(name, hash);

        return new Guid(hash[..16]);
    }
}
