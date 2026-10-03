using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Flyback.Core.Graph;

/// <summary>
/// The sockets of one module that follow a knob, filed in
/// <see cref="NodeInstance.State"/> under <see cref="StateKey"/> by port index.
/// </summary>
/// <remarks>
/// Kept on the module so copying, pasting and deleting carry the links along. Not a
/// <see cref="NodeExtra"/>, because any module's sockets can be linked.
/// </remarks>
public static class ControlMap
{
    /// <summary>What the links are filed under in <see cref="NodeInstance.State"/>.</summary>
    public const string StateKey = "controls";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>The knob <paramref name="port"/> follows, or null where it rests on its own.</summary>
    public static ControlLink? Of(NodeInstance node, int port) =>
        node.StateOf(StateKey)?[port.ToString(CultureInfo.InvariantCulture)] is { } held
            ? Read(held)
            : null;

    /// <summary>Every linked socket on <paramref name="node"/>, by port.</summary>
    public static IEnumerable<(int Port, ControlLink Link)> All(NodeInstance node)
    {
        if (node.StateOf(StateKey) is not JsonObject links) yield break;

        foreach (var (key, held) in links)
        {
            if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var port)) continue;
            if (held is not null && Read(held) is { } link) yield return (port, link);
        }
    }

    /// <summary>Links <paramref name="port"/> to a knob, replacing whatever it followed.</summary>
    public static void Link(NodeInstance node, int port, ControlLink link)
    {
        var links = node.StateOf(StateKey) as JsonObject ?? new JsonObject();

        links[port.ToString(CultureInfo.InvariantCulture)] = JsonSerializer.SerializeToNode(link, Options);
        node.SetState(StateKey, links);
    }

    /// <summary>Lets <paramref name="port"/> go back to its own knob.</summary>
    public static bool Unlink(NodeInstance node, int port)
    {
        if (node.StateOf(StateKey) is not JsonObject links) return false;

        var went = links.Remove(port.ToString(CultureInfo.InvariantCulture));

        node.SetState(StateKey, links.Count == 0 ? null : links);
        return went;
    }

    /// <summary>Every socket in <paramref name="patch"/> following <paramref name="control"/>.</summary>
    public static IEnumerable<(NodeInstance Node, int Port, ControlLink Link)> Following(Patch patch, Guid control) =>
        from node in patch.Nodes
        from linked in All(node)
        where linked.Link.Control == control
        select (node, linked.Port, linked.Link);

    private static ControlLink? Read(JsonNode held)
    {
        try
        {
            var link = held.Deserialize<ControlLink>(Options);

            return link.Control == Guid.Empty || !float.IsFinite(link.Min) || !float.IsFinite(link.Max)
                ? null
                : link;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }
}