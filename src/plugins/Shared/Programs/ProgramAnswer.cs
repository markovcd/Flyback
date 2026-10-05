namespace Flyback.Plugins.Programs;

/// <summary>What the program said, and what it cost in tokens.</summary>
internal sealed record ProgramAnswer(string Text, int Input, int Cached, int Output);
