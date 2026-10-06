using Flyback.Core.Compile;

namespace Flyback.Engine.Render;

/// <summary>
/// A standard MIDI file, read into notes placed in seconds.
/// </summary>
/// <remarks>
/// Format 0, 1 and 2 are read alike, every track's notes laid over one tempo map
/// (a format 2 file's tracks are songs of their own and play together here). Never
/// throws: a file is hostile until read, so every length is held to what the bytes
/// could carry, a truncated track gives what it had, and a size or a duration no
/// patch could use is refused by name (ADR-0019 takes no packages, so nothing here
/// leans on one).
/// </remarks>
public static class MidiFileReader
{
    /// <summary>The largest file read, in bytes.</summary>
    public const int MostBytes = 8 * 1024 * 1024;

    /// <summary>The most notes read from one file.</summary>
    public const int MostNotes = 200_000;

    /// <summary>The longest file read, in seconds: each voice of it is four tables of this many thousand.</summary>
    public const float MostSeconds = 1200f;

    private const double DefaultTempo = 500_000d;

    /// <param name="path">The file.</param>
    /// <param name="fault">Why nothing came back, or <see cref="MidiFault.None"/>.</param>
    public static LoadedMidi? Read(string path, out MidiFault fault)
    {
        try
        {
            var info = new FileInfo(path);

            if (!info.Exists)
            {
                fault = MidiFault.Missing;
                return null;
            }

            if (info.Length > MostBytes)
            {
                fault = MidiFault.TooBig;
                return null;
            }

            return Read(File.ReadAllBytes(path), out fault);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            fault = MidiFault.Missing;
            return null;
        }
    }

    /// <param name="bytes">The file's bytes, from the start.</param>
    /// <inheritdoc cref="Read(string, out MidiFault)"/>
    public static LoadedMidi? Read(byte[] bytes, out MidiFault fault)
    {
        fault = MidiFault.None;

        if (bytes.Length > MostBytes)
        {
            fault = MidiFault.TooBig;
            return null;
        }

        if (!Header(bytes, out var division, out var tracks, out var at))
        {
            fault = MidiFault.NotMidi;
            return null;
        }

        var tempos = new List<(long Tick, double Micros)>();
        var events = new List<Pressed>();

        for (var track = 0; track < tracks && at + 8 <= bytes.Length; track++)
        {
            var length = (long)ReadBigEndian(bytes, at + 4, 4);
            var body = at + 8;
            var end = (int)Math.Min(bytes.Length, body + length);
            var isTrack = bytes[at] == 'M' && bytes[at + 1] == 'T' && bytes[at + 2] == 'r' && bytes[at + 3] == 'k';

            at = end;

            // A chunk of some other kind is skipped, as the format asks.
            if (!isTrack)
            {
                track--;
                continue;
            }

            if (!Track(bytes, body, end, tempos, events))
            {
                fault = MidiFault.TooBig;
                return null;
            }
        }

        return Placed(events, tempos, division, ref fault);
    }

    private static bool Header(byte[] bytes, out Division division, out int tracks, out int next)
    {
        division = default;
        tracks = 0;
        next = 0;

        if (bytes.Length < 14 || bytes[0] != 'M' || bytes[1] != 'T' || bytes[2] != 'h' || bytes[3] != 'd') return false;

        var length = (int)ReadBigEndian(bytes, 4, 4);

        if (length < 6 || length > bytes.Length - 8) return false;

        tracks = (int)ReadBigEndian(bytes, 10, 2);
        var raw = (int)ReadBigEndian(bytes, 12, 2);

        if ((raw & 0x8000) == 0)
        {
            if (raw == 0) return false;

            division = new Division(raw, 0d);
        }
        else
        {
            // SMPTE: a negative frame rate in the high byte, ticks to a frame in the low.
            var frames = -(sbyte)(raw >> 8);
            var perFrame = raw & 0xFF;

            if (frames <= 0 || perFrame == 0) return false;

            division = new Division(0, (frames == 29 ? 29.97d : frames) * perFrame);
        }

        next = 8 + length;
        return true;
    }

    /// <summary>
    /// One track's events into <paramref name="events"/> and the tempo changes into
    /// <paramref name="tempos"/>. False where the file holds more notes than it may.
    /// </summary>
    private static bool Track(
        byte[] bytes, int at, int end, List<(long Tick, double Micros)> tempos, List<Pressed> events)
    {
        long tick = 0;
        var status = 0;

        while (at < end)
        {
            if (!Variable(bytes, ref at, end, out var delta)) return true;

            tick += delta;

            if (at >= end) return true;

            var first = bytes[at];

            // A byte below 0x80 is data under the status before it: running status.
            if (first >= 0x80)
            {
                status = first;
                at++;
            }
            else if (status == 0) return true;

            if (status == 0xFF)
            {
                if (at >= end) return true;

                var type = bytes[at++];

                if (!Variable(bytes, ref at, end, out var size) || size > end - at) return true;

                if (type == 0x51 && size == 3)
                    tempos.Add((tick, (bytes[at] << 16) | (bytes[at + 1] << 8) | bytes[at + 2]));

                at += (int)size;

                if (type == 0x2F) return true;

                status = 0;
            }
            else if (status is 0xF0 or 0xF7)
            {
                if (!Variable(bytes, ref at, end, out var size) || size > end - at) return true;

                at += (int)size;
                status = 0;
            }
            else if (status >= 0xF1)
            {
                // System messages have no place in a track.
                return true;
            }
            else
            {
                var kind = status & 0xF0;
                var wanted = kind is 0xC0 or 0xD0 ? 1 : 2;

                if (at + wanted > end) return true;

                if (kind is 0x80 or 0x90)
                {
                    if (events.Count >= MostNotes * 2) return false;

                    var on = kind == 0x90 && bytes[at + 1] != 0;
                    events.Add(new Pressed(tick, (status & 0x0F) + 1, bytes[at] & 0x7F, on, (bytes[at + 1] & 0x7F) / 127f));
                }

                at += wanted;
            }
        }

        return true;
    }

    /// <summary>The notes the presses make, in seconds, or nothing where there are none.</summary>
    private static LoadedMidi? Placed(
        List<Pressed> events, List<(long Tick, double Micros)> tempos, Division division, ref MidiFault fault)
    {
        // Sorted by tick, a change at the same tick as an earlier one replacing it.
        tempos.Sort((a, b) => a.Tick.CompareTo(b.Tick));

        var open = new Dictionary<(int Channel, int Note), Queue<Pressed>>();
        var notes = new List<MidiNote>();

        // Off before on at one tick, so a note ending where another begins ends first.
        foreach (var press in events.OrderBy(e => e.Tick).ThenBy(e => e.On))
        {
            var key = (press.Channel, press.Note);

            if (press.On)
            {
                if (!open.TryGetValue(key, out var queue)) open[key] = queue = new Queue<Pressed>();

                queue.Enqueue(press);
            }
            else if (open.TryGetValue(key, out var queue) && queue.Count > 0)
            {
                var start = queue.Dequeue();

                notes.Add(Note(start, Seconds(press.Tick, tempos, division)));
            }
        }

        // What was never let go ends with the file.
        var finish = events.Count == 0 ? 0L : events.Max(e => e.Tick);

        foreach (var queue in open.Values)
            foreach (var start in queue)
                notes.Add(Note(start, Seconds(finish, tempos, division)));

        if (notes.Count == 0)
        {
            fault = MidiFault.Empty;
            return null;
        }

        if (notes.Count > MostNotes)
        {
            fault = MidiFault.TooBig;
            return null;
        }

        var last = notes.Max(n => n.End);

        if (last > MostSeconds)
        {
            fault = MidiFault.TooLong;
            return null;
        }

        return new LoadedMidi(notes, last);

        MidiNote Note(Pressed start, double end) =>
            new((float)Seconds(start.Tick, tempos, division), (float)Math.Max(end, Seconds(start.Tick, tempos, division)), start.Note, start.Velocity, start.Channel);
    }

    /// <summary>A tick in seconds, through every tempo change before it.</summary>
    private static double Seconds(long tick, List<(long Tick, double Micros)> tempos, Division division)
    {
        if (division.TicksPerSecond > 0d) return tick / division.TicksPerSecond;

        var seconds = 0d;
        var at = 0L;
        var tempo = DefaultTempo;

        foreach (var (changed, micros) in tempos)
        {
            if (changed >= tick) break;

            seconds += (changed - at) * tempo / 1e6d / division.PerQuarter;
            at = changed;
            tempo = micros;
        }

        return seconds + (tick - at) * tempo / 1e6d / division.PerQuarter;
    }

    /// <summary>A variable-length quantity, at most four bytes, false where the bytes end first.</summary>
    private static bool Variable(byte[] bytes, ref int at, int end, out long value)
    {
        value = 0;

        for (var i = 0; i < 4; i++)
        {
            if (at >= end) return false;

            var next = bytes[at++];

            value = (value << 7) | (uint)(next & 0x7F);

            if ((next & 0x80) == 0) return true;
        }

        return false;
    }

    private static uint ReadBigEndian(byte[] bytes, int at, int count)
    {
        uint value = 0;

        for (var i = 0; i < count; i++) value = (value << 8) | bytes[at + i];

        return value;
    }

    /// <summary>How a tick is timed: a count to the quarter note, or a count to the second.</summary>
    private readonly record struct Division(int PerQuarter, double TicksPerSecond);

    private readonly record struct Pressed(long Tick, int Channel, int Note, bool On, float Velocity);
}
