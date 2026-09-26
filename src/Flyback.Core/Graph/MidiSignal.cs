namespace Flyback.Core.Graph;

/// <summary>
/// The signals one instrument carries, and how a program names them.
/// </summary>
/// <remarks>
/// These strings are the join between a module compiled at one moment and a
/// hand moving at another — see <see cref="Compile.OpCode.LoadLive"/>. Both ends
/// spell them from here.
/// </remarks>
public static class MidiSignal
{
    /// <summary>How many clock ticks MIDI sends a beat, as it settled on in 1983.</summary>
    public const int TicksPerBeat = 24;

    /// <summary>The note being held, as a MIDI number — see <see cref="Pitch"/>.</summary>
    public const string Pitch = "pitch";

    /// <summary>One while a key is down, nought while none is.</summary>
    public const string Gate = "gate";

    /// <summary>How hard the note was struck, 0 to 1.</summary>
    public const string Velocity = "velocity";

    /// <summary>
    /// How many notes have been struck since the program started. A count rather
    /// than a pulse, which is what makes a retrigger possible: nothing outside the
    /// program can hand it a signal high for exactly one evaluation, and a number
    /// that only goes up can be differenced inside it by each path at its own rate.
    /// </summary>
    public const string Strikes = "strikes";

    /// <summary>
    /// What one signal of one instrument is called in
    /// <c>Compile.CompiledPatch.LiveInputs</c>.
    /// </summary>
    public static string Key(string source, string signal) => Key(source, 1, signal);

    /// <summary>
    /// The signal of one indexed voice. Index one keeps the original key shape so
    /// patches written before indexed MIDI continue to receive their first voice.
    /// </summary>
    public static string Key(string source, int index, string signal) =>
        index == 0 ? $"{source}/auto/{signal}" : index == 1 ? $"{source}/{signal}" : $"{source}/{index}/{signal}";

    public static string AutoKey(string source, Guid node, string signal) =>
        $"{source}/auto/{node:N}/{signal}";

    /// <summary>
    /// One channel of an instrument, named as an instrument of its own, so a
    /// module listening to channel 3 of a box and one listening to the whole box
    /// keep separate voices. Channel 0 is the whole box.
    /// </summary>
    /// <remarks>
    /// A suffix rather than a segment of the key, because everything that reads a
    /// key by its segments — the automatic voices most of all — then goes on
    /// working without knowing channels exist. An id is a slug or the keyboard,
    /// so the '@' cannot occur in one.
    /// </remarks>
    public static string Channeled(string source, int channel) =>
        channel <= 0 ? source : $"{source}@{channel}";

    /// <summary>The instrument a key belongs to, whichever channel and signal it names.</summary>
    public static string SourceOf(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var slash = key.IndexOf('/');
        var source = slash < 0 ? key : key[..slash];
        var at = source.IndexOf('@');

        return at < 0 ? source : source[..at];
    }

    /// <summary>Beats since the instrument pressed Start, as of its latest tick — see <c>MidiClock.Beat</c>.</summary>
    public const string Beat = "beat";

    /// <summary>Beats a second while it runs, nought while it is stopped.</summary>
    public const string Rate = "rate";

    /// <summary>The tempo it is sending, in beats a minute.</summary>
    public const string Bpm = "bpm";

    /// <summary>One between Start and Stop.</summary>
    public const string Running = "running";

    /// <summary>How many times it has pressed Start, a count for the reason <see cref="Strikes"/> is one.</summary>
    public const string Starts = "starts";

    /// <summary>
    /// What one signal of one instrument's clock is called. An instrument has one
    /// clock however many voices it has, so there is no index.
    /// </summary>
    public static string ClockKey(string source, string signal) => $"{source}/clock/{signal}";
}
