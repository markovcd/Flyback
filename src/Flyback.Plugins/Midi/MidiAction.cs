namespace Flyback.Plugins.Midi;

/// <summary>What a device just did: a note, a knob turned, or its clock moving.</summary>
/// <remarks>
/// A MIDI cable carries more — wheels, aftertouch, song select — and none of it is
/// here, because nothing above this reads any of it. A signal added later is a
/// case added here rather than a shape changed.
/// </remarks>
public enum MidiAction
{
    /// <summary>A key struck.</summary>
    Down,

    /// <summary>A key let go.</summary>
    Up,

    /// <summary>
    /// Everything let go at once. What a panic button sends, and the only
    /// honest answer to a device that has stopped talking mid-chord.
    /// </summary>
    AllOff,

    /// <summary>
    /// A controller moved — a knob, a fader, anything sent as a control change.
    /// <see cref="MidiMessage.Note"/> is the controller number and
    /// <see cref="MidiMessage.Velocity"/> where it now sits, 0 to 1.
    /// </summary>
    Control,

    /// <summary>
    /// One tick of a sequencer's clock, of which there are twenty-four to a beat.
    /// Sent whether or not the sequencer is running.
    /// </summary>
    Tick,

    /// <summary>The sequencer began playing from the top.</summary>
    Start,

    /// <summary>The sequencer went on from where it had stopped.</summary>
    Continue,

    /// <summary>The sequencer stopped.</summary>
    Stop,

    /// <summary>
    /// The sequencer said where in the song it is: <see cref="MidiMessage.Note"/>
    /// is the position in sixteenth notes.
    /// </summary>
    Position,
}