namespace Flyback.Core.Tests.Graph;

/// <summary>Standard MIDI files made by hand, so a test says exactly what is in the one it reads.</summary>
internal static class MidiBytes
{
    /// <summary>A file of format 1 with <paramref name="division"/> ticks to the quarter note.</summary>
    public static byte[] File(int division, params byte[][] tracks)
    {
        var bytes = new List<byte>("MThd"u8.ToArray());

        bytes.AddRange([0, 0, 0, 6, 0, 1, 0, (byte)tracks.Length, (byte)(division >> 8), (byte)division]);

        foreach (var track in tracks)
        {
            bytes.AddRange("MTrk"u8.ToArray());
            bytes.AddRange([(byte)(track.Length >> 24), (byte)(track.Length >> 16), (byte)(track.Length >> 8), (byte)track.Length]);
            bytes.AddRange(track);
        }

        return [.. bytes];
    }

    /// <summary>One track from its events, closed with an end-of-track.</summary>
    public static byte[] Track(params byte[][] events) => [.. events.SelectMany(e => e), .. End(0)];

    /// <summary>A note struck on channel 1 to 16, <paramref name="delta"/> ticks after the event before.</summary>
    public static byte[] On(int delta, int channel, int note, int velocity = 100) =>
        [.. Delta(delta), (byte)(0x90 | (channel - 1)), (byte)note, (byte)velocity];

    /// <summary>A note let go.</summary>
    public static byte[] Off(int delta, int channel, int note) =>
        [.. Delta(delta), (byte)(0x80 | (channel - 1)), (byte)note, 0];

    /// <summary>A tempo change, in microseconds to the quarter note.</summary>
    public static byte[] Tempo(int delta, int micros) =>
        [.. Delta(delta), 0xFF, 0x51, 3, (byte)(micros >> 16), (byte)(micros >> 8), (byte)micros];

    /// <summary>The end of a track.</summary>
    public static byte[] End(int delta) => [.. Delta(delta), 0xFF, 0x2F, 0];

    /// <summary>A variable-length quantity.</summary>
    public static byte[] Delta(int value)
    {
        var groups = new List<byte> { (byte)(value & 0x7F) };

        for (value >>= 7; value > 0; value >>= 7) groups.Insert(0, (byte)((value & 0x7F) | 0x80));

        return [.. groups];
    }
}
