using System.Globalization;
using Flyback.Core.Graph;
using Flyback.Engine.Language;

namespace Flyback.Engine.Tutorials;

/// <summary>
/// Whether a patch holds a step's goal: a module of each kind the goal places, wired as the goal
/// wires them, with every knob the goal turns away from its default at the goal's value. Anything
/// else the patch holds is the person's own and is never held against them.
/// </summary>
internal static class StepCheck
{
    /// <summary>How far a knob may sit from the goal's value and still count, as a share of it.</summary>
    /// <remarks>A knob dragged by hand rarely lands exactly; two percent is closer than an ear or an eye tells apart.</remarks>
    private const float Near = 0.02f;

    /// <summary>The most ways of pairing the goal's modules with the patch's that are tried before settling for the best so far.</summary>
    private const int Tries = 20_000;

    /// <summary>Checks <paramref name="patch"/> against a goal written in the text language.</summary>
    /// <exception cref="FormatException">The goal does not build; shipped goals are checked by test.</exception>
    public static StepVerdict Against(Patch patch, string goal, ModuleCatalog catalog)
    {
        var load = PatchLanguage.Build(goal, catalog);

        if (load.Issues.Count > 0) throw new FormatException($"A tutorial goal does not build: {load.Report}");

        return Against(patch, load.Patch, catalog);
    }

    public static StepVerdict Against(Patch patch, Patch goal, ModuleCatalog catalog)
    {
        var wanted = goal.Nodes;

        foreach (var kind in wanted.GroupBy(n => n.TypeId))
        {
            var have = patch.Nodes.Count(n => n.TypeId == kind.Key && !n.Off);
            if (have >= kind.Count()) continue;

            var name = Name(catalog, kind.Key);
            return StepVerdict.Unmet(kind.Count() > 1 && have > 0 ? $"Add another {name}." : $"Add {Article(name)} {name}.");
        }

        var needs = Needs(goal, catalog);
        var candidates = wanted
            .Select(w => patch.Nodes.Where(n => n.TypeId == w.TypeId && !n.Off).ToArray())
            .ToArray();

        var pairing = new NodeInstance[wanted.Count];
        var used = new HashSet<Guid>();
        NodeInstance[]? best = null;
        var bestMet = -1;
        var tries = 0;

        if (Pair(0)) return StepVerdict.Met;

        var first = needs.First(n => !n.MetBy(patch, best!));
        return StepVerdict.Unmet(first.Hint(patch, best!, catalog));

        bool Pair(int index)
        {
            if (tries >= Tries) return false;

            if (index == wanted.Count)
            {
                tries++;
                var met = needs.Count(n => n.MetBy(patch, pairing));

                if (met > bestMet)
                {
                    bestMet = met;
                    best = [.. pairing];
                }

                return met == needs.Count;
            }

            foreach (var node in candidates[index])
            {
                if (!used.Add(node.Id)) continue;

                pairing[index] = node;
                if (Pair(index + 1)) return true;
                used.Remove(node.Id);
            }

            return false;
        }
    }

    /// <summary>Every wire the goal draws and every knob it turns, in the order the goal says them.</summary>
    private static IReadOnlyList<Need> Needs(Patch goal, ModuleCatalog catalog)
    {
        var index = goal.Nodes.Select((n, i) => (n.Id, i)).ToDictionary(p => p.Id, p => p.i);
        var needs = new List<Need>();

        foreach (var wire in goal.Connections)
            needs.Add(new WireNeed(index[wire.SourceNode], wire.SourcePort, index[wire.TargetNode], wire.TargetPort));

        for (var i = 0; i < goal.Nodes.Count; i++)
        {
            var node = goal.Nodes[i];
            if (catalog.Get(node.TypeId) is not { } def) continue;

            for (var port = 0; port < def.Inputs.Count && port < node.InputValues.Length; port++)
            {
                var spec = def.Inputs[port];
                if (spec.NeedsAWire || spec.NormalledTo is not null) continue;
                if (goal.IncomingTo(node.Id, port) is not null) continue;
                if (node.InputValues[port].Equals(spec.Default)) continue;

                needs.Add(new KnobNeed(i, port, node.InputValues[port]));
            }
        }

        return needs;
    }

    private static string Name(ModuleCatalog catalog, string typeId) => catalog.Get(typeId)?.Name ?? typeId;

    private static string Article(string name) => "AEIOU".Contains(char.ToUpperInvariant(name[0])) ? "an" : "a";

    /// <summary>"the Sine's", "the Rings'".</summary>
    private static string Whose(ModuleCatalog catalog, NodeInstance node)
    {
        var name = Name(catalog, node.TypeId);

        return name.EndsWith('s') ? $"the {name}'" : $"the {name}'s";
    }

    private abstract record Need
    {
        public abstract bool MetBy(Patch patch, NodeInstance[] pairing);

        public abstract string Hint(Patch patch, NodeInstance[] pairing, ModuleCatalog catalog);
    }

    private sealed record WireNeed(int Source, int SourcePort, int Target, int TargetPort) : Need
    {
        public override bool MetBy(Patch patch, NodeInstance[] pairing) =>
            patch.IncomingTo(pairing[Target].Id, TargetPort) is { } wire
            && wire.SourceNode == pairing[Source].Id
            && wire.SourcePort == SourcePort;

        public override string Hint(Patch patch, NodeInstance[] pairing, ModuleCatalog catalog)
        {
            var from = pairing[Source];
            var to = pairing[Target];
            var output = catalog.Get(from.TypeId)?.Outputs[SourcePort].Name ?? "out";
            var input = catalog.Get(to.TypeId)?.Inputs[TargetPort].Name ?? "in";

            return $"Wire {Whose(catalog, from)} {output} into {Whose(catalog, to)} {input}.";
        }
    }

    private sealed record KnobNeed(int Node, int Port, float Value) : Need
    {
        public override bool MetBy(Patch patch, NodeInstance[] pairing)
        {
            var node = pairing[Node];

            return patch.IncomingTo(node.Id, Port) is null
                && Port < node.InputValues.Length
                && MathF.Abs(node.InputValues[Port] - Value) <= Near * MathF.Max(MathF.Abs(Value), 1f);
        }

        public override string Hint(Patch patch, NodeInstance[] pairing, ModuleCatalog catalog)
        {
            var node = pairing[Node];
            var spec = catalog.Get(node.TypeId)!.Inputs[Port];
            var wanted = spec.Format(Value);

            if (patch.IncomingTo(node.Id, Port) is not null)
                return $"Take the wire out of {Whose(catalog, node)} {spec.Name}, then turn it to {wanted}.";

            var now = Port < node.InputValues.Length ? spec.Format(node.InputValues[Port]) : "unset";

            return string.Create(CultureInfo.InvariantCulture, $"Turn {Whose(catalog, node)} {spec.Name} to {wanted}; it is at {now}.");
        }
    }
}
