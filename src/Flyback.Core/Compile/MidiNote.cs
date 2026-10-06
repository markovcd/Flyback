namespace Flyback.Core.Compile;

/// <summary>One note of a MIDI file, placed in seconds.</summary>
/// <param name="Start">When it is struck, in seconds from the start of the file.</param>
/// <param name="End">When it is let go, never before <paramref name="Start"/>.</param>
/// <param name="Note">The key, 0 to 127.</param>
/// <param name="Velocity">How hard, 0 to 1.</param>
/// <param name="Channel">The channel it plays on, 1 to 16.</param>
public readonly record struct MidiNote(float Start, float End, int Note, float Velocity, int Channel);
