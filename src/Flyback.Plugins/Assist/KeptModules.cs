using Flyback.Core.Graph;

namespace Flyback.Plugins.Assist;

/// <summary>
/// A patch rebuilt from text, with each module the text still names under the id it
/// had before, so what it remembers and what points at it carry over (ADR-0067).
/// </summary>
/// <remarks>
/// Matched by handle, which is the <c>let</c> name the text binds it to, and by type:
/// a name rebound to a different module is a different module. The Output is always
/// the same one. A module the text does not name keeps the id the text gives it,
/// which is already stable where the text was written rather than printed.
/// </remarks>
internal static class KeptModules
{
    /// <param name="built">The patch the text built.</param>
    /// <param name="named">The module each handle names in <paramref name="built"/>.</param>
    /// <param name="was">The patch it replaces.</param>
    /// <param name="handles">The module each handle named in <paramref name="was"/>.</param>
    /// <returns>The patch with old ids put back, and <paramref name="named"/> in those ids.</returns>
    public static (Patch Patch, Dictionary<string, Guid> Named) Keep(
        Patch built,
        IReadOnlyDictionary<string, Guid> named,
        Patch was,
        IReadOnlyDictionary<string, NodeInstance> handles)
    {
        var ids = new Dictionary<Guid, Guid>();

        foreach (var (handle, id) in named)
        {
            if (!handles.TryGetValue(handle, out var old) || built.Find(id) is not { } node || node.TypeId != old.TypeId) continue;

            ids.TryAdd(id, old.Id);
        }

        if (built.FirstOf(NodeCatalog.OutputTypeId) is { } output && was.FirstOf(NodeCatalog.OutputTypeId) is { } before)
            ids.TryAdd(output.Id, before.Id);

        // An old id the rebuild already gave to some other module stays with that one.
        foreach (var (from, to) in ids.ToArray())
            if (from != to && built.Find(to) is not null && !ids.ContainsKey(to)) ids.Remove(from);

        var kept = named.ToDictionary(pair => pair.Key, pair => Id(pair.Value), StringComparer.OrdinalIgnoreCase);

        if (ids.All(pair => pair.Key == pair.Value)) return (built, kept);

        built.Nodes = [.. built.Nodes.Select(node => ids.TryGetValue(node.Id, out var id) ? node.Clone(id) : node)];
        built.Connections = [.. built.Connections.Select(wire => wire with { SourceNode = Id(wire.SourceNode), TargetNode = Id(wire.TargetNode) })];

        foreach (var group in built.Groups ?? [])
        {
            group.Members = [.. group.Members.Select(Id)];
            group.Exposed = [.. group.Exposed.Select(socket => socket with { Node = Id(socket.Node) })];
        }

        return (built, kept);

        Guid Id(Guid id) => ids.TryGetValue(id, out var old) ? old : id;
    }
}
