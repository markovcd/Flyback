namespace Flyback.Core.Graph;

/// <summary>One placed module: a node type, where it sits, and its knob values.</summary>
public sealed class NodeInstance
{
    public required Guid Id { get; init; }

    public required string TypeId { get; init; }

    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>
    /// Per-input constants, used for any input with nothing wired into it.
    /// Length always matches the definition's input count.
    /// </summary>
    public float[] InputValues { get; set; } = [];

    public static NodeInstance Create(NodeDef def, double x, double y) => new()
    {
        Id = Guid.NewGuid(),
        TypeId = def.TypeId,
        X = x,
        Y = y,
        InputValues = [.. def.Inputs.Select(p => p.Default)],
    };
}

/// <summary>A wire from one node's output socket to another node's input socket.</summary>
public sealed record Connection(Guid SourceNode, int SourcePort, Guid TargetNode, int TargetPort);

/// <summary>The whole document: modules plus the wires between them.</summary>
public sealed class Patch
{
    /// <summary>
    /// The plugins this patch cannot be opened without, stamped as it is
    /// written. Null rather than empty when it uses nothing but the modules that
    /// ship in the engine, so an ordinary patch file looks exactly as it always
    /// did and one saved by an older build still loads.
    /// </summary>
    public List<ModuleProvider>? Requires { get; set; }

    public List<NodeInstance> Nodes { get; set; } = [];

    public List<Connection> Connections { get; set; } = [];

    public NodeInstance? Find(Guid id) => Nodes.FirstOrDefault(n => n.Id == id);

    /// <summary>The wire feeding an input, if any. An input takes at most one.</summary>
    public Connection? IncomingTo(Guid node, int port) =>
        Connections.FirstOrDefault(c => c.TargetNode == node && c.TargetPort == port);

    /// <summary>
    /// Wires two sockets together, replacing whatever already fed the target
    /// input. Outputs may fan out to any number of inputs.
    /// </summary>
    public void Connect(Guid sourceNode, int sourcePort, Guid targetNode, int targetPort)
    {
        if (sourceNode == targetNode) return;

        Connections.RemoveAll(c => c.TargetNode == targetNode && c.TargetPort == targetPort);
        Connections.Add(new Connection(sourceNode, sourcePort, targetNode, targetPort));
    }

    public void Disconnect(Guid targetNode, int targetPort) =>
        Connections.RemoveAll(c => c.TargetNode == targetNode && c.TargetPort == targetPort);

    public void Remove(Guid nodeId)
    {
        Nodes.RemoveAll(n => n.Id == nodeId);
        Connections.RemoveAll(c => c.SourceNode == nodeId || c.TargetNode == nodeId);
    }
}
