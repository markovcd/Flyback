namespace Flyback.Core.Graph;

/// <summary>A seven-note scale as semitones above its tonic.</summary>
public sealed record ScaleMode(string Id, string Name, int[] Classes);