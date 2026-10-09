using System.Text.Json.Nodes;

namespace Flyback.Plugins.Assist;

/// <summary>What a <see cref="ToolField"/> holds, as a JSON Schema says it.</summary>
/// <param name="Type">The schema's <c>type</c>, or null for a field that takes more than one.</param>
internal sealed record ToolKind(
    string? Type,
    double? Minimum = null,
    double? Maximum = null,
    IReadOnlyList<string>? Choices = null,
    ToolKind? Items = null,
    IReadOnlyList<ToolField>? Fields = null)
{
    public static ToolKind Text { get; } = new("string");

    public static ToolKind Number { get; } = new("number");

    public static ToolKind Flag { get; } = new("boolean");

    /// <summary>A value whose kind depends on something else the call names.</summary>
    public static ToolKind Anything { get; } = new((string?)null);

    public static ToolKind Between(double minimum, double maximum) => new("number", minimum, maximum);

    public static ToolKind Whole(int minimum, int maximum) => new("integer", minimum, maximum);

    public static ToolKind OneOf(IEnumerable<string> choices) => new("string", Choices: [.. choices]);

    public static ToolKind ListOf(ToolKind items) => new("array", Items: items);

    public static ToolKind ObjectOf(params ToolField[] fields) => new("object", Fields: fields);

    internal JsonObject Schema()
    {
        var schema = new JsonObject();

        if (Type is not null) schema["type"] = Type;
        if (Minimum is { } minimum) schema["minimum"] = minimum;
        if (Maximum is { } maximum) schema["maximum"] = maximum;
        if (Choices is not null) schema["enum"] = new JsonArray([.. Choices.Select(choice => (JsonNode)choice)]);
        if (Items is not null) schema["items"] = Items.Schema();
        if (Fields is not null) ToolField.Describe(schema, Fields);

        return schema;
    }
}
