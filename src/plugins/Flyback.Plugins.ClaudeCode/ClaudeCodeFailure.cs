namespace Flyback.Plugins.ClaudeCode;

/// <summary>Claude Code could not answer; the message is what the person is told.</summary>
internal sealed class ClaudeCodeFailure(string message) : Exception(message);
