using System.Globalization;
using Avalonia;
using Flyback.Core.Graph;

namespace Flyback.App.Canvas;

/// <summary>
/// What a socket's tooltip says on a compact module (<see cref="NodeGeometry.Compact"/>):
/// what an unwired input rests at, then the socket's help.
/// </summary>
internal static class SocketTips
{
    /// <summary>The whole tooltip for one socket: where it rests, if it is an input, then its help.</summary>
    public static string? Say(Patch patch, NodeInstance node, NodeDef def, int port, bool isOutput)
    {
        var spec = isOutput ? def.Outputs[port] : def.Inputs[port];

        return Lines(isOutput ? null : Resting(patch, node, spec, port), spec.Help);
    }

    /// <summary>
    /// What an unwired input rests at: its value, the panel knob it follows, or what
    /// it is normalled from. Null for a wired input and for a knob its formula never reads.
    /// </summary>
    public static string? Resting(Patch patch, NodeInstance node, PortSpec port, int i)
    {
        if (patch.IncomingTo(node.Id, i) is not null) return null;

        if (ControlMap.Of(node, i) is { } found && patch.Control(found.Control) is { } control)
            return $"{port.Format(found.At(control.Value))}, following {control.Name}";

        if (NodeCatalog.Normalled(port) is { } source) return $"Normalled from {source}";

        if (i >= node.InputValues.Length) return null;
        if (NodeCatalog.FormulaOf(node) is { } formula && !FormulaLayout.Reads(formula, i)) return null;

        var spans = node.TypeId == NodeCatalog.AutoRemapTypeId ? AutoRemap.Of(patch, node) : null;

        return RemapValue(spans, i, node.InputValues[i]) switch
        {
            (var said, true) => $"{said}, with no range at the far end",
            (var said, false) => said,
            null => port.Format(node.InputValues[i]),
        };
    }

    /// <summary>
    /// What an Auto remap's range knob comes to at the far end of its wire, or, flagged,
    /// its plain number where that end has no range; null for every other socket.
    /// </summary>
    public static (string, bool)? RemapValue(RemapSpans? spans, int port, float value)
    {
        if (spans is null || port == AutoRemap.In) return null;

        return (port is AutoRemap.InLow or AutoRemap.InHigh ? spans.In : spans.Out) is { } span
            ? (span.Format(value), false)
            : (value.ToString("0.###", CultureInfo.InvariantCulture), true);
    }

    /// <summary>The socket under <paramref name="graph"/> on a compact module, by the row and the half of it.</summary>
    public static bool RowAt(Point graph, Rect bounds, NodeDef def, out int port, out bool isOutput)
    {
        port = (int)Math.Floor((graph.Y - bounds.Y - NodeGeometry.HeaderHeight) / NodeGeometry.RowHeight);
        isOutput = graph.X >= bounds.Center.X;

        return port >= 0 && port < (isOutput ? def.Outputs.Count : def.Inputs.Count);
    }

    /// <summary>The lines given, skipping the empty ones; null when none is left.</summary>
    public static string? Lines(params string?[] lines) =>
        string.Join("\n", lines.Where(l => !string.IsNullOrWhiteSpace(l))) is { Length: > 0 } joined ? joined : null;
}
