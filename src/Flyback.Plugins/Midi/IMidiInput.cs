namespace Flyback.Plugins.Midi;

/// <summary>What a device just did. Notes and nothing else, which is what a voice can use.</summary>
/// <remarks>
/// A MIDI cable carries a great deal more than this — controllers, wheels, clock,
/// aftertouch, whole system-exclusive conversations. None of it is here, because
/// nothing above this reads any of it: the module a patch holds has four outputs
/// and every one is about a note. A backend that decodes more would be decoding
/// it for nobody, and a signal added later is a case added here rather than a
/// shape changed.
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
}

/// <summary>
/// One thing that happened on a device.
/// </summary>
/// <param name="Note">The MIDI note number, 0 to 127. Ignored for <see cref="MidiAction.AllOff"/>.</param>
/// <param name="Velocity">
/// How hard, 0 to 1 — already divided out of whatever the wire carried, because
/// 127 is a fact about MIDI and not about anything above this line.
/// </param>
public readonly record struct MidiMessage(MidiAction Action, int Note, float Velocity);

/// <summary>
/// Called when a device sends something. Called on whatever thread the backend
/// hears on, which is not the one the window runs on — so it must not block, and
/// whoever handles it is answerable for what it touches.
/// </summary>
public delegate void MidiCallback(MidiMessage message);

/// <summary>
/// One device that could be played, before anything is open.
/// </summary>
/// <param name="Id">
/// Stable, and what a saved patch stores. Deliberately not the port number: plug
/// the same keyboard into the other socket and a patch keyed by number would be
/// pointing at nothing. <see cref="MidiPorts.Named"/> is how a backend gets one
/// that survives being unplugged.
/// </param>
/// <param name="Name">What the picker shows — the device's own name, unaltered.</param>
public readonly record struct MidiPortInfo(string Id, string Name);

/// <summary>
/// A device that is open and listening. The mirror of
/// <see cref="Audio.IAudioDevice"/>: that is hardware being written to, and this
/// is hardware being read from.
/// </summary>
/// <remarks>
/// Nothing is pulled from this. A port delivers through the callback it was
/// opened with and is otherwise only there to be closed, which is the shape a
/// keyboard actually has — notes happen when a hand moves, and asking between
/// two of them would always get the same answer.
/// </remarks>
public interface IMidiPort : IDisposable
{
    /// <summary>The <see cref="MidiPortInfo.Id"/> this was opened for.</summary>
    string Id { get; }

    /// <summary>Whether it is still listening. False once closed, and once the device is gone.</summary>
    bool IsOpen { get; }
}

/// <summary>
/// A MIDI backend a plugin offers, before any device is open. Kept separate from
/// <see cref="IMidiPort"/> for the reason ADR-0025 split the sound pair: the host
/// can ask what is there, and choose between backends, without opening hardware
/// to find out.
/// </summary>
public interface IMidiInput
{
    /// <summary>Stable identifier, e.g. <c>winmm</c>.</summary>
    string Id { get; }

    /// <summary>What a person should see, e.g. <c>Windows MIDI</c>.</summary>
    string Name { get; }

    /// <summary>Higher wins when several backends are available. Ties break on <see cref="Id"/>.</summary>
    int Priority { get; }

    /// <summary>
    /// Whether this backend can run here at all. Must be answerable without
    /// opening a device and without throwing — a backend for another operating
    /// system reports <c>false</c> rather than failing in <see cref="Open"/>.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// What is plugged in right now.
    /// </summary>
    /// <remarks>
    /// Asked afresh every time rather than listed once, because devices are
    /// plugged in and pulled out while the program runs. Enumerating must not
    /// open anything and must not throw: a driver that is half-installed or a
    /// device that vanished between two calls is a shorter list, not a failure,
    /// and there is always the computer's own keyboard behind this.
    /// </remarks>
    IReadOnlyList<MidiPortInfo> Ports { get; }

    /// <summary>
    /// Opens one device and starts listening. Throws if it cannot — a device
    /// another program has taken, or one that was unplugged since
    /// <see cref="Ports"/> was read.
    /// </summary>
    IMidiPort Open(string port, MidiCallback deliver);
}

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
    /// <summary>The four bits of a status byte that say which channel, and are ignored here.</summary>
    private const byte Command = 0xF0;

    private const byte NoteOff = 0x80;

    private const byte NoteOn = 0x90;

    private const byte ControlChange = 0xB0;

    /// <summary>The panic buttons, which every device that has one sends as one of these.</summary>
    private const byte AllSoundOff = 120;

    private const byte AllNotesOff = 123;

    /// <summary>
    /// What a message means, or null where it means nothing to a voice — which is
    /// most of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every channel is heard and none is distinguished, which is the right
    /// default for a voice that plays one note at a time: a keyboard split across
    /// two channels is still one pair of hands, and a patch could not ask for a
    /// channel anyway because the module has nowhere to say so. A device sending
    /// on several at once is merged, which is what a monophonic synth on a MIDI
    /// thru chain has always done.
    /// </para>
    /// <para>
    /// Clock, active sensing, aftertouch, the wheels and every controller but the
    /// two panics come back null. They are not dropped as a shortcut — there is
    /// nothing above this that reads them, and a module that grew an output for
    /// one would be a case added here.
    /// </para>
    /// </remarks>
    public static MidiMessage? Of(byte status, byte first, byte second)
    {
        // A status byte has the top bit set; anything else is a data byte that
        // arrived where a message was expected, and means nothing on its own.
        if (status < 0x80) return null;

        // The system messages — clock, sensing, sysex, and the rest of 0xF0.
        // They carry no channel, so the mask below would otherwise read one of
        // them as whatever command happened to share its top nibble.
        if (status >= Command) return null;

        var note = (int)(first & 0x7F);
        var value = (int)(second & 0x7F);

        return (status & Command) switch
        {
            // A note-on with no force behind it is a note-off said the other way
            // round, and a great many keyboards say it that way rather than
            // sending the message that means it.
            NoteOn when value > 0 => new MidiMessage(MidiAction.Down, note, value / 127f),
            NoteOn or NoteOff => new MidiMessage(MidiAction.Up, note, 0f),

            ControlChange when note is AllSoundOff or AllNotesOff =>
                new MidiMessage(MidiAction.AllOff, 0, 0f),

            _ => null,
        };
    }
}

/// <summary>
/// How a backend turns the names it read off the machine into ids a patch can
/// keep.
/// </summary>
/// <remarks>
/// Here rather than in each backend because every backend has the same problem
/// and only one right answer to it. What comes off a driver is a display name —
/// "Launchkey Mini MK3", "USB MIDI Interface" — and what a patch needs is
/// something that is the same tomorrow, in another socket, after a reboot, and
/// is not the port number that changes when any of that happens.
/// </remarks>
public static class MidiPorts
{
    /// <summary>
    /// What every hardware id begins with.
    /// </summary>
    /// <remarks>
    /// Not decoration. <c>MidiSources.Keyboard</c> is the id <c>keyboard</c>, and
    /// a device that happens to be called "Keyboard" would otherwise take its
    /// place and swallow the one instrument that is always there.
    /// </remarks>
    public const string Prefix = "midi:";

    /// <summary>
    /// Ids for a backend's devices, in the order it enumerated them.
    /// </summary>
    /// <remarks>
    /// Two of the same model on one machine is the case that has no good answer:
    /// the driver gives both the same name, so the only thing left to tell them
    /// apart with is the order they were found in, and that is exactly what an
    /// id is not supposed to depend on. They are numbered, and the second one
    /// becomes the first if the pair is swapped over — which is wrong, and is
    /// less wrong than one of the two being unreachable.
    /// </remarks>
    public static IReadOnlyList<MidiPortInfo> Named(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var ports = new List<MidiPortInfo>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var name in names)
        {
            var display = string.IsNullOrWhiteSpace(name) ? "MIDI device" : name.Trim();
            var id = Prefix + Slug(display);

            if (seen.TryGetValue(id, out var already))
            {
                seen[id] = already + 1;
                ports.Add(new MidiPortInfo($"{id}-{already + 1}", $"{display} ({already + 1})"));
                continue;
            }

            seen[id] = 1;
            ports.Add(new MidiPortInfo(id, display));
        }

        return ports;
    }

    /// <summary>
    /// A name reduced to what will still be legible in a patch file a year from
    /// now: lower case, ASCII letters and digits, and a hyphen wherever anything
    /// else was.
    /// </summary>
    /// <remarks>
    /// Runs of hyphens collapse and the ends are trimmed, so "Launchkey Mini
    /// [MK3]" and "Launchkey  Mini  MK3" are the same device rather than two.
    /// Non-ASCII goes to a hyphen rather than being transliterated: a device with
    /// a Japanese name becomes a row of hyphens and a number, which is ugly and
    /// is still stable, and the picker shows the real name regardless.
    /// </remarks>
    private static string Slug(string name)
    {
        var slug = new System.Text.StringBuilder(name.Length);

        foreach (var c in name)
        {
            if (char.IsAsciiLetterOrDigit(c)) slug.Append(char.ToLowerInvariant(c));
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }

        while (slug.Length > 0 && slug[^1] == '-') slug.Length--;

        return slug.Length == 0 ? "device" : slug.ToString();
    }
}
