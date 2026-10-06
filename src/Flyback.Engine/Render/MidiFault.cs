namespace Flyback.Engine.Render;

/// <summary>
/// Why a file could not be read as MIDI, or <see cref="None"/> where it could. A
/// value rather than an exception, for the reason <see cref="SoundFault"/> is one.
/// </summary>
public enum MidiFault
{
    None,

    /// <summary>Nothing at that path.</summary>
    Missing,

    /// <summary>Something is there and it is not a standard MIDI file.</summary>
    NotMidi,

    /// <summary>A MIDI file whose timing this reader does not know how to read.</summary>
    Unsupported,

    /// <summary>On another machine, where a patch is not allowed to reach.</summary>
    Elsewhere,

    /// <summary>A MIDI file with no notes in it.</summary>
    Empty,

    /// <summary>Longer than <see cref="MidiFileReader.MostSeconds"/>.</summary>
    TooLong,

    /// <summary>Larger than <see cref="MidiFileReader.MostBytes"/>, or holding more than <see cref="MidiFileReader.MostNotes"/> notes.</summary>
    TooBig,
}
