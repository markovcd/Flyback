using System.Globalization;
using Flyback.Core.Graph;

namespace Flyback.Engine.Language;

/// <summary>The bindings and names a writer needs before it emits patch text.</summary>
internal sealed class PatchPrintPlan(
    Dictionary<Guid, string> names,
    HashSet<Guid> bound,
    HashSet<string> taken,
    Guid coord,
    Guid clock,
    Dictionary<Guid, string> panel,
    Dictionary<Guid, string?> boxes)
{
    internal Dictionary<Guid, string> Names { get; } = names;
    internal HashSet<Guid> Bound { get; } = bound;
    internal HashSet<string> Taken { get; } = taken;
    internal Guid Coord { get; } = coord;
    internal Guid Clock { get; } = clock;
    internal Dictionary<Guid, string> Panel { get; } = panel;
    internal Dictionary<Guid, string?> Boxes { get; } = boxes;

    internal static PatchPrintPlan Create(
        Patch patch,
        ModuleCatalog modules,
        IReadOnlyDictionary<Guid, string>? called,
        IReadOnlyCollection<string>? used = null)
    {
        var bound = new HashSet<Guid>();
        var taken = new HashSet<string>(used ?? [], StringComparer.Ordinal);

        // Panel words win collisions, because modules following those knobs
        // need to use the same word.
        var panel = new Dictionary<Guid, string>();
        var moduleNames = new ModuleNames(modules);

        foreach (var control in patch.Controls ?? [])
        {
            var word = PatchPrinter.Usable(control.Word) ? control.Word!
                : PatchPrinter.Usable(control.Name) ? control.Name
                : PatchPrinter.Spelled(control.Name);

            panel[control.Id] = Unique(moduleNames.Knows(word) ? word + "_knob" : word, taken);
        }

        var names = new Dictionary<Guid, string>();

        // Only one ungrouped, switched-on Coordinates and Time can be written
        // as the bare words x/y and t.
        var coord = patch.Nodes.FirstOrDefault(n =>
            n.TypeId == NodeCatalog.CoordTypeId && !n.Off && patch.GroupOf(n.Id) is null)?.Id ?? Guid.Empty;
        var clock = patch.Nodes.FirstOrDefault(n =>
            n.TypeId == NodeCatalog.TimeTypeId && !n.Off && patch.GroupOf(n.Id) is null)?.Id ?? Guid.Empty;

        // Backwards wires need a name so they can be written as back-wires.
        var looped = Cycles.Backwards(patch).Select(wire => wire.TargetNode).ToHashSet();

        foreach (var node in patch.Nodes)
        {
            if (node.Id == coord || node.Id == clock) continue;
            if (NodeCatalog.IsSink(node.TypeId)) continue;

            var leaving = patch.Connections.Where(c => c.SourceNode == node.Id).ToList();
            var home = patch.GroupOf(node.Id)?.Id;

            // A chain stops at a group boundary, so a crossing wire needs a name.
            var crossing = leaving.Any(c =>
                patch.GroupOf(c.TargetNode)?.Id != home
                && patch.Find(c.TargetNode) is { } target
                && !NodeCatalog.IsSink(target.TypeId));

            var must = called is not null
                || crossing
                || looped.Contains(node.Id)
                || leaving.Count != 1
                || leaving.Any(c => c.SourcePort != 0)
                || node.Off
                || PatchPrinter.Usable(node.Name);

            if (must) bound.Add(node.Id);
        }

        // Plain arithmetic is named after its destination, which must already
        // have a name before the role can be chosen.
        foreach (var node in patch.Nodes.Where(n => bound.Contains(n.Id)).OrderBy(n => Plain(n) ? 1 : 0))
        {
            var wanted = called is not null && called.TryGetValue(node.Id, out var given) && PatchPrinter.Usable(given)
                ? given
                : Role(patch, node, modules, names) ?? Wanted(node, modules);

            names[node.Id] = Unique(wanted, taken);
        }

        // Equal group labels would parse as one group, so make each one distinct.
        var boxes = new Dictionary<Guid, string?>();
        var labels = new HashSet<string>(used ?? [], StringComparer.Ordinal);

        foreach (var group in patch.Groups ?? [])
        {
            if (group.Name is not { } label || PatchPrinter.Quotable(label) is null)
            {
                boxes[group.Id] = null;
                continue;
            }

            var unique = label;

            for (var n = 2; !labels.Add(unique); n++) unique = $"{label} {n}";

            boxes[group.Id] = unique;
        }

        return new PatchPrintPlan(names, bound, taken, coord, clock, panel, boxes);
    }

    /// <summary>
    /// What to call a module: its chosen name, its short source name, or its
    /// palette label when the short name is reserved by the language.
    /// </summary>
    internal bool Name(Patch patch, NodeInstance node, ModuleCatalog modules)
    {
        if (!Bound.Add(node.Id)) return false;

        Names[node.Id] = Unique(Role(patch, node, modules, Names) ?? Wanted(node, modules), Taken);
        return true;
    }

    private static string Wanted(NodeInstance node, ModuleCatalog modules)
    {
        if (PatchPrinter.Usable(node.Name)) return node.Name!;

        var dot = node.TypeId.LastIndexOf('.');
        var stem = dot < 0 ? node.TypeId : node.TypeId[(dot + 1)..];

        if (PatchPrinter.Usable(stem)) return stem;

        if (modules.Get(node.TypeId) is { } def)
        {
            var label = new string([.. def.Name.ToLowerInvariant()
                .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_')]);

            if (PatchPrinter.Usable(label)) return label;
        }

        return "node";
    }

    private static bool Plain(NodeInstance node) =>
        node.TypeId.StartsWith("math.", StringComparison.Ordinal)
        && node.TypeId is not (NodeCatalog.MixerTypeId or NodeCatalog.DeskTypeId);

    /// <summary>
    /// Names an unnamed arithmetic module after the one socket it feeds, where
    /// that socket and destination describe its role unambiguously.
    /// </summary>
    private static string? Role(
        Patch patch,
        NodeInstance node,
        ModuleCatalog modules,
        IReadOnlyDictionary<Guid, string> names)
    {
        if (!Plain(node) || PatchPrinter.Usable(node.Name)) return null;

        var places = new HashSet<(string Target, string Socket)>();

        foreach (var wire in patch.Connections.Where(c => c.SourceNode == node.Id))
        {
            if (patch.Find(wire.TargetNode) is not { } target || Plain(target)) return null;
            if (modules.Get(target.TypeId) is not { } def || wire.TargetPort >= def.Inputs.Count) return null;

            var where = NodeCatalog.IsSink(target.TypeId)
                ? "out"
                : names.TryGetValue(target.Id, out var named) ? named : Wanted(target, modules);

            places.Add((where, def.Inputs[wire.TargetPort].Name.Replace(' ', '_').ToLowerInvariant()));
        }

        if (places.Select(place => place.Socket).Distinct().Count() != 1) return null;

        var socket = places.First().Socket;
        var targets = places.Select(place => place.Target).Distinct().ToList();
        var word = targets.Count == 1 && targets[0] != socket ? $"{targets[0]}_{socket}" : socket;

        return PatchPrinter.Usable(word) ? word : null;
    }

    /// <summary>Chooses a distinct language name, keeping suffixes unambiguous.</summary>
    private static string Unique(string wanted, HashSet<string> taken)
    {
        if (taken.Add(wanted)) return wanted;

        // left_1's second is left_1_2, never left_12.
        var apart = char.IsAsciiDigit(wanted[^1]) ? wanted + "_" : wanted;

        for (var n = 2; ; n++)
        {
            var tried = apart + n.ToString(CultureInfo.InvariantCulture);

            if (taken.Add(tried)) return tried;
        }
    }
}
