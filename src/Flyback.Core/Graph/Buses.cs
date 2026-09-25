namespace Flyback.Core.Graph;

/// <summary>
/// What a patch is with its buses joined up: every wire out of a Receive
/// comes instead from whatever feeds the Send on its bus.
/// </summary>
/// <remarks>
/// Done before anything is compiled, so a bus is a wire to the compiler and to
/// <see cref="Cycles"/>. A loop closed through one is delayed like any other
/// (ADR-0075), and nothing downstream has to know buses exist. The patch itself
/// is not touched: the Send and the Receive are what is saved and drawn.
/// </remarks>
internal static class Buses
{
    /// <summary>
    /// <paramref name="patch"/> with its buses as wires, or <paramref name="patch"/>
    /// itself where it has no Receive.
    /// </summary>
    /// <param name="warn">Told about a Receive with no Send, and a bus with two.</param>
    public static Patch Joined(Patch patch, Action<Guid, string>? warn = null)
    {
        ArgumentNullException.ThrowIfNull(patch);

        if (!patch.Nodes.Any(n => n.TypeId == NodeCatalog.ReceiveTypeId)) return patch;

        // By id when two Sends share a bus, which nothing about editing moves —
        // the canvas reorders the list to draw a module in front.
        var sends = new Dictionary<string, NodeInstance>(StringComparer.OrdinalIgnoreCase);

        foreach (var send in patch.Nodes.Where(n => n.TypeId == NodeCatalog.SendTypeId).OrderBy(n => n.Id))
        {
            var bus = NodeCatalog.BusOf(send)!;

            if (!sends.TryAdd(bus, send))
                warn?.Invoke(send.Id, $"Another Send is already on '{bus}', so this one is not heard. Put it on a bus of its own.");
        }

        foreach (var receive in patch.Nodes.Where(n => n.TypeId == NodeCatalog.ReceiveTypeId))
        {
            var bus = NodeCatalog.BusOf(receive)!;

            if (!sends.ContainsKey(bus))
                warn?.Invoke(receive.Id, $"No Send is on '{bus}', so this Receive carries nothing.");
        }

        var wires = new List<Connection>(patch.Connections.Count);
        var ringed = new HashSet<Guid>();

        foreach (var wire in patch.Connections)
        {
            if (patch.Find(wire.SourceNode) is not { TypeId: NodeCatalog.ReceiveTypeId } receive)
            {
                wires.Add(wire);
                continue;
            }

            // A Receive carrying nothing is a socket with no wire in it, which
            // rests on its knob, rather than one fed silence.
            if (Feed(receive) is { } feed)
                wires.Add(wire with { SourceNode = feed.SourceNode, SourcePort = feed.SourcePort });
        }

        foreach (var receive in ringed)
            warn?.Invoke(receive, $"'{NodeCatalog.BusOf(patch.Find(receive)!)}' is fed round from its own Receive, so it carries nothing.");

        return new Patch
        {
            Version = patch.Version,
            Requires = patch.Requires,
            Nodes = patch.Nodes,
            Connections = wires,
            Groups = patch.Groups,
            Controls = patch.Controls,
            Keyboard = patch.Keyboard,
            Description = patch.Description,
            Author = patch.Author,
            Tags = patch.Tags,
        };

        // The wire into the Send a Receive hears, followed through a Send that
        // is itself fed by a Receive. Switched off, either end carries nothing.
        Connection? Feed(NodeInstance receive)
        {
            var heard = receive.Id;
            var through = new HashSet<Guid>();

            while (through.Add(receive.Id))
            {
                if (receive.Off) return null;
                if (!sends.TryGetValue(NodeCatalog.BusOf(receive)!, out var send) || send.Off) return null;
                if (patch.IncomingTo(send.Id, 0) is not { } into) return null;
                if (patch.Find(into.SourceNode) is not { TypeId: NodeCatalog.ReceiveTypeId } next) return into;

                receive = next;
            }

            // Buses fed round into each other, with nothing at the end to hear.
            ringed.Add(heard);
            return null;
        }
    }
}
