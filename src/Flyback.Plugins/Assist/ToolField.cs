using System.Text.Json;
using System.Text.Json.Nodes;

namespace Flyback.Plugins.Assist;

/// <summary>
/// One argument a tool takes: what a provider is told of it and what the tool's body
/// reads it by, so the two cannot name it differently.
/// </summary>
internal sealed record ToolField(string Name, ToolKind Kind, bool Required = false, string? Description = null)
{
    /// <summary>The same field, told to a provider in other words.</summary>
    public ToolField Described(string description) => this with { Description = description };

    /// <summary>The body of the object schema <paramref name="fields"/> make, as <see cref="PatchTool.Schema"/> holds it.</summary>
    public static string Schema(IReadOnlyList<ToolField> fields)
    {
        var schema = new JsonObject();

        if (fields.Count > 0) Describe(schema, fields);

        return schema.ToJsonString();
    }

    internal static void Describe(JsonObject schema, IReadOnlyList<ToolField> fields)
    {
        var properties = new JsonObject();

        foreach (var field in fields)
        {
            var property = field.Kind.Schema();
            if (field.Description is not null) property["description"] = field.Description;
            properties[field.Name] = property;
        }

        schema["properties"] = properties;

        if (fields.Any(field => field.Required))
            schema["required"] = new JsonArray([.. fields.Where(field => field.Required).Select(field => (JsonNode)field.Name)]);
    }

    /// <summary>Whatever was sent for this field, of any kind.</summary>
    public bool Find(JsonElement arguments, out JsonElement value)
    {
        if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(Name, out value)) return true;

        value = default;
        return false;
    }

    /// <summary>A string that is not empty.</summary>
    public bool Text(JsonElement arguments, out string value)
    {
        value = Find(arguments, out var found) && found.ValueKind == JsonValueKind.String ? found.GetString() ?? string.Empty : string.Empty;
        return value.Length > 0;
    }

    /// <summary>
    /// A true-or-false argument, and <paramref name="fallback"/> where it was not
    /// sent: a switch nobody threw is one the caller had no opinion about.
    /// </summary>
    public bool Flag(JsonElement arguments, bool fallback) =>
        Find(arguments, out var found) && found.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? found.GetBoolean()
            : fallback;

    public bool Number(JsonElement arguments, out double value)
    {
        value = 0d;
        return Find(arguments, out var found) && found.ValueKind == JsonValueKind.Number && found.TryGetDouble(out value);
    }

    public bool Real(JsonElement arguments, out float value)
    {
        value = 0f;
        return Find(arguments, out var found) && found.ValueKind == JsonValueKind.Number && found.TryGetSingle(out value);
    }

    public bool Whole(JsonElement arguments, out int value)
    {
        value = 0;
        return Find(arguments, out var found) && found.ValueKind == JsonValueKind.Number && found.TryGetInt32(out value);
    }

    public bool List(JsonElement arguments, out JsonElement items) =>
        Find(arguments, out items) && items.ValueKind == JsonValueKind.Array;

    /// <summary>The name as a refusal quotes it.</summary>
    public string Quoted => $"'{Name}'";
}
