namespace Flyback.Core.Graph;

/// <summary>
/// The two edits that would otherwise pull a Send and its Receives apart: putting
/// the Send on another bus, and pasting a copy of both.
/// </summary>
/// <remarks>
/// A wire follows its sockets through a rename and a paste because it names them.
/// A bus is matched by what is typed on each end (ADR-0126), so nothing follows
/// unless something here makes it.
/// </remarks>
public static class BusEdits
{
    /// <summary>
    /// Puts <paramref name="send"/> on <paramref name="bus"/>, and the Receives that
    /// were playing it with it.
    /// </summary>
    /// <remarks>
    /// The Receives move only where they were this Send's to begin with, and where
    /// they would still be afterwards: it was the Send its bus was heard from, and no
    /// other Send is on the bus it goes to. Either way round, moving them would
    /// change what they play rather than keep it.
    /// </remarks>
    /// <returns>The Receives that moved.</returns>
    public static IReadOnlyList<NodeInstance> Rename(Patch patch, NodeInstance send, string bus)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(send);

        var from = NodeCatalog.BusOf(send);

        NodeCatalog.PutOnBus(send, bus);

        var to = NodeCatalog.BusOf(send);

        if (send.TypeId != NodeCatalog.SendTypeId || from is null || to is null || Same(from, to)) return [];

        var others = patch.Nodes.Where(n => n.TypeId == NodeCatalog.SendTypeId && n.Id != send.Id).ToList();

        // The Send a bus is heard from is the first by id, which is Buses' rule.
        var wasHeard = !others.Any(other => Same(NodeCatalog.BusOf(other)!, from) && other.Id.CompareTo(send.Id) < 0);
        var lands = !others.Any(other => Same(NodeCatalog.BusOf(other)!, to));

        if (!wasHeard || !lands) return [];

        var moved = patch.Nodes
            .Where(n => n.TypeId == NodeCatalog.ReceiveTypeId && Same(NodeCatalog.BusOf(n)!, from))
            .ToList();

        foreach (var receive in moved) NodeCatalog.PutOnBus(receive, to);

        return moved;
    }

    /// <summary>
    /// Gives what <paramref name="arrived"/> in <paramref name="patch"/> a bus of its
    /// own, where it brought a Send onto a bus the patch was already using.
    /// </summary>
    /// <remarks>
    /// The Receives that arrived with the Send go with it, so a copy of a Send and its
    /// Receives is a second pair rather than a second Send nobody hears. A Receive that
    /// arrived alone stays where it was: another listener on a bus that is there is
    /// what pasting one is for.
    /// </remarks>
    public static void Separate(Patch patch, IReadOnlyList<NodeInstance> arrived)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(arrived);

        var brought = arrived
            .Where(n => n.TypeId == NodeCatalog.SendTypeId)
            .Select(n => NodeCatalog.BusOf(n)!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (brought.Count == 0) return;

        var fresh = arrived.Select(n => n.Id).ToHashSet();

        // Every bus either end is on, so a new name lands on nobody's.
        var taken = patch.Nodes
            .Where(n => !fresh.Contains(n.Id))
            .Select(NodeCatalog.BusOf)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var sentOn = patch.Nodes
            .Where(n => n.TypeId == NodeCatalog.SendTypeId && !fresh.Contains(n.Id))
            .Select(n => NodeCatalog.BusOf(n)!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var bus in brought)
        {
            if (!sentOn.Contains(bus)) continue;

            var own = Free(bus, taken);

            taken.Add(own);

            foreach (var node in arrived)
                if (NodeCatalog.BusOf(node) is { } on && Same(on, bus))
                    NodeCatalog.PutOnBus(node, own);
        }
    }

    /// <summary><paramref name="bus"/> with the first number after it that nothing in <paramref name="taken"/> has.</summary>
    private static string Free(string bus, HashSet<string> taken)
    {
        var stem = bus;
        var at = bus.LastIndexOf(' ');

        // "kick 2" counts on from 2 rather than becoming "kick 2 2".
        if (at > 0 && int.TryParse(bus.AsSpan(at + 1), out _)) stem = bus[..at];

        for (var number = 2; ; number++)
        {
            var named = $"{stem} {number}";

            if (!taken.Contains(named)) return named;
        }
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
