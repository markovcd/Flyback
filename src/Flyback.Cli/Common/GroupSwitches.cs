using Flyback.Core;
using Flyback.Core.Graph;

namespace Flyback.Cli.Common;

/// <summary>
/// Switches modules off for a run by naming groups: a muted group is off, and a soloed
/// one leaves on only what feeds it and what carries it to the Output (ADR-0117).
/// </summary>
internal static class GroupSwitches
{
    /// <summary>
    /// Switches the patch's modules off as asked. Says why and returns false for a name that is
    /// no group's, or is two groups'. The patch is changed, so it is one the caller will not save.
    /// </summary>
    public static bool Apply(Patch patch, IReadOnlyList<string> mute, IReadOnlyList<string> solo, TextWriter error)
    {
        if (Resolve(patch, solo, error) is not { } soloed || Resolve(patch, mute, error) is not { } muted) return false;

        if (soloed.Count > 0)
        {
            var keep = Kept(patch, soloed);

            foreach (var node in patch.Nodes.Where(n => !keep.Contains(n.Id) && !NodeCatalog.IsSink(n.TypeId)))
                node.Off = true;
        }

        foreach (var node in patch.Nodes.Where(n => muted.Contains(n.Id) && !NodeCatalog.IsSink(n.TypeId)))
            node.Off = true;

        return true;
    }

    private static HashSet<Guid>? Resolve(Patch patch, IReadOnlyList<string> names, TextWriter error)
    {
        var members = new HashSet<Guid>();

        foreach (var name in names)
        {
            var named = (patch.Groups ?? []).Where(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();

            if (named.Length == 1)
            {
                members.UnionWith(named[0].Members);
                continue;
            }

            var known = string.Join(", ", (patch.Groups ?? []).Select(g => g.Name).OfType<string>());

            error.WriteLine(
                $"{GlobalConstants.ApplicationName}: {(named.Length == 0 ? "no group is called" : "more than one group is called")} '{name}'. "
                + (known.Length == 0 ? "The patch has no named groups." : $"The named groups are: {known}."));

            return null;
        }

        return members;
    }

    /// <summary>What a solo leaves on: the members, all that feeds them, and what carries them on to the Output.</summary>
    private static HashSet<Guid> Kept(Patch patch, HashSet<Guid> members)
    {
        var feeds = patch.Connections.Select(c => (From: c.SourceNode, To: c.TargetNode)).ToList();

        // A Send is heard wherever a Receive on its bus is.
        foreach (var send in patch.Nodes.Where(n => n.TypeId == NodeCatalog.SendTypeId))
        {
            foreach (var receive in patch.Nodes.Where(n => n.TypeId == NodeCatalog.ReceiveTypeId
                && string.Equals(NodeCatalog.BusOf(n), NodeCatalog.BusOf(send), StringComparison.OrdinalIgnoreCase)))
                feeds.Add((send.Id, receive.Id));
        }

        var upstream = Reach(members, feeds.Select(f => (f.To, f.From)));
        var downstream = Reach(members, feeds);
        var sinks = patch.Nodes.Where(n => NodeCatalog.IsSink(n.TypeId)).Select(n => n.Id);
        var toOutput = Reach(sinks, feeds.Select(f => (f.To, f.From)));

        downstream.IntersectWith(toOutput);
        upstream.UnionWith(downstream);

        return upstream;
    }

    /// <summary>Every module reached from <paramref name="start"/> along <paramref name="edges"/>, the start included.</summary>
    private static HashSet<Guid> Reach(IEnumerable<Guid> start, IEnumerable<(Guid From, Guid To)> edges)
    {
        var next = edges.ToLookup(e => e.From, e => e.To);
        var seen = new HashSet<Guid>(start);
        var pending = new Stack<Guid>(seen);

        while (pending.TryPop(out var node))
        {
            foreach (var to in next[node])
            {
                if (seen.Add(to)) pending.Push(to);
            }
        }

        return seen;
    }
}
