namespace Flyback.Core.Graph;

/// <summary>Small helper for assembling patches in code, used by the presets and by tests.</summary>
/// <param name="modules">
/// Which catalogue the type ids mean, defaulting to the installed one. A preset
/// that uses a plugin's modules must be handed the catalogue containing them,
/// rather than relying on what happens to be installed.
/// </param>
public sealed class PatchBuilder(ModuleCatalog? modules = null)
{
    private readonly ModuleCatalog catalog = modules ?? NodeCatalog.Current;

    public Patch Patch { get; } = new();

    public NodeInstance Add(string typeId, double x, double y, params (int Port, float Value)[] knobs)
    {
        var node = NodeInstance.Create(catalog.Require(typeId), x, y);

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

    /// <summary>
    /// Draws <paramref name="nodes"/> together as one named, collapsed box — the
    /// same grouping a person makes by hand on the canvas
    /// (<see cref="Patch.Group"/>), used here to mark out a preset's own logical
    /// parts as it is built rather than leaving a reader to find them by the
    /// comments alone.
    /// </summary>
    public PatchBuilder Group(string name, params NodeInstance[] nodes)
    {
        if (Patch.Group(nodes.Select(n => n.Id)) is { } group) group.Rename(name);
        return this;
    }
}
