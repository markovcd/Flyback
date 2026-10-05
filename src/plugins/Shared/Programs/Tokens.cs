using System.Text.Json.Nodes;

namespace Flyback.Plugins.Programs;

/// <summary>Reading what a program says a turn cost.</summary>
internal static class Tokens
{
    /// <summary>The named count in a usage object, or zero where it is missing or is not a number.</summary>
    public static int Count(JsonNode? usage, string name) =>
        usage?[name] is JsonValue value && value.TryGetValue<int>(out var count) ? count : 0;
}
