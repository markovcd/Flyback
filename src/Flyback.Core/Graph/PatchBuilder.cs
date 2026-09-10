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

    /// <summary>
    /// What has been assembled, exactly as it was assembled. A caller that chose
    /// its own coordinates takes the patch from here; a caller that did not
    /// takes it from <see cref="Build"/>, which places it.
    /// </summary>
    public Patch Patch { get; } = new();

    /// <summary>
    /// Adds a module, unplaced. What a patch is and where it is drawn are two
    /// different statements, and only the first of them is a preset's to make —
    /// see ADR-0070.
    /// </summary>
    public NodeInstance Add(string typeId, params (int Port, float Value)[] knobs) =>
        Add(typeId, 0d, 0d, knobs);

    /// <summary>
    /// The same, at a coordinate the caller means: a test about dragging,
    /// framing or hit-testing, where where a node sits is the subject rather
    /// than an accident. Take the patch from <see cref="Patch"/>, which leaves
    /// the coordinates as they were given.
    /// </summary>
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

    /// <summary>
    /// The patch, placed and ready to be shown: the same layered layout the text
    /// language and the Tidy button use (ADR-0044), run once as the patch is handed
    /// over.
    /// </summary>
    /// <remarks>
    /// Here rather than at the picker, so every route to a preset gets the same placed
    /// patch and it is placed before any history opens on it. The layout is
    /// idempotent, so a caller that arranges again gets the same answer.
    /// </remarks>
    public Patch Build()
    {
        PatchLayout.Arrange(Patch, catalog);
        return Patch;
    }
}
