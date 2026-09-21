using System.Globalization;
using System.Text.Json;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Assist;

/// <summary>Reads what a tool call was sent, and writes a number back the way every tool answers one.</summary>
internal static class ToolArguments
{
    public static bool Text(JsonElement arguments, string field, out string value)
    {
        if (arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty(field, out var found)
            && found.ValueKind == JsonValueKind.String
            && found.GetString() is { Length: > 0 } text)
        {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <summary>
    /// A true-or-false argument, and <paramref name="fallback"/> where it was not
    /// sent — a switch nobody threw is one the caller had no opinion about.
    /// </summary>
    public static bool Flag(JsonElement arguments, string field, bool fallback) =>
        arguments.ValueKind == JsonValueKind.Object
        && arguments.TryGetProperty(field, out var found)
        && found.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? found.GetBoolean()
            : fallback;

    public static bool Real(JsonElement element, string name, out float value)
    {
        value = 0f;

        return element.TryGetProperty(name, out var found)
            && found.ValueKind == JsonValueKind.Number
            && found.TryGetSingle(out value);
    }

    public static bool Port(IReadOnlyList<PortSpec> ports, string name, out int index)
    {
        // An index is accepted as well as a name, which is the way out if a
        // plugin ever ships two ports called the same thing. Every listing a
        // tool prints shows both, so the escape hatch is always in view.
        if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var direct)
            && direct < ports.Count)
        {
            index = direct;
            return true;
        }

        for (var i = 0; i < ports.Count; i++)
        {
            if (!string.Equals(ports[i].Name, name, StringComparison.OrdinalIgnoreCase)) continue;

            index = i;
            return true;
        }

        index = -1;
        return false;
    }

    public static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
