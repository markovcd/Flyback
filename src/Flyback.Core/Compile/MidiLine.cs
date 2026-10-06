namespace Flyback.Core.Compile;

/// <summary>
/// What one voice of a MIDI file plays, as four tables read by time: the pitch,
/// the gate, the velocity and a short pulse for each note struck.
/// </summary>
/// <remarks>
/// Tables rather than events because a program is a pure function of the clock
/// (ADR-0005): whatever it plays is read at the moment it asks, so the speakers,
/// the picture, an export and a seek all hear the same file. At <see cref="Rate"/>
/// a read interpolates over a millisecond, which is what lets a gate rise and fall
/// without a click.
/// </remarks>
public sealed class MidiLine
{
    /// <summary>How many table entries a second holds.</summary>
    public const int Rate = 1000;

    /// <summary>The most notes held at once that have a voice of their own.</summary>
    public const int Voices = 8;

    /// <summary>How long a note's trigger stays high, in table entries.</summary>
    private const int PulseWidth = 5;

    private MidiLine(LoadedSample pitch, LoadedSample gate, LoadedSample velocity, LoadedSample trigger, int notes, float seconds)
    {
        Pitch = pitch;
        Gate = gate;
        Velocity = velocity;
        Trigger = trigger;
        Notes = notes;
        Seconds = seconds;
    }

    /// <summary>The key held, in notes, kept after the key is let go.</summary>
    public LoadedSample Pitch { get; }

    /// <summary>High while a key is held, and low for an instant as each note lands.</summary>
    public LoadedSample Gate { get; }

    /// <summary>How hard the key was struck, 0 to 1, kept after it is let go.</summary>
    public LoadedSample Velocity { get; }

    /// <summary>A pulse at each note struck.</summary>
    public LoadedSample Trigger { get; }

    /// <summary>How long the file runs, in seconds.</summary>
    public float Seconds { get; }

    /// <summary>How many notes of the file this voice may hear, which is not how many it plays.</summary>
    public int Notes { get; }

    /// <summary>
    /// Plays <paramref name="notes"/> through one voice: <paramref name="voice"/> 1 to
    /// <see cref="Voices"/> is that one of the notes held at once, 0 is a single voice
    /// that always plays the newest key held.
    /// </summary>
    /// <param name="notes">Every note of the file.</param>
    /// <param name="seconds">How long the file runs.</param>
    /// <param name="channel">0 hears every channel, 1 to 16 only that one.</param>
    internal static MidiLine Of(IReadOnlyList<MidiNote> notes, float seconds, int voice, int channel)
    {
        var heard = notes
            .Where(note => channel == 0 || note.Channel == channel)
            .OrderBy(note => note.Start)
            .ThenBy(note => note.Note)
            .ToList();

        var length = (int)Math.Ceiling(Math.Max(0f, seconds) * Rate) + PulseWidth + 2;
        var tables = new Tables(length, seconds);
        var slots = voice == 0 ? 1 : Voices;
        var target = Math.Max(0, voice - 1);
        var held = Enumerable.Range(0, slots).Select(_ => new List<int>()).ToArray();
        var owner = new int[heard.Count];

        foreach (var (index, on, id) in Events(heard))
        {
            var note = heard[id];

            if (on)
            {
                // The first slot with nothing held, else the first one over what it held.
                var slot = Math.Max(0, Array.FindIndex(held, keys => keys.Count == 0));

                held[slot].Add(id);
                owner[id] = slot;

                if (slot == target) tables.Struck(index, note);
            }
            else
            {
                var slot = owner[id];

                held[slot].Remove(id);

                if (slot != target) continue;

                if (held[slot].Count == 0) tables.Released(index);
                else tables.Fell(index, heard[held[slot][^1]]);
            }
        }

        return tables.Finish(heard.Count);
    }

    /// <summary>
    /// Every note on and off in order, a note let go ahead of one struck at the same
    /// moment, and a note that would end where it began lasting one entry.
    /// </summary>
    private static IEnumerable<(int Index, bool On, int Id)> Events(List<MidiNote> heard)
    {
        var events = new List<(int Index, bool On, int Id)>(heard.Count * 2);

        for (var id = 0; id < heard.Count; id++)
        {
            var on = (int)Math.Round(heard[id].Start * Rate);
            var off = Math.Max(on + 1, (int)Math.Round(heard[id].End * Rate));

            events.Add((on, true, id));
            events.Add((off, false, id));
        }

        return events.OrderBy(e => e.Index).ThenBy(e => e.On).ThenBy(e => e.Id);
    }

    /// <summary>The four tables of one voice, and the state they are written from.</summary>
    private sealed class Tables(int length, float seconds)
    {
        private readonly float[] pitch = new float[length];
        private readonly float[] gate = new float[length];
        private readonly float[] velocity = new float[length];
        private readonly float[] trigger = new float[length];

        private int filled;
        private bool struck;
        private float nowPitch = 60f;
        private float nowGate;
        private float nowVelocity;

        /// <summary>A note lands: the gate drops for an entry so a legato note retriggers.</summary>
        public void Struck(int index, MidiNote note)
        {
            Fill(index);

            nowPitch = note.Note;
            nowVelocity = note.Velocity;

            // Before the first note the pitch is that note's, so the instrument is
            // tuned before it is played.
            if (!struck) Array.Fill(pitch, nowPitch, 0, Math.Min(index, length));

            struck = true;

            Put(index, nowPitch, 0f, nowVelocity);
            Array.Fill(trigger, 1f, Math.Min(index, length), Math.Min(PulseWidth, Math.Max(0, length - index)));

            nowGate = 1f;
        }

        /// <summary>The last note held is let go: the gate closes and the pitch stays.</summary>
        public void Released(int index)
        {
            Fill(index);

            nowGate = 0f;
        }

        /// <summary>A note let go leaves an older one still held, which sounds again without retriggering.</summary>
        public void Fell(int index, MidiNote note)
        {
            Fill(index);

            nowPitch = note.Note;
            nowVelocity = note.Velocity;
        }

        public MidiLine Finish(int notes)
        {
            Fill(length);

            return new MidiLine(
                new LoadedSample(pitch, Rate),
                new LoadedSample(gate, Rate),
                new LoadedSample(velocity, Rate),
                new LoadedSample(trigger, Rate),
                notes,
                seconds);
        }

        private void Put(int index, float p, float g, float v)
        {
            if ((uint)index >= (uint)length) return;

            pitch[index] = p;
            gate[index] = g;
            velocity[index] = v;
            filled = index + 1;
        }

        private void Fill(int upTo)
        {
            upTo = Math.Min(upTo, length);

            for (; filled < upTo; filled++)
            {
                pitch[filled] = nowPitch;
                gate[filled] = nowGate;
                velocity[filled] = nowVelocity;
            }
        }
    }
}
