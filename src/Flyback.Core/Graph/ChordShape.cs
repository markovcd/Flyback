namespace Flyback.Core.Graph;

/// <summary>A chord as semitones above its root, and what it is called.</summary>
internal sealed record ChordShape(string Name, int[] Intervals);