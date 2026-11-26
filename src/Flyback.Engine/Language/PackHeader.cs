namespace Flyback.Engine.Language;

/// <summary>What a module pack says of itself in its first lines: its id, its version and the text it was written for.</summary>
public sealed record PackHeader(string Id, string Version, int Language);
