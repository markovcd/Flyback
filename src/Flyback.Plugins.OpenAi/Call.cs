namespace Flyback.Plugins.OpenAi;

/// <summary>One tool call the model asked for.</summary>
internal sealed record Call(string Id, string Name, string Arguments);