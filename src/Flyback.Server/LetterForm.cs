namespace Flyback.Server;

/// <summary>What a letter says, as it is posted.</summary>
internal sealed record LetterForm(string? Mood, string? Message, string? Contact, string? Version, string? Platform, string? Plugins);
