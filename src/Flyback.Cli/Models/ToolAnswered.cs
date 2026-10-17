namespace Flyback.Cli.Models;

/// <summary>What a call answered, wherever it ran: the words, and a picture where it drew one.</summary>
/// <param name="Ok">Whether it did what was asked.</param>
/// <param name="Text">What it said, or why it did nothing.</param>
/// <param name="Png">A picture that goes with it, or null.</param>
internal sealed record ToolAnswered(bool Ok, string Text, byte[]? Png = null);
