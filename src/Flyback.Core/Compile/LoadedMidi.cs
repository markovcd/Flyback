using System.Collections.Concurrent;

namespace Flyback.Core.Compile;

/// <summary>
/// A MIDI file as the machine wants it: its notes placed in seconds, with every
/// tempo change already applied.
/// </summary>
/// <remarks>
/// Immutable, so a program on one thread and a program on another may both read
/// it. The tables a voice plays are built the first time one is asked for and kept,
/// since every edit recompiles the whole patch (ADR-0021).
/// </remarks>
/// <param name="notes">Every note in the file, in no particular order.</param>
/// <param name="seconds">How long the file runs, to the last note let go.</param>
public sealed class LoadedMidi(IReadOnlyList<MidiNote> notes, float seconds)
{
    private readonly ConcurrentDictionary<(int Voice, int Channel), MidiLine> lines = new();

    /// <summary>Every note in the file.</summary>
    public IReadOnlyList<MidiNote> Notes => notes;

    /// <summary>How long the file runs.</summary>
    public float Seconds => seconds;

    /// <summary>
    /// What one voice of the file plays, with the channel heard held to what a
    /// file has.
    /// </summary>
    /// <param name="voice">0 for one voice that plays the newest key held, 1 to <see cref="MidiLine.Voices"/> for that one of the notes held at once.</param>
    /// <param name="channel">0 for every channel, 1 to 16 for one.</param>
    public MidiLine Line(int voice, int channel)
    {
        var key = (Math.Clamp(voice, 0, MidiLine.Voices), Math.Clamp(channel, 0, 16));

        return lines.GetOrAdd(key, k => MidiLine.Of(notes, seconds, k.Item1, k.Item2));
    }
}
