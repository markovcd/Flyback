using System.Text.Json.Nodes;

namespace Flyback.Plugins.Gemini;

/// <summary>One tool call the model asked for.</summary>
/// <remarks>
/// No id, unlike the chat-completions spelling: a <c>functionCall</c> carries a
/// name and nothing to match a reply to, so a turn that asked for the same tool
/// twice is answered by order.
/// </remarks>
/// <param name="Arguments">Already a JSON object here, where the other format sends a string of one.</param>
internal sealed record Call(string Name, JsonNode? Arguments);
