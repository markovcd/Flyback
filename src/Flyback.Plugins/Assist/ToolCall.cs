namespace Flyback.Plugins.Assist;

/// <summary>One tool call as a model asked for it.</summary>
/// <param name="Id">What ties the answer to the call, where the format has one; empty where the order does.</param>
/// <param name="Name">The tool.</param>
/// <param name="Arguments">Its arguments as JSON text, or empty for none.</param>
public sealed record ToolCall(string Id, string Name, string Arguments);
