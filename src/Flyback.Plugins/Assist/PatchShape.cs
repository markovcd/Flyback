using Flyback.Core.Graph;

namespace Flyback.Plugins.Assist;

/// <summary>
/// Which modules a patch has and how they are wired, and nothing else. Two patches
/// of one shape are the same patch however their knobs are set.
/// </summary>
internal sealed class PatchShape
{
    private readonly HashSet<(Guid, string)> modules;
    private readonly HashSet<Connection> wires;

    private PatchShape(Patch patch)
    {
        modules = [.. Modules(patch)];
        wires = [.. patch.Connections];
    }

    public static PatchShape Of(Patch patch) => new(patch);

    public bool Matches(Patch patch) =>
        modules.SetEquals(Modules(patch)) && wires.SetEquals(patch.Connections);

    private static IEnumerable<(Guid, string)> Modules(Patch patch) =>
        patch.Nodes.Select(node => (node.Id, node.TypeId));
}
