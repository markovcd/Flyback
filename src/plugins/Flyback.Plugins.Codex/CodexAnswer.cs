namespace Flyback.Plugins.Codex;

/// <summary>What Codex said, and what it cost in tokens.</summary>
internal sealed record CodexAnswer(string Text, int Input, int Cached, int Output);
