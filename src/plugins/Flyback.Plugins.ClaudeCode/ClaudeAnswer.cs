namespace Flyback.Plugins.ClaudeCode;

/// <summary>What Claude Code said, and what it cost in tokens.</summary>
internal sealed record ClaudeAnswer(string Text, int Input, int Cached, int Output);
