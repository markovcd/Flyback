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
/// A depth-first walk in the order the patch is written down — nodes as they are
/// listed, wires as they were drawn — so the same file always gives the same
/// answer, and a patch loaded, saved and loaded again cuts its loops in the same
/// places. Which wire of a loop it lands on is therefore a fact about the order
/// the loop was drawn in, and rerouting one is what moves it.
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
        var leaving = new Dictionary<Guid, List<Connection>>();

        foreach (var wire in patch.Connections)
        {
            if (!leaving.TryGetValue(wire.SourceNode, out var list))
                leaving[wire.SourceNode] = list = [];

            list.Add(wire);
        }

        var finished = new HashSet<Guid>();
        var open = new HashSet<Guid>();

        foreach (var node in patch.Nodes)
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
                var wires = leaving.TryGetValue(node, out var list) ? list : [];

                if (next >= wires.Count)
                {
                    open.Remove(node);
                    continue;
                }

                stack.Push((node, next + 1));

                var wire = wires[next];

                // A wire into something the walk is still inside is the one that
                // closes the loop. Everything else has either been finished
                // already or is about to be.
                if (open.Contains(wire.TargetNode))
                {
                    backwards.Add(wire);
                    continue;
                }

                if (!finished.Add(wire.TargetNode)) continue;

                open.Add(wire.TargetNode);
                stack.Push((wire.TargetNode, 0));
            }
        }
    }

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
