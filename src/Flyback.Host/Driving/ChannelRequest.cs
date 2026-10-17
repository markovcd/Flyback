using System.Text.Json;

namespace Flyback.Host.Driving;

/// <summary>One message from a program driving the editor: a greeting, a tool call, or words for the person.</summary>
/// <param name="Id">What ties the reply to the request; 0 for the greeting.</param>
/// <param name="Hello">The client's name, as the greeting gives it; null on every other message.</param>
/// <param name="Tool">The tool to call, or null.</param>
/// <param name="Arguments">The call's arguments, a JSON object.</param>
/// <param name="Say">What to tell the person, or null.</param>
internal sealed record ChannelRequest(int Id, string? Hello = null, string? Tool = null, JsonElement? Arguments = null, string? Say = null);
