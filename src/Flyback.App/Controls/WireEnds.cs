using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>What the inspector says is at the other end of a module's wires.</summary>
internal static class WireEnds
{
    /// <summary>A socket as <c>module.socket</c>, the module by the title the canvas draws.</summary>
    internal static string Name(Patch patch, Guid node, int port, bool output)
    {
        if (patch.Find(node) is not { } found || NodeCatalog.Get(found.TypeId) is not { } def) return "?";

        var ports = output ? def.Outputs : def.Inputs;

        return $"{found.Title(def)}.{(port < ports.Count ? ports[port].Name : port.ToString())}";
    }

    /// <summary>An input's row beside its name, or null where no wire arrives.</summary>
    internal static string? Into(Patch patch, Guid node, int port) =>
        patch.IncomingTo(node, port) is { } wire
            ? $"◀ patched from {Name(patch, wire.SourceNode, wire.SourcePort, output: true)}"
            : null;

    /// <summary>An output's row beside its name, every socket it feeds in the order wired, or null where it feeds none.</summary>
    internal static string? OutOf(Patch patch, Guid node, int port)
    {
        var to = patch.Connections
            .Where(c => c.SourceNode == node && c.SourcePort == port)
            .Select(c => Name(patch, c.TargetNode, c.TargetPort, output: false))
            .ToList();

        return to.Count == 0 ? null : $"▶ patched to {string.Join(", ", to)}";
    }
}
