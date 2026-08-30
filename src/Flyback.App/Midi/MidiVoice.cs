using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.App.Midi;

/// <summary>
/// One instrument's worth of what a MIDI In reads: the note being held, whether
/// anything is held at all, how hard it was struck, and how many notes have been
/// struck since the program started.
/// </summary>
/// <remarks>
/// One indexed voice. Polyphony is supplied by <see cref="MidiHub"/>, which owns
/// a fixed set of these and assigns each new note to the first free voice.
/// <para>
/// Written on the thread the keys arrive on and read on the thread that plays,
/// which is what <see cref="LiveValues"/> is built for: single floats, no lock,
/// and at worst one evaluation seeing a new note's pitch beside an old note's
/// gate.
/// </para>
/// </remarks>
internal sealed class MidiVoice
{
    /// <summary>
    /// What is down, oldest first, with what each was struck with. Bounded
    /// because a stuck note-on from a device that never sends the matching off
    /// should not grow a list for the rest of the session.
    /// </summary>
    private readonly List<(int Note, float Velocity)> held = [];

    private const int MostHeld = 32;

    /// <summary>
    /// What a voice reads before anything has been played, which is nothing.
    /// </summary>
    /// <remarks>
    /// Nought rather than a note in the middle somewhere, so that a keyboard
    /// nobody has touched and a program nobody is playing at all read the same —
    /// see <see cref="LiveValues"/>, where an input with no block behind it is
    /// nought. Resting at middle C was tried first and is the more comfortable
    /// number, and it makes the picture on screen differ from the picture the
    /// same patch exports: two answers to "nobody is playing" is one too many,
    /// and the one that costs nothing to say is nought.
    /// <para>
    /// Nothing is protected by a friendlier resting pitch anyway. A patch reading
    /// this without a gate is silent at note nought and inaudible near it, and
    /// the first key pressed moves the pitch without a click, because an
    /// oscillator carries its phase across a change of frequency (ADR-0030).
    /// </para>
    /// </remarks>
    public float Pitch { get; private set; }

    public float Gate { get; private set; }

    public float Velocity { get; private set; }

    /// <summary>
    /// How many notes have been struck. Only ever goes up — see
    /// <see cref="MidiSignal.Strikes"/>, where it is differenced back into an
    /// edge by the program rather than by anything here.
    /// </summary>
    public float Strikes { get; private set; }

    /// <summary>
    /// A key pressed. Velocity is 0 to 1; a device that sends nought for a
    /// note-on means a note-off, which is what
    /// <see cref="Up"/> is for and is handled there.
    /// </summary>
    public void Down(int note, float velocity)
    {
        note = Math.Clamp(note, 0, 127);

        // The same key twice without a release in between is keyboard auto-repeat
        // (or a duplicate device message), not a new strike.
        if (held.Any(entry => entry.Note == note)) return;

        if (held.Count >= MostHeld) held.RemoveAt(0);

        held.Add((note, Math.Clamp(velocity, 0f, 1f)));

        Take(held[^1]);
        Strikes++;
    }

    /// <summary>
    /// A key let go. The voice falls back to whatever is still down rather than
    /// closing, which is what makes a legato run one note rather than several.
    /// </summary>
    public void Up(int note)
    {
        held.RemoveAll(entry => entry.Note == note);

        if (held.Count == 0)
        {
            Gate = 0f;
            return;
        }

        // Not a fresh strike: nothing was played, something stopped being. So
        // the count is left alone and no envelope is restarted.
        Take(held[^1]);
    }

    /// <summary>
    /// Everything let go at once — what a window losing the focus means, and the
    /// only cure for a key released somewhere this never heard about.
    /// </summary>
    /// <remarks>
    /// The pitch is left where it was rather than reset. A note that has just
    /// ended should decay at the pitch it was played at, and an envelope's
    /// release is still running when this is called.
    /// </remarks>
    public void Silence()
    {
        held.Clear();
        Gate = 0f;
    }

    /// <summary>Whether anything is held, which is what a stuck note looks like from outside.</summary>
    public bool Playing => held.Count > 0;

    /// <summary>Puts this indexed voice into a program's live-input block.</summary>
    public void WriteTo(LiveValues block, string source, int index)
        => WriteTo(block, signal => MidiSignal.Key(source, index, signal));

    public void WriteTo(LiveValues block, Func<string, string> key)
    {
        block.Set(key(MidiSignal.Pitch), Pitch);
        block.Set(key(MidiSignal.Gate), Gate);
        block.Set(key(MidiSignal.Velocity), Velocity);
        block.Set(key(MidiSignal.Strikes), Strikes);
    }

    private void Take((int Note, float Velocity) entry)
    {
        Pitch = entry.Note;
        Velocity = entry.Velocity;
        Gate = 1f;
    }
}
