namespace VideoSynth.Core.Graph;

/// <summary>Small helper for assembling patches in code, used by the presets and by tests.</summary>
public sealed class PatchBuilder
{
    public Patch Patch { get; } = new();

    public NodeInstance Add(string typeId, double x, double y, params (int Port, float Value)[] knobs)
    {
        var node = NodeInstance.Create(NodeCatalog.Require(typeId), x, y);

        foreach (var (port, value) in knobs)
            if (port >= 0 && port < node.InputValues.Length)
                node.InputValues[port] = value;

        Patch.Nodes.Add(node);
        return node;
    }

    public PatchBuilder Wire(NodeInstance source, int sourcePort, NodeInstance target, int targetPort)
    {
        Patch.Connect(source.Id, sourcePort, target.Id, targetPort);
        return this;
    }
}
