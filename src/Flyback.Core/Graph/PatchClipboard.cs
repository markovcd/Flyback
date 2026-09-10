namespace Flyback.Core.Graph;

/// <summary>
/// Taking part of a patch out of it, and putting part of a patch into one.
/// </summary>
/// <remarks>
/// The graph half of copy and paste, with nothing about a canvas in it: what
/// comes out of <see cref="Copy"/> is an ordinary <see cref="Patch"/> and
/// travels as the JSON a patch already travels as (ADR-0020) — so the text on
/// the clipboard is a patch file, and pasting a saved <c>.fbk</c> merges it.
/// Where the pasted modules land is not decided here: that needs the size of a
/// drawn node and the bounds of a viewport.
/// </remarks>
public static class PatchClipboard
{
    /// <summary>
    /// The named modules and the wires between them, as a patch of their own.
    /// </summary>
    /// <remarks>
    /// Only wires with both ends among the named modules come: one with an end
    /// outside has nothing to be plugged into. The Output never comes, since a
    /// patch has exactly one and may not hold two (ADR-0037).
    /// <para>
    /// A box comes with what is in it, and only where all of it is coming. A
    /// group with a member left behind would arrive as a different box — a
    /// different shape and a different set of sockets — so it is dropped rather
    /// than clipped, and what was inside arrives as ordinary modules.
    /// </para>
    /// </remarks>
    /// <param name="patch">Where the modules are now. Not modified.</param>
    /// <param name="ids">Which modules to take. Anything not in the patch is ignored.</param>
    public static Patch Copy(Patch patch, IEnumerable<Guid> ids)
    {
        var taking = ids
            .Distinct()
            .Select(patch.Find)
            .OfType<NodeInstance>()
            .Where(node => !NodeCatalog.IsSink(node.TypeId))
            .ToArray();

        var inside = taking.Select(node => node.Id).ToHashSet();
        var copy = new Patch();

        // Deep, so that what is held is a picture of the patch as it was rather
        // than a view onto one that may go on being edited — see
        // NodeInstance.Clone. Keeping the ids, because a fragment is matched to
        // its wires by them and the paste is what makes them fresh.
        foreach (var node in taking) copy.Nodes.Add(node.Clone());

        foreach (var wire in patch.Connections)
            if (inside.Contains(wire.SourceNode) && inside.Contains(wire.TargetNode))
                copy.Connections.Add(wire);

        // The boxes drawn round what is coming, whole or not at all. Their ids
        // travel exactly as the modules' do, and the paste is what makes them
        // fresh. See NodeGroup: a group is a fact about the canvas, so this is
        // the one thing copied here that the compiler will never be told about.
        foreach (var group in patch.Groups ?? [])
            if (group.Members.Count >= NodeGroup.Fewest && group.Members.All(inside.Contains))
                (copy.Groups ??= []).Add(group.Clone());

        // Not stamped with a version or a plugin list here: writing it out is
        // what does that, and PatchIO is the one place that knows how. Which is
        // also what makes pasting into a build without the plugin a module came
        // from refused by name rather than quietly full of holes — the stamp
        // goes on at ToJson and is checked at Read.
        return copy;
    }

    /// <summary>
    /// Adds a copy of <paramref name="fragment"/> to <paramref name="into"/>,
    /// shifted by (<paramref name="dx"/>, <paramref name="dy"/>), and hands back
    /// the modules that arrived.
    /// </summary>
    /// <remarks>
    /// Every module gets a fresh id and the wires are rewritten to match, which is
    /// what makes pasting twice give two of a thing. Any Output in the fragment is
    /// dropped — one arrives whenever the text came through
    /// <see cref="PatchIO.Read"/>, and whenever somebody pastes a whole saved
    /// patch. Nothing here checks that the modules are ones this build has: that
    /// is a thing to refuse with a sentence, and the caller is where there is
    /// somewhere to say it — see <see cref="PatchLoad.IsComplete"/>.
    /// <para>
    /// A group is drawn again round the modules that arrived, with a fresh id and
    /// its sockets pointed at their new ones — the unwired ones included, so a box
    /// arrives with the edge somebody arranged. Made after the wires, so
    /// <see cref="Patch.Connect"/> does not read them as crossing an edge and put
    /// back sockets that had been taken off it.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<NodeInstance> Paste(
        Patch into,
        Patch fragment,
        double dx = 0,
        double dy = 0)
    {
        var arriving = fragment.Nodes
            .Where(node => !NodeCatalog.IsSink(node.TypeId))
            .ToArray();

        if (arriving.Length == 0) return [];

        var renamed = new Dictionary<Guid, Guid>();
        var added = new List<NodeInstance>(arriving.Length);

        foreach (var node in arriving)
        {
            var fresh = node.Clone(Guid.NewGuid(), dx, dy);

            renamed[node.Id] = fresh.Id;
            into.Nodes.Add(fresh);
            added.Add(fresh);
        }

        foreach (var wire in fragment.Connections)
        {
            // A wire naming something that did not arrive — the sink that was
            // dropped, or a fragment somebody hand-edited — is left behind
            // rather than wired to whatever happens to share its id.
            if (!renamed.TryGetValue(wire.SourceNode, out var source)) continue;
            if (!renamed.TryGetValue(wire.TargetNode, out var target)) continue;

            into.Connect(source, wire.SourcePort, target, wire.TargetPort);
        }

        foreach (var group in fragment.Groups ?? [])
        {
            var members = group.Members
                .Where(renamed.ContainsKey)
                .Select(id => renamed[id])
                .ToList();

            // Worn down past what a group may be by whatever did not arrive —
            // a sink that was dropped, or a fragment somebody hand-edited. The
            // rule Patch.Forget keeps after a delete, kept here too rather than
            // pasting the box round one module that Ctrl+G declines to draw.
            if (members.Count < NodeGroup.Fewest) continue;

            (into.Groups ??= []).Add(new NodeGroup
            {
                Id = Guid.NewGuid(),
                Name = group.Name,
                Collapsed = group.Collapsed,
                Members = members,
                Exposed =
                [
                    .. group.Exposed
                        .Where(socket => renamed.ContainsKey(socket.Node))
                        .Select(socket => socket with { Node = renamed[socket.Node] }),
                ],
            });
        }

        return added;
    }
}
