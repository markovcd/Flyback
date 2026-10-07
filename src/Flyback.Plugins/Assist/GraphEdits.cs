using System.Text.Json;
using Flyback.Core.Graph;
using static Flyback.Plugins.Assist.ToolArguments;

namespace Flyback.Plugins.Assist;

/// <summary>The tools that change how modules are wired, switch one off or take one away.</summary>
internal sealed class GraphEdits(WorkingPatch bench, PatchReports reports)
{
    public ToolOutcome Connect(JsonElement arguments)
    {
        if (!bench.Node(arguments, "from", out var source, out var sourceDef, out var refusal))
            return ToolOutcome.Refused(refusal);

        if (!bench.Node(arguments, "to", out var target, out var targetDef, out refusal))
            return ToolOutcome.Refused(refusal);

        if (source.Id == target.Id)
            return ToolOutcome.Refused(
                "a module cannot be wired to itself — a pixel cannot depend on itself. "
                + "Use a 'feedback' module to read the previous frame.");

        int fromPort;

        if (Text(arguments, "from_port", out var fromName))
        {
            if (!Port(sourceDef.Outputs, fromName, out fromPort))
                return ToolOutcome.Refused(
                    $"{bench.Handle(source)} has no output called '{fromName}'. Its outputs are: {CatalogReference.List(sourceDef.Outputs)}.");
        }
        else if (sourceDef.Outputs.Count == 1)
        {
            fromPort = 0;
        }
        else
        {
            return ToolOutcome.Refused(
                $"{bench.Handle(source)} has more than one output, so 'from_port' is needed. "
                + $"Its outputs are: {CatalogReference.List(sourceDef.Outputs)}.");
        }

        if (!Text(arguments, "to_port", out var toName))
            return ToolOutcome.Refused("'to_port' is required and must be a string.");

        if (!Port(targetDef.Inputs, toName, out var toPort))
            return ToolOutcome.Refused(
                $"{bench.Handle(target)} has no input called '{toName}'. Its inputs are: {CatalogReference.List(targetDef.Inputs)}.");

        var replaced = bench.Patch.IncomingTo(target.Id, toPort) is { } existing
            ? $" (replacing {bench.Handle(bench.Patch.Find(existing.SourceNode))}.{Name(existing, sourceOf: true)})"
            : string.Empty;

        bench.Patch.Connect(source.Id, fromPort, target.Id, toPort);
        bench.Edits++;

        return ToolOutcome.Fine(
            $"wired {bench.Handle(source)}.{sourceDef.Outputs[fromPort].Name} -> "
            + $"{bench.Handle(target)}.{targetDef.Inputs[toPort].Name}{replaced}. {reports.Issues()}");
    }

    public ToolOutcome Disconnect(JsonElement arguments)
    {
        if (!bench.Node(arguments, "handle", out var node, out var def, out var refusal))
            return ToolOutcome.Refused(refusal);

        if (!Text(arguments, "port", out var portName))
            return ToolOutcome.Refused("'port' is required and must be a string.");

        if (!Port(def.Inputs, portName, out var port))
            return ToolOutcome.Refused(
                $"{bench.Handle(node)} has no input called '{portName}'. Its inputs are: {CatalogReference.List(def.Inputs)}.");

        // What a socket falls back to when nothing is patched into it: the module
        // it is normalled to where there is one, and its knob otherwise. Both
        // messages below need it, and neither is worth being wrong about — an
        // assistant told a socket is on a knob will go looking for the knob.
        var resting = bench.Modules.Normalled(def.Inputs[port]) is { } source
            ? $"{source}, which it is normalled to"
            : $"its knob at {def.Inputs[port].Format(Knob(node, port, def))}";

        if (bench.Patch.IncomingTo(node.Id, port) is null)
            return ToolOutcome.Fine($"nothing was wired to {bench.Handle(node)}.{def.Inputs[port].Name}; it is on {resting}.");

        bench.Patch.Disconnect(node.Id, port);
        bench.Edits++;

        return ToolOutcome.Fine(
            $"unwired {bench.Handle(node)}.{def.Inputs[port].Name}, which is back on {resting}. {reports.Issues()}");
    }

    /// <summary>
    /// Switches a module off, or back on — see <see cref="NodeInstance.Off"/>.
    /// </summary>
    public ToolOutcome SwitchModule(JsonElement arguments)
    {
        if (!bench.Node(arguments, "handle", out var node, out var def, out var refusal))
            return ToolOutcome.Refused(refusal);

        if (NodeCatalog.IsSink(node.TypeId))
        {
            return ToolOutcome.Refused(
                "the Output cannot be switched off. Set its 'volume' to 0, or switch off what is wired into it.");
        }

        var off = Flag(arguments, "off", fallback: true);

        node.Off = off;
        bench.Edits++;

        if (!off) return ToolOutcome.Fine($"switched {bench.Handle(node)} back on. {reports.Issues()}");

        var through = def.Through(0);

        var handing = through < 0 || bench.Patch.IncomingTo(node.Id, through) is null
            ? "Nothing is patched into it, so it hands on nothing and whatever it fed is back on its own knob."
            : $"It hands on what is patched into its '{def.Inputs[through].Name}'.";

        return ToolOutcome.Fine($"switched {bench.Handle(node)} off. {handing} {reports.Issues()}");
    }

    public ToolOutcome RemoveModule(JsonElement arguments)
    {
        if (!bench.Node(arguments, "handle", out var node, out _, out var refusal))
            return ToolOutcome.Refused(refusal);

        var handle = bench.Handle(node);
        var lost = bench.Patch.Connections.Count(c => c.SourceNode == node.Id || c.TargetNode == node.Id);

        if (!bench.Remove(node))
            return ToolOutcome.Refused($"{handle} is the patch's Output and stays. Disconnect what feeds it instead.");

        bench.Edits++;

        var wires = lost switch
        {
            0 => "It was wired to nothing.",
            1 => "One wire went with it.",
            _ => $"{lost} wires went with it.",
        };

        return ToolOutcome.Fine($"removed {handle}. {wires} {reports.Issues()}");
    }

    private static float Knob(NodeInstance node, int port, NodeDef def) =>
        port < node.InputValues.Length ? node.InputValues[port] : def.Inputs[port].Default;

    private string Name(Connection wire, bool sourceOf)
    {
        if (bench.Patch.Find(sourceOf ? wire.SourceNode : wire.TargetNode) is not { } node) return "?";
        if (bench.Modules.Get(node.TypeId) is not { } def) return "?";

        return sourceOf ? CatalogReference.PortName(def.Outputs, wire.SourcePort) : CatalogReference.PortName(def.Inputs, wire.TargetPort);
    }
}
