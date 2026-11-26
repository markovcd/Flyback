namespace Flyback.Engine.Packs;

/// <summary>A pack as it was read: its id, its version and its whole text, which a bundle carries.</summary>
public sealed record PackSource(string Id, string Version, string Text);
