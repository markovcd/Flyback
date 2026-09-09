using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flyback.Core;

namespace Flyback.Plugins.Assist;

/// <summary>
/// What the assistant panel was last set to. The first thing this application
/// has ever written about itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no key in here, and there never will be.</b> Which provider, and
/// whatever that provider asked to be remembered — those are choices, and a
/// choice is worth remembering in a file. A credential is not: it goes to the
/// operating system's own store or nowhere at all. See ADR-0034 and
/// <see cref="Credentials"/>.
/// </para>
/// <para>
/// What a provider's choices <em>are</em> is not written down here, because
/// nothing in the App knows — a provider declares its own settings and this
/// keeps the answers under the names it gave them (ADR-0069). So the file grew
/// one level: a bag of strings per provider, which is also what stops two
/// providers with a setting of the same name from overwriting each other, and
/// what lets somebody keep a configured endpoint on one while trying another.
/// </para>
/// <para>
/// Nothing here is load-bearing. A file that is missing, unreadable or written
/// by a different version means the defaults, because losing a preference is not
/// worth failing to start over.
/// </para>
/// </remarks>
public sealed class AssistantSettings
{
    /// <remarks>
    /// Escaped for a person rather than for a web page. The default encoder is
    /// the cautious one and spells anything it is unsure of as an escape, which
    /// is correct anywhere and legible nowhere; these are settings somebody
    /// opens and reads. What is structured does not come through here at all —
    /// see <see cref="ChoiceConverter"/> — so this is about the ordinary values
    /// beside it, an endpoint or a model name.
    /// </remarks>
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(), new ChoiceConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Which assistant, by <see cref="IPatchAssistant.Id"/>. Empty means whichever is preferred.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Whether a key typed in should be handed to the operating system to keep.
    /// The key itself is not here — this only records the answer to the question.
    /// </summary>
    /// <remarks>
    /// One answer for every provider rather than one each, because it is an
    /// answer about this machine — whether secrets belong in its store — rather
    /// than about whoever is being talked to.
    /// </remarks>
    public bool RememberKey { get; set; }

    /// <summary>
    /// Whether every turn is written out to a file under
    /// <see cref="ConversationLog.Folder"/>, one file per conversation. Off
    /// until somebody turns it on.
    /// </summary>
    /// <remarks>
    /// A choice about this machine rather than about any provider, for the same
    /// reason <see cref="RememberKey"/> is: the file is written here regardless
    /// of who was asked.
    /// </remarks>
    public bool LogConversations { get; set; }

    /// <summary>
    /// What each provider was last set to, filed under its id.
    /// </summary>
    /// <remarks>
    /// Plain strings, in the shape a provider's own <see cref="AssistantField"/>
    /// list gave them, so the file stays readable and hand-editable and this
    /// class stays ignorant of what any of it means. A settable property with a
    /// public setter because that is what the serialiser needs; everything in
    /// the program goes through <see cref="Of"/> and <see cref="Remember"/>.
    /// <para>
    /// Strings in here, and not necessarily strings in the file — see
    /// <see cref="ChoiceConverter"/>. A provider that keeps something structured
    /// keeps it as text on this side of the boundary and gets it back as the
    /// same text, while the file holds it as what it is.
    /// </para>
    /// </remarks>
    public Dictionary<string, Dictionary<string, string>> Choices { get; set; } = new(StringComparer.Ordinal);

    public static string File => Path.Combine(GlobalConstants.DataFolder, "assistant.json");

    /// <summary>What is set for one provider, and nothing at all for one nobody has configured.</summary>
    public AssistantValues Of(string provider) =>
        Choices.TryGetValue(provider, out var held) ? new AssistantValues(held) : AssistantValues.None;

    /// <summary>
    /// Takes one provider's answers, leaving every other provider's alone.
    /// </summary>
    /// <remarks>
    /// Kept rather than replaced, so that trying a second provider for an
    /// afternoon does not cost the endpoint and model somebody spent time on for
    /// the first.
    /// </remarks>
    public void Remember(string provider, AssistantValues values) =>
        Choices[provider] = new Dictionary<string, string>(values.All, StringComparer.Ordinal);

    /// <summary>Never throws. A settings file is not worth a failure to start.</summary>
    /// <param name="path">Somewhere other than the usual place, for the tests.</param>
    public static AssistantSettings Load(string? path = null)
    {
        try
        {
            var from = path ?? File;

            return System.IO.File.Exists(from)
                ? JsonSerializer.Deserialize<AssistantSettings>(System.IO.File.ReadAllText(from), Options) ?? new()
                : new AssistantSettings();
        }
        catch
        {
            return new AssistantSettings();
        }
    }

    /// <summary>
    /// Writes the choices out. Throws if it cannot, so the caller can say so —
    /// silently forgetting what somebody just set is worse than a line in the
    /// status bar.
    /// </summary>
    public void Save(string? path = null)
    {
        var to = path ?? File;

        Directory.CreateDirectory(Path.GetDirectoryName(to) ?? GlobalConstants.DataFolder);
        System.IO.File.WriteAllText(to, JsonSerializer.Serialize(this, Options));
    }
}

/// <summary>
/// One provider's answers, written in the file as what they actually are.
/// </summary>
/// <remarks>
/// <para>
/// The settings shape is a bag of strings per provider and that is not
/// negotiable: it is what lets the shell carry answers it cannot interpret
/// (ADR-0069). But a provider may keep something that is itself structured — a
/// survey of an endpoint is a list of models — and writing that as a quoted
/// string puts a second layer of escaping over every quote in it, which turns
/// the part of the file somebody most wants to read into the part they cannot.
/// </para>
/// <para>
/// So the boundary keeps its strings and the file keeps its shape. A value that
/// parses as an array or an object is written inline and read back as exactly
/// the text that was written, which the provider parses as it always did.
/// </para>
/// <para>
/// Arrays and objects only, deliberately. A value of <c>1</c> or <c>true</c>
/// would be just as writable and would come back as a different string than it
/// went in as — <c>"007"</c> is the one that gives the game away — and a setting
/// that changes under a save is worse than one that is quoted.
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
