namespace Flyback.Plugins.Assist;

/// <summary>What one request of a conversation came back with.</summary>
/// <param name="Text">What the model said, or null where it only asked for things.</param>
/// <param name="Calls">The tools it asked for, in order.</param>
/// <param name="Input">Tokens sent, where the endpoint says.</param>
/// <param name="Cached">How many of those it had cached.</param>
/// <param name="Output">Tokens it wrote.</param>
public sealed record ModelReply(string? Text, IReadOnlyList<ToolCall> Calls, int Input = 0, int Cached = 0, int Output = 0);
