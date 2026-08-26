namespace Flyback.Core.Graph;

/// <summary>
/// Taking part of a patch out of it, and putting part of a patch into one.
/// </summary>
/// <remarks>
/// <para>
/// The graph half of copy and paste, with nothing about a canvas or a clipboard
/// in it: what comes out of <see cref="Copy"/> is an ordinary
/// <see cref="Patch"/>, so it travels as the JSON a patch already travels as
/// (ADR-0020) and needs no format of its own. Which also means the text on the
/// clipboard is a patch file — pasting a saved <c>.fbk</c> into the canvas
/// merges it, and copying a selection out gives something another window can
/// read.
/// </para>
/// <para>
/// Where the pasted modules land is not decided here. This translates by
/// whatever it is handed, because working out a sensible place needs the size
/// of a drawn node and the bounds of a viewport, and neither is the engine's
/// business — see ADR-0044 for the same split.
/// </para>
/// </remarks>
public static class PatchClipboard
{
    /// <summary>
    /// The named modules and the wires between them, as a patch of their own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only wires with <em>both</em> ends among the named modules come. A wire
    /// with one end outside has nothing to be plugged into once it arrives, and
    /// guessing at what it should reach for instead would be inventing a patch
    /// nobody drew.
    /// </para>
    /// <para>
    /// The Output never comes, whatever was selected. A patch has exactly one
    /// and may not hold two (ADR-0037), so it is not a thing that can be pasted
    /// — the same reason Delete leaves it alone.
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

        // Not stamped with a version or a plugin list here: writing it out is
        // what does that, and PatchIo is the one place that knows how. Which is
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
    /// <para>
    /// Every module gets a fresh id and the wires are rewritten to match, which
    /// is what makes pasting twice give two of a thing rather than one thing
    /// mentioned twice. The ids in a fragment are the ones it was copied from,
    /// and pasting into the patch it came from would otherwise collide with
    /// them.
    /// </para>
    /// <para>
    /// Any Output in the fragment is dropped. One arrives whenever the text came
    /// through <see cref="PatchIo.Read"/>, which adds one to anything short of
    /// it, and again whenever somebody pastes a whole saved patch — which is a
    /// thing worth being able to do, and it means everything but that patch's
    /// sink.
    /// </para>
    /// <para>
    /// Nothing here checks that the modules are ones this build has. A fragment
    /// naming a module the catalogue lacks is a thing to refuse with a sentence
    /// rather than to paste with holes in it, and the caller is where there is
    /// somewhere to say it — see <see cref="PatchLoad.IsComplete"/>.
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

        return added;
    }
}
