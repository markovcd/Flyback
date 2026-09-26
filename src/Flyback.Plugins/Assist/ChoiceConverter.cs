using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flyback.Plugins.Assist;

/// <summary>
/// One provider's answers, written in the file as what they actually are.
/// </summary>
/// <remarks>
/// The settings shape is a bag of strings per provider (ADR-0069), but a provider
/// may keep something structured — a survey of an endpoint is a list of models —
/// and writing that as a quoted string puts a second layer of escaping over every
/// quote in it. So the boundary keeps its strings and the file keeps its shape.
/// <para>
/// Arrays and objects only: <c>1</c> or <c>true</c> would be just as writable and
/// would come back as a different string than it went in as — <c>"007"</c> gives
/// the game away — and a setting that changes under a save is worse than a quoted
/// one.
/// </para>
/// </remarks>
internal sealed class ChoiceConverter : JsonConverter<Dictionary<string, string>>
{
    public override Dictionary<string, string> Read(
        ref Utf8JsonReader reader,
        Type type,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();

        var held = new Dictionary<string, string>(StringComparer.Ordinal);

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return held;
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException();

            var name = reader.GetString() ?? throw new JsonException();

            reader.Read();

            // Anything that is not a string comes back as the text it was
            // written as, which is what a file somebody edited by hand deserves:
            // a number typed where a string was expected is an answer, not a
            // fault, and the provider reading it back knows what it meant.
            held[name] = reader.TokenType == JsonTokenType.String
                ? reader.GetString() ?? string.Empty
                : Raw(ref reader);
        }

        throw new JsonException();
    }

    public override void Write(
        Utf8JsonWriter writer,
        Dictionary<string, string> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var (name, held) in value)
        {
            writer.WritePropertyName(name);

            if (Structured(held)) writer.WriteRawValue(held);
            else writer.WriteStringValue(held);
        }

        writer.WriteEndObject();
    }

    private static string Raw(ref Utf8JsonReader reader)
    {
        using var value = JsonDocument.ParseValue(ref reader);

        return value.RootElement.GetRawText();
    }

    /// <summary>
    /// Whether this is a list or an object rather than a word that happens to
    /// begin with a bracket. Parsed rather than guessed at, because a value
    /// written raw and not readable back would take the whole file with it.
    /// </summary>
    private static bool Structured(string value)
    {
        var start = value.AsSpan().TrimStart();

        if (start.Length == 0 || (start[0] != '[' && start[0] != '{')) return false;

        try
        {
            using var parsed = JsonDocument.Parse(value);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}