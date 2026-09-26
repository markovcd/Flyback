namespace Flyback.Plugins.Midi;

/// <summary>
/// One thing that happened on a device.
/// </summary>
/// <param name="Note">
/// The MIDI note number, 0 to 127; the controller number for
/// <see cref="MidiAction.Control"/>; the song position in sixteenths for
/// <see cref="MidiAction.Position"/>. Ignored by everything else.
/// </param>
/// <param name="Velocity">
/// How hard, or for a controller how far, 0 to 1 — already divided out of whatever
/// the wire carried, because 127 is a fact about MIDI and not about anything above
/// this line.
/// </param>
public readonly record struct MidiMessage(MidiAction Action, int Note, float Velocity)
{
    /// <summary>
    /// The channel it arrived on, 1 to 16, or 0 where nobody said — the clock and
    /// the transport belong to the whole cable. A voice ignores it; a knob bound
    /// to a controller may not.
    /// </summary>
    public int Channel { get; init; }
}