using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Flyback.Core.Graph;

/// <summary>The hardware controller a knob follows.</summary>
/// <param name="Device">The device's stable id, the same string a MIDI In stores.</param>
/// <param name="Channel">1 to 16, or 0 for whichever channel it arrives on.</param>
/// <param name="Controller">The controller number, 0 to 127.</param>
public sealed record MidiBinding(string Device, int Channel, int Controller)
{
    /// <summary>What a panel shows under a knob: <c>CC21</c>, or <c>CC21·2</c> on a channel.</summary>
    public string Label => Channel == 0
        ? $"CC{Controller}"
        : $"CC{Controller}·{Channel}";

    /// <summary>Whether a controller moving on <paramref name="channel"/> is this one.</summary>
    public bool Hears(string device, int channel, int controller) =>
        Controller == controller
        && (Channel == 0 || Channel == channel)
        && string.Equals(Device, device, StringComparison.Ordinal);
}

/// <summary>
/// A knob on the patch's control panel, which any number of sockets can follow
/// through <see cref="ControlMap"/>.
/// </summary>
/// <remarks>
/// A hardware controller is bound to a knob, never to a socket, so learning a
/// controller and linking a knob are the same thing. A linked socket reads the knob
/// as a live value under <see cref="KeyOf"/>, so turning it recompiles nothing
/// (ADR-0086).
/// </remarks>
public sealed class PatchControl
{
    /// <inheritdoc cref="NodeInstance.NameLimit"/>
    public const int NameLimit = NodeInstance.NameLimit;

    public required Guid Id { get; init; }

    /// <summary>What the panel calls it.</summary>
    public string Name
    {
        get;
        set => field = Named(value);
    } = "Knob";

    /// <summary>Where it rests, 0 to 1: what a saved patch opens at and a fresh live block is seeded with.</summary>
    public float Value
    {
        get;
        set => field = float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;
    }

    /// <summary>The hardware controller it follows, or null for a knob only turned on screen.</summary>
    public MidiBinding? Midi { get; set; }

    /// <summary>What a program reading this knob calls it in <c>CompiledPatch.LiveInputs</c>.</summary>
    public static string KeyOf(Guid control) => $"control/{control:N}";

    /// <inheritdoc cref="KeyOf(Guid)"/>
    public string Key => KeyOf(Id);

    public PatchControl Clone() => new() { Id = Id, Name = Name, Value = Value, Midi = Midi };

    private static string Named(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;

        if (trimmed.Length > NameLimit) trimmed = trimmed[..NameLimit].TrimEnd();

        return trimmed.Length == 0 ? "Knob" : trimmed;
    }
}

/// <summary>Which knob a socket follows, and over what range.</summary>
/// <param name="Control">The <see cref="PatchControl.Id"/> it follows.</param>
/// <param name="Min">What the socket reads with the knob all the way down.</param>
/// <param name="Max">What it reads all the way up; below <paramref name="Min"/> turns the knob round.</param>
public readonly record struct ControlLink(Guid Control, float Min, float Max)
{
    /// <summary>What the socket reads with the knob at <paramref name="value"/>.</summary>
    public float At(float value) => Min + value * (Max - Min);

    /// <summary>Where the knob has to sit for the socket to read <paramref name="reading"/>, held to 0..1.</summary>
    public float Inverse(float reading) =>
        Max == Min ? 0f : Math.Clamp((reading - Min) / (Max - Min), 0f, 1f);
}

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
