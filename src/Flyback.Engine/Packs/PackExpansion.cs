using Flyback.Core.Graph;

namespace Flyback.Engine.Packs;

/// <summary>
/// A patch with each placed pack module swapped for a copy of the modules it is made of,
/// wired where its sockets were, so the compiler only ever sees built-in and plugin modules.
/// </summary>
internal static class PackExpansion
{
    /// <summary><paramref name="patch"/> itself where it holds no pack module, else an expanded copy; the original is never changed.</summary>
    public static Patch Expand(Patch patch, ModuleCatalog catalog)
    {
        var placed = patch.Nodes
            .Select(node => (Node: node, Def: catalog.Get(node.TypeId)))
            .Where(p => p.Def is not null && PackModules.StencilOf(p.Def) is not null)
            .ToList();

        if (placed.Count == 0) return patch;

        var nodes = patch.Nodes.ToList();
        var wires = patch.Connections.ToList();

        foreach (var (node, def) in placed) Swap(node, def!, PackModules.StencilOf(def!)!, nodes, wires, catalog);

        return patch.With(nodes, wires);
    }

    private static void Swap(NodeInstance node, NodeDef def, PackStencil stencil, List<NodeInstance> nodes, List<Connection> wires, ModuleCatalog catalog)
    {
        var feeding = wires.Where(w => w.TargetNode == node.Id).ToDictionary(w => w.TargetPort, w => (w.SourceNode, w.SourcePort));
        var outputs = new (Guid Node, int Port)?[def.Outputs.Count];

        nodes.Remove(node);
        wires.RemoveAll(w => w.TargetNode == node.Id);

        if (node.Off)
        {
            for (var output = 0; output < outputs.Length; output++)
                outputs[output] = feeding.TryGetValue(def.Through(output), out var through) ? through : Silent(output);
        }
        else
        {
            Copy(node, stencil, feeding, outputs, nodes, wires, catalog);
        }

        var leaving = wires.Where(w => w.SourceNode == node.Id).ToList();

        foreach (var wire in leaving)
        {
            wires.Remove(wire);

            if (outputs[wire.SourcePort] is { } from)
                wires.Add(new Connection(from.Node, from.Port, wire.TargetNode, wire.TargetPort));
        }

        // A module switched off with nothing to hand on gives nought.
        (Guid, int) Silent(int output)
        {
            var held = NodeInstance.Create(catalog.Require(SourceModules.ValueTypeId), node.X, node.Y, Inside(node.Id, new Guid(output, 0, 0, new byte[8])));
            held.InputValues[0] = 0f;
            nodes.Add(held);
            return (held.Id, 0);
        }
    }

    private static void Copy(
        NodeInstance node,
        PackStencil stencil,
        Dictionary<int, (Guid Node, int Port)> feeding,
        (Guid Node, int Port)?[] outputs,
        List<NodeInstance> nodes,
        List<Connection> wires,
        ModuleCatalog catalog)
    {
        var sockets = stencil.Sockets.Select((id, i) => (id, i)).ToDictionary(p => p.id, p => p.i);
        var results = stencil.Results.Select((id, i) => (id, i)).ToDictionary(p => p.id, p => p.i);
        var copies = new Dictionary<Guid, Guid>();

        foreach (var inner in stencil.Inner.Nodes)
        {
            if (sockets.ContainsKey(inner.Id) || results.ContainsKey(inner.Id)) continue;

            var copy = inner.Clone(Inside(node.Id, inner.Id));
            copy.X = node.X;
            copy.Y = node.Y;
            nodes.Add(copy);
            copies[inner.Id] = copy.Id;
        }

        foreach (var wire in stencil.Inner.Connections)
        {
            if (results.TryGetValue(wire.TargetNode, out var output))
            {
                outputs[output] = sockets.TryGetValue(wire.SourceNode, out var straight)
                    ? Fed(straight)
                    : (copies[wire.SourceNode], wire.SourcePort);
                continue;
            }

            var target = copies[wire.TargetNode];

            if (!sockets.TryGetValue(wire.SourceNode, out var input))
            {
                wires.Add(new Connection(copies[wire.SourceNode], wire.SourcePort, target, wire.TargetPort));
            }
            else if (feeding.TryGetValue(input, out var source))
            {
                wires.Add(new Connection(source.Node, source.Port, target, wire.TargetPort));
            }
            else
            {
                nodes.Single(n => n.Id == target).InputValues[wire.TargetPort] = node.InputValues[input];
            }
        }

        // A result nothing wires is the number its body ended with.
        for (var output = 0; output < outputs.Length; output++)
        {
            if (outputs[output] is not null) continue;

            var result = stencil.Inner.Nodes.Single(n => n.Id == stencil.Results[output]);
            var held = NodeInstance.Create(catalog.Require(SourceModules.ValueTypeId), node.X, node.Y, Inside(node.Id, result.Id));
            held.InputValues[0] = result.InputValues[0];
            nodes.Add(held);
            outputs[output] = (held.Id, 0);
        }

        // An output wired straight from an input is what feeds that input, or the number it holds.
        (Guid Node, int Port)? Fed(int input)
        {
            if (feeding.TryGetValue(input, out var source)) return source;

            var held = NodeInstance.Create(catalog.Require(SourceModules.ValueTypeId), node.X, node.Y, Inside(node.Id, stencil.Sockets[input]));
            held.InputValues[0] = node.InputValues[input];
            nodes.Add(held);
            return (held.Id, 0);
        }
    }

    /// <summary>The identity of one module inside a placed one, the same at every compile so what it remembers carries over.</summary>
    private static Guid Inside(Guid placed, Guid inner)
    {
        Span<byte> bytes = stackalloc byte[16];
        placed.TryWriteBytes(bytes);
        var (a, b) = (BitConverter.ToUInt64(bytes[..8]), BitConverter.ToUInt64(bytes[8..]));
        inner.TryWriteBytes(bytes);
        var (c, d) = (BitConverter.ToUInt64(bytes[..8]), BitConverter.ToUInt64(bytes[8..]));

        var high = Mix(a ^ Mix(c ^ 0x9E3779B97F4A7C15UL));
        var low = Mix(b ^ Mix(d ^ high));

        BitConverter.TryWriteBytes(bytes[..8], high);
        BitConverter.TryWriteBytes(bytes[8..], low);
        return new Guid(bytes);
    }

    /// <summary>SplitMix64's finalizer: every bit of the input moves every bit of the output.</summary>
    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
