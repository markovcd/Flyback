using System.Text.Json.Nodes;

namespace Flyback.Plugins.Gemini;

/// <summary>What came back from one request.</summary>
internal sealed record Reply(
    string? Text,
    IReadOnlyList<Call> Calls,
    JsonNode? RawContent,
    int Input,
    int Cached,
    int Output);
