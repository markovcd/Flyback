namespace Flyback.Plugins.Midi;

/// <summary>
/// MIDI bytes as a wire carries them, cut into messages: for a backend handed the stream
/// as it comes rather than one message at a time.
/// </summary>
/// <remarks>
/// Android's <c>MidiReceiver</c> hands over whatever has arrived, so a message may come in
/// two pieces, a run of notes may share one status byte (running status), and a clock tick
/// may land between two bytes of a note. What has been gathered is kept between calls, so
/// each message reaches <see cref="MidiMessages.Of"/> once, whole. A system-exclusive
/// message is stepped over to its end byte.
/// </remarks>
internal sealed class MidiStream(MidiCallback deliver)
{
    private const byte StartOfExclusive = 0xF0;

    private const byte EndOfExclusive = 0xF7;

    /// <summary>The clock and the transport, which may land between any two bytes of anything else.</summary>
    private const byte RealTime = 0xF8;

    /// <summary>The status byte the next data bytes belong to; nought where there is none.</summary>
    private byte status;

    private byte first;

    /// <summary>Data bytes gathered since the status byte: nought or one.</summary>
    private int gathered;

    private bool exclusive;

    /// <summary>Reads what arrived, delivering each message it completes.</summary>
    public void Feed(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes) Take(b);
    }

    private void Take(byte b)
    {
        if (b >= RealTime)
        {
            Deliver(b, 0, 0);
            return;
        }

        if (b >= 0x80)
        {
            exclusive = b == StartOfExclusive;
            gathered = 0;
            status = b is StartOfExclusive or EndOfExclusive ? (byte)0 : b;

            if (status != 0 && DataBytes(status) == 0)
            {
                Deliver(status, 0, 0);
                status = 0;
            }

            return;
        }

        // A data byte with nothing to belong to, or inside an exclusive message.
        if (exclusive || status == 0) return;

        var needed = DataBytes(status);

        if (needed == 2 && gathered == 0)
        {
            first = b;
            gathered = 1;
            return;
        }

        Deliver(status, needed == 2 ? first : b, needed == 2 ? b : (byte)0);
        gathered = 0;

        // Running status is a channel message's; a system common message is said once.
        if (status >= StartOfExclusive) status = 0;
    }

    /// <summary>How many data bytes follow a status byte. Exclusive and real time never reach this.</summary>
    private static int DataBytes(byte status) => status switch
    {
        // Quarter frame and song select carry one; song position two; the rest of system common none.
        0xF1 or 0xF3 => 1,
        0xF2 => 2,
        >= 0xF4 => 0,

        // Program change and channel pressure, the two channel messages with one byte after them.
        >= 0xC0 and <= 0xDF => 1,

        // Notes, aftertouch, controllers and the wheel.
        _ => 2,
    };

    private void Deliver(byte status, byte first, byte second)
    {
        if (MidiMessages.Of(status, first, second) is not { } message) return;

        try
        {
            deliver(message);
        }
        catch
        {
            // Whoever is listening threw. One dropped note is the answer that leaves
            // the rest of the stream playing.
        }
    }
}
