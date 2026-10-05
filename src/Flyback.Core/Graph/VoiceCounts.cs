namespace Flyback.Core.Graph;

/// <summary>
/// How many voices each module in a patch runs: more than one downstream of a
/// module that starts a polyphonic wire, until something merges them — see
/// ADR-0174.
/// </summary>
/// <remarks>
/// Shared by the compiler, which lowers such a module once per voice, and the
/// canvas, which draws its wires as polyphonic. A wire carries the count of the
/// module it leaves. A module switched off passes on what reaches it, as its
/// wires do.
/// </remarks>
internal static class VoiceCounts
{
    /// <summary>The most voices a wire carries, which is as many as MIDI plays at once.</summary>
    public const int Most = 8;

    /// <summary>The count of every module that runs more than one voice; every other runs one.</summary>
    public static IReadOnlyDictionary<Guid, int> Of(Patch patch, ModuleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(catalog);

        var counts = new Dictionary<Guid, int>();
        var nodes = patch.Nodes.ToDictionary(node => node.Id);
        var leaving = patch.Connections.ToLookup(wire => wire.SourceNode);
        var raised = new Queue<Guid>();

        foreach (var node in patch.Nodes)
        {
            if (node.Off || catalog.Get(node.TypeId)?.StartsVoices is not { } starts) continue;

            var count = Math.Clamp(starts(node), 1, Most);
            if (count <= 1) continue;

            counts[node.Id] = count;
            raised.Enqueue(node.Id);
        }

        // Counts only rise, and only as far as the largest source, so this ends
        // however the patch loops.
        while (raised.Count > 0)
        {
            var from = raised.Dequeue();
            var count = counts[from];

            foreach (var wire in leaving[from])
            {
                if (!nodes.TryGetValue(wire.TargetNode, out var target)) continue;
                if (!target.Off && (catalog.Get(target.TypeId) is not { } def || Merges(def))) continue;
                if (counts.GetValueOrDefault(target.Id, 1) >= count) continue;

                counts[target.Id] = count;
                raised.Enqueue(target.Id);
            }
        }

        return counts;
    }

    /// <summary>
    /// Whether a module hears its inputs' voices summed into one. A chart or a
    /// meter does whether it says so or not: it records one signal.
    /// </summary>
    public static bool Merges(NodeDef def) => def.MergesVoices || def.TapsSignal || def.ChartsSignal;
}
