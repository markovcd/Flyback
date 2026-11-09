using System.Text.Json.Nodes;

namespace Flyback.Assist.OpenAi;

/// <summary>What came back from one request.</summary>
internal sealed record Reply(
    string? Text,
    IReadOnlyList<Call> Calls,
    JsonNode? RawMessage,
    int Input,
    int Cached,
    int Output,
    string? Model = null);
