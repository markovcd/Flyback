namespace Flyback.Plugins.Programs;

/// <summary>What the program said, and what it cost in tokens.</summary>
/// <param name="Input">Tokens sent, cached ones included.</param>
/// <param name="Cached">How many of those were read from the cache.</param>
internal sealed record ProgramAnswer(string Text, int Input, int Cached, int Output);
