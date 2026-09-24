using System.Globalization;
using System.Text;
using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using static Flyback.Plugins.Assist.ToolArguments;

namespace Flyback.Plugins.Assist;

/// <summary>The lookup tools that answer from the catalog and the presets, never from the patch on the bench.</summary>
internal sealed class CatalogReference(ModuleCatalog modules, IReadOnlyList<PatchPreset> presets)
{
    public ToolOutcome DescribeModule(JsonElement arguments)
    {
        if (!Text(arguments, "type_id", out var typeId))
            return ToolOutcome.Refused("'type_id' is required and must be a string.");

        if (modules.Get(typeId) is null)
            return ToolOutcome.Refused($"there is no module with type id '{typeId}'. {Nearest(typeId)}");

        var text = new StringBuilder();
        Describe(text, typeId);
        return ToolOutcome.Fine(text.ToString());
    }

    public ToolOutcome DescribePreset(JsonElement arguments)
    {
        if (!Text(arguments, "name", out var name))
            return ToolOutcome.Refused("'name' is required and must be a string.");

        if (presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) is not { } preset)
        {
            return ToolOutcome.Refused(
                $"there is no preset called '{name}'. The presets are: {string.Join(", ", presets.Select(p => p.Name))}.");
        }

        Patch patch;

        try
        {
            patch = preset.Build(modules);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolOutcome.Refused($"'{preset.Name}' cannot be built here: {ex.Message}");
        }

        return ToolOutcome.Fine(
            $"{preset.Name}: {preset.Description}{Environment.NewLine}"
            + PatchPrinter.Print(patch, modules) + Environment.NewLine
            + $"{patch.Nodes.Count} modules, {patch.Connections.Count} wires.");
    }

    public ToolOutcome FindModules(JsonElement arguments)
    {
        if (!Text(arguments, "query", out var query))
            return ToolOutcome.Refused("'query' is required and must be a string.");

        var hits = modules.All
            .Where(d => d.TypeId.Contains(query, StringComparison.OrdinalIgnoreCase)
                || d.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || d.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                || d.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                || d.Inputs.Any(p => p.Help.Contains(query, StringComparison.OrdinalIgnoreCase))
                || d.Outputs.Any(p => p.Help.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .Take(30)
            .ToArray();

        if (hits.Length == 0) return ToolOutcome.Fine($"nothing matches '{query}'.");

        return ToolOutcome.Fine(string.Join(
            Environment.NewLine,
            hits.Select(d => $"{d.TypeId} | {d.Name} | {d.Category}")));
    }

    public string Nearest(string typeId)
    {
        var tail = typeId[(typeId.LastIndexOf('.') + 1)..];

        var close = modules.All
            .Where(d => d.TypeId.Contains(tail, StringComparison.OrdinalIgnoreCase)
                || d.Name.Contains(tail, StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .Select(d => d.TypeId)
            .ToArray();

        return close.Length == 0
            ? "Use find_modules, or read the module list again."
            : $"Did you mean: {string.Join(", ", close)}?";
    }

    public static string Sockets(NodeDef def) => $"Its ports: in {List(def.Inputs)}; out {List(def.Outputs)}.";

    public static string PortName(IReadOnlyList<PortSpec> ports, int index) =>
        index >= 0 && index < ports.Count ? ports[index].Name : index.ToString(CultureInfo.InvariantCulture);

    public static string List(IReadOnlyList<PortSpec> ports) =>
        ports.Count == 0
            ? "(none)"
            : string.Join(", ", ports.Select((p, i) => $"{i} {p.Name}"));

    private void Describe(StringBuilder text, string typeId)
    {
        var def = modules.Require(typeId);

        text.Append(def.TypeId).Append(" | ").Append(def.Name).Append(" | ").Append(def.Category);

        if (def.Sinks is not ModuleSinks.Both)
            text.Append(" | ").Append(def.Sinks is ModuleSinks.Audio ? "audio only" : "video only");

        text.AppendLine();

        for (var i = 0; i < def.Inputs.Count; i++)
        {
            var port = def.Inputs[i];
            text.Append("  in  ").Append(i).Append(' ').Append(port.Name)
                .Append(" = ").Append(port.Format(port.Default))
                .Append(" [").Append(Number(port.Min)).Append("..").Append(Number(port.Max)).Append(']')
                .Append(port.Kind == PortKind.Color ? " color" : port.Kind == PortKind.Any ? " any" : "");
            Helped(port.Help);
        }

        for (var i = 0; i < def.Outputs.Count; i++)
        {
            text.Append("  out ").Append(i).Append(' ').Append(def.Outputs[i].Name);
            Helped(def.Outputs[i].Help);
        }

        // What the module carries that is neither a socket nor a knob, which
        // the two loops above cannot show — the whole reason a model asking
        // about a Sequencer or a Quantiser would otherwise miss half of it.
        foreach (var extra in def.Extras)
        {
            text.AppendLine(Vocabulary.Announce(extra));

            foreach (var (name, help) in extra.Explained())
                if (help.Length > 0) text.Append("    ").Append(name).Append(" — ").AppendLine(help);
        }

        if (def.Description.Length > 0) text.AppendLine(def.Description);

        void Helped(string help) => text.AppendLine(help.Length > 0 ? $" — {help}" : string.Empty);
    }
}
