using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Plugins.Assist;

namespace Flyback.App.Assist;

/// <summary>Whose a line of the transcript is, which decides how it is drawn.</summary>
public enum Voice
{
    /// <summary>What the person asked.</summary>
    You,

    /// <summary>What the assistant said in words.</summary>
    Said,

    /// <summary>What it did, looked at or listened to, and what the panel says about the conversation.</summary>
    Note,

    /// <summary>The small print: what a turn cost, and what applying a proposal did.</summary>
    Aside,

    Proposed,

    Failed,
}

/// <summary>One line of the transcript, as the panel showed it.</summary>
public sealed record TranscriptLine(Voice Voice, string Text);

/// <summary>
/// A conversation put away with the patch it is about, to be carried on the next
/// time that patch is opened (ADR-0072).
/// </summary>
/// <remarks>
/// Two owners, kept apart: the workbench and the transcript are the host's, and
/// <see cref="History"/> is whatever the provider's session wrote — opaque here,
/// handed back to the same provider and read by nobody else.
/// <para>
/// No key, and no settings either. What the conversation was set up with is kept
/// as a fingerprint, which is enough to say whether the settings in force now are
/// the same ones and says nothing else: a bundle is something people send each
/// other, and an endpoint address is not theirs to pass on.
/// </para>
/// </remarks>
/// <param name="Provider">Who it was with, by <see cref="IPatchAssistant.Id"/>.</param>
/// <param name="Settings">What that provider was set to, as <see cref="SettingsOf"/> fingerprints it.</param>
/// <param name="Turns">How many turns it has had.</param>
/// <param name="Bench"></param>
/// <param name="History">The provider's own account of it, or null where it kept none.</param>
/// <param name="Transcript"></param>
public sealed record SavedConversation(
    string Provider,
    string Settings,
    int Turns,
    WorkbenchState Bench,
    string? History,
    IReadOnlyList<TranscriptLine> Transcript)
{
    /// <summary>
    /// The shape this writes. A file in any other is not read at all: a
    /// conversation that comes back half understood is worse than one that starts
    /// again.
    /// </summary>
    private const int Shape = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>A fingerprint of a provider's settings, the same however they were built up.</summary>
    public static string SettingsOf(AssistantValues values)
    {
        var pairs = new JsonArray();

        foreach (var (key, value) in values.All.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            pairs.Add(new JsonArray { key, value });

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pairs.ToJsonString())));
    }

    /// <summary>
    /// The conversation as a file holds it.
    /// </summary>
    /// <remarks>
    /// The patches and the provider's history go in as the JSON they are rather
    /// than as strings of it, so the file reads as what it is — a bundle is a zip
    /// anybody can look inside.
    /// </remarks>
    public string ToJson()
    {
        var handles = new JsonObject();

        foreach (var (handle, id) in Bench.Handles) handles[handle] = id.ToString();

        var transcript = new JsonArray();

        foreach (var line in Transcript)
            transcript.Add(new JsonObject { ["voice"] = line.Voice.ToString(), ["text"] = line.Text });

        return new JsonObject
        {
            ["shape"] = Shape,
            ["provider"] = Provider,
            ["settings"] = Settings,
            ["turns"] = Turns,
            ["edits"] = Bench.Edits,
            ["toolCalls"] = Bench.ToolCalls,
            ["start"] = Embedded(Bench.Start),
            ["working"] = Embedded(Bench.Working),
            ["handles"] = handles,
            ["history"] = History is null ? null : Embedded(History),
            ["transcript"] = transcript,
        }.ToJsonString(Options);
    }

    /// <summary>
    /// A conversation read back, or null for anything that is not one this can
    /// carry on. Never throws: a conversation that will not read costs the
    /// conversation, and the patch it came with opens either way.
    /// </summary>
    public static SavedConversation? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            if (JsonNode.Parse(json) is not JsonObject body) return null;
            if (Number(body["shape"]) != Shape) return null;

            if (Word(body["provider"]) is not { Length: > 0 } provider) return null;
            if (Word(body["settings"]) is not { } settings) return null;
            if (Raw(body["start"]) is not { } start || Raw(body["working"]) is not { } working) return null;

            var handles = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

            if (body["handles"] is JsonObject named)
            {
                foreach (var (handle, id) in named)
                    if (Guid.TryParse(Word(id), out var parsed)) handles[handle] = parsed;
            }

            var transcript = new List<TranscriptLine>();

            if (body["transcript"] is JsonArray lines)
            {
                foreach (var line in lines)
                {
                    if (Enum.TryParse<Voice>(Word(line?["voice"]), out var voice) && Word(line?["text"]) is { } text)
                        transcript.Add(new TranscriptLine(voice, text));
                }
            }

            return new SavedConversation(
                provider,
                settings,
                Number(body["turns"]),
                new WorkbenchState(start, working, handles, Number(body["edits"]), Number(body["toolCalls"])),
                Raw(body["history"]),
                transcript);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>JSON as the node it is, or as a string where it is not JSON after all.</summary>
    private static JsonNode Embedded(string json)
    {
        try
        {
            return JsonNode.Parse(json) ?? JsonValue.Create(json);
        }
        catch (JsonException)
        {
            return JsonValue.Create(json);
        }
    }

    /// <summary>The inverse of <see cref="Embedded"/>: a string as itself, anything else as its JSON.</summary>
    private static string? Raw(JsonNode? node) => node is null ? null : Word(node) ?? node.ToJsonString();

    private static string? Word(JsonNode? node) =>
        node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;

    private static int Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;
}
