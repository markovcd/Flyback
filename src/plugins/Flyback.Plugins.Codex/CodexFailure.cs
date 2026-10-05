namespace Flyback.Plugins.Codex;

/// <summary>Codex could not answer; the message is what the person is told.</summary>
internal sealed class CodexFailure(string message) : Exception(message);
