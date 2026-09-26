namespace Flyback.Plugins.Midi;

/// <summary>
/// The three bytes of a MIDI message, read as a note.
/// </summary>
/// <remarks>
/// Here rather than in a backend because this is the one part of hearing a
/// keyboard that is not about the machine. Every platform hands the same three
/// bytes over in a different way — Windows packs them into one word, CoreMIDI
/// puts them in a packet list, ALSA has decoded them into a struct already — and
/// what those bytes then mean was settled in 1983 and is the same everywhere.
/// A backend does the unpacking; this does the reading.
/// </remarks>
public static class MidiMessages
{
    /// <summary>The four bits of a status byte that say which command; the other four are the channel.</summary>
    private const byte Command = 0xF0;

    private const byte NoteOff = 0x80;

    private const byte NoteOn = 0x90;

    private const byte ControlChange = 0xB0;

    private const byte SongPosition = 0xF2;

    private const byte TimingClock = 0xF8;

    private const byte SequencerStart = 0xFA;

    private const byte SequencerContinue = 0xFB;

    private const byte SequencerStop = 0xFC;

    /// <summary>The panic buttons, which every device that has one sends as one of these.</summary>
    private const byte AllSoundOff = 120;

    private const byte AllNotesOff = 123;

    /// <summary>
    /// What a message means, or null where it means nothing to anything here —
    /// which is most of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every channel is heard and the channel is carried along, not acted on: a
    /// voice merges them, which is what a monophonic synth on a MIDI thru chain has
    /// always done, and a mapped knob may care which one it was.
    /// </para>
    /// <para>
    /// Active sensing, aftertouch and the pitch wheel come back null. They are not
    /// dropped as a shortcut — there is nothing above this that reads them, and a
    /// module that grew an output for one would be a case added here. The
    /// modulation wheel is a controller like any other, and comes back as one.
    /// </para>
    /// </remarks>
    public static MidiMessage? Of(byte status, byte first, byte second)
    {
        // A status byte has the top bit set; anything else is a data byte that
        // arrived where a message was expected, and means nothing on its own.
        if (status < 0x80) return null;

        // The system messages carry no channel, so the mask below would read one
        // as whatever command shares its top nibble. The clock and the transport
        // are the ones anything here listens for.
        if (status >= Command)
        {
            return status switch
            {
                TimingClock => new MidiMessage(MidiAction.Tick, 0, 0f),
                SequencerStart => new MidiMessage(MidiAction.Start, 0, 0f),
                SequencerContinue => new MidiMessage(MidiAction.Continue, 0, 0f),
                SequencerStop => new MidiMessage(MidiAction.Stop, 0, 0f),
                SongPosition => new MidiMessage(MidiAction.Position, (first & 0x7F) | ((second & 0x7F) << 7), 0f),
                _ => null,
            };
        }

        var note = first & 0x7F;
        var value = second & 0x7F;
        var channel = (status & 0x0F) + 1;

        MidiMessage? read = (status & Command) switch
        {
            // A note-on with no force behind it is a note-off said the other way
            // round, and a great many keyboards say it that way rather than
            // sending the message that means it.
            NoteOn when value > 0 => new MidiMessage(MidiAction.Down, note, value / 127f),
            NoteOn or NoteOff => new MidiMessage(MidiAction.Up, note, 0f),

            ControlChange when note is AllSoundOff or AllNotesOff =>
                new MidiMessage(MidiAction.AllOff, 0, 0f),

            ControlChange => new MidiMessage(MidiAction.Control, note, value / 127f),

            _ => null,
        };

        return read is { } message ? message with { Channel = channel } : null;
    }
}