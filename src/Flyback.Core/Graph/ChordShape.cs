namespace Flyback.Core.Graph;

/// <summary>A chord as semitones above its root, and what it is called.</summary>
public sealed record ChordShape(string Name, int[] Intervals);