using Flyback.Core.Graph;

namespace Flyback.Editor.Canvas;

/// <summary>
/// What drawing one frame has worked out about the patch once: each box's sockets, each
/// module by its id, the wire into each input, the wires that run backwards and the voices
/// each carries. Every wire and row asks again, and a pan asks every frame.
/// </summary>
/// <remarks>Lives only while one frame is drawn (<see cref="PaintFrame"/>), when the patch cannot change.</remarks>
internal sealed class SceneMemo(Patch patch)
{
    private readonly Dictionary<NodeGroup, GroupSockets> sockets = new(ReferenceEqualityComparer.Instance);
    private Dictionary<Guid, NodeInstance>? nodes;
    private Dictionary<(Guid Node, int Port), Connection>? incoming;
    private IReadOnlySet<Connection>? backwards;
    private IReadOnlyDictionary<Guid, int>? voices;

    /// <summary>The patch this frame draws, which every answer here is about.</summary>
    public Patch Patch => patch;

    public GroupSockets SocketsOf(NodeGroup group)
    {
        if (sockets.TryGetValue(group, out var known)) return known;

        return sockets[group] = patch.SocketsOf(group);
    }

    public NodeInstance? Find(Guid id)
    {
        if (nodes is null)
        {
            nodes = new Dictionary<Guid, NodeInstance>(patch.Nodes.Count);

            // The first of two with one id, as Patch.Find has it.
            foreach (var node in patch.Nodes) nodes.TryAdd(node.Id, node);
        }

        return nodes.GetValueOrDefault(id);
    }

    /// <summary>The wire into an input, as <see cref="Patch.IncomingTo"/> finds it.</summary>
    public Connection? IncomingTo(Guid node, int port)
    {
        if (incoming is null)
        {
            incoming = new Dictionary<(Guid, int), Connection>(patch.Connections.Count);

            foreach (var wire in patch.Connections) incoming.TryAdd((wire.TargetNode, wire.TargetPort), wire);
        }

        return incoming.GetValueOrDefault((node, port));
    }

    public IReadOnlySet<Connection> Backwards => backwards ??= Cycles.BackwardsThroughBuses(patch);

    public IReadOnlyDictionary<Guid, int> Voices => voices ??= VoiceCounts.Of(Buses.Joined(patch), NodeCatalog.Current);
}
