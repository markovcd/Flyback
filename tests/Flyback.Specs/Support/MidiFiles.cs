namespace Flyback.Specs.Support;

/// <summary>Standard MIDI files made for a scenario, at 120 beats a minute so a second is 960 ticks.</summary>
public static class MidiFiles
{
    private const int TicksPerSecond = 960;

    /// <summary>Writes a file of <paramref name="notes"/> to <paramref name="path"/>, each a key, when it starts and how long it lasts.</summary>
    public static void Write(string path, params (int Note, double Start, double Length)[] notes)
    {
        var events = notes
            .SelectMany(n => new[]
            {
                (Tick: (int)Math.Round(n.Start * TicksPerSecond), Bytes: new byte[] { 0x90, (byte)n.Note, 100 }),
                (Tick: (int)Math.Round((n.Start + n.Length) * TicksPerSecond), Bytes: new byte[] { 0x80, (byte)n.Note, 0 }),
            })
            .OrderBy(e => e.Tick)
            .ToList();

        var track = new List<byte>();
        var last = 0;

        foreach (var (tick, bytes) in events)
        {
            track.AddRange(Quantity(tick - last));
            track.AddRange(bytes);
            last = tick;
        }

        track.AddRange([0, 0xFF, 0x2F, 0]);

        List<byte> file = [.. "MThd"u8.ToArray(), 0, 0, 0, 6, 0, 0, 0, 1, 0x01, 0xE0, .. "MTrk"u8.ToArray()];

        file.AddRange(BitConverter.GetBytes(track.Count).Reverse());
        file.AddRange(track);

        File.WriteAllBytes(path, [.. file]);
    }

    private static byte[] Quantity(int value)
    {
        var groups = new List<byte> { (byte)(value & 0x7F) };

        for (value >>= 7; value > 0; value >>= 7) groups.Insert(0, (byte)((value & 0x7F) | 0x80));

        return [.. groups];
    }
}
