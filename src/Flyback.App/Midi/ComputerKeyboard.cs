using Avalonia.Input;
using Flyback.Core.Graph;

namespace Flyback.App.Midi;

/// <summary>
/// The keys under your hands, read as two octaves of a piano.
/// </summary>
/// <remarks>
/// The tracker layout, which is the one arrangement of a typewriter that
/// everybody who has played one already knows: the bottom row is the white notes
/// and the row above holds the black ones over the gaps, and the two rows above
/// that are the same shape an octave up. So Z is C, S is C sharp, X is D, and Q
/// is the C above them all.
/// <para>
/// Keyed by <see cref="Key"/> rather than by the character typed, and that is
/// deliberate: a layout is a physical thing, and it should stay a piano on a
/// keyboard whose letters are somewhere else. What it costs is that the letters
/// printed on some keyboards will not match the notes — which is the same trade
/// every game that uses WASD makes, and the right way round for an instrument.
/// </para>
/// </remarks>
internal sealed class ComputerKeyboard
{
    /// <summary>
    /// Which note each key is, counted in semitones from the bottom of the lower
    /// row. Two rows of twelve and the octave above them, plus the three keys
    /// that carry the run on past the top of each row — the way a tracker lets
    /// you reach the next C without leaving the row.
    /// </summary>
    private static readonly Dictionary<Key, int> Layout = new()
    {
        // Lower octave: the bottom two rows.
        [Key.Z] = 0,
        [Key.S] = 1,
        [Key.X] = 2,
        [Key.D] = 3,
        [Key.C] = 4,
        [Key.V] = 5,
        [Key.G] = 6,
        [Key.B] = 7,
        [Key.H] = 8,
        [Key.N] = 9,
        [Key.J] = 10,
        [Key.M] = 11,
        [Key.OemComma] = 12,
        [Key.L] = 13,
        [Key.OemPeriod] = 14,
        [Key.OemSemicolon] = 15,
        [Key.OemQuestion] = 16,

        // Upper octave: the two rows above, starting an octave up.
        [Key.Q] = 12,
        [Key.D2] = 13,
        [Key.W] = 14,
        [Key.D3] = 15,
        [Key.E] = 16,
        [Key.R] = 17,
        [Key.D5] = 18,
        [Key.T] = 19,
        [Key.D6] = 20,
        [Key.Y] = 21,
        [Key.D7] = 22,
        [Key.U] = 23,
        [Key.I] = 24,
        [Key.D9] = 25,
        [Key.O] = 26,
        [Key.D0] = 27,
        [Key.P] = 28,
    };

    /// <summary>
    /// Where the bottom of the lower row sits by default: C3, an octave and a bit
    /// below middle C, which puts the two rows either side of where a melody
    /// usually is.
    /// </summary>
    private const int Bottom = 48;

    /// <summary>How far the whole layout has been moved, in octaves.</summary>
    /// <remarks>
    /// Held to what leaves every key on the layout a note that exists. The top of
    /// the run is 28 semitones above the bottom, so this cannot go so high that a
    /// key would ask for a note past 127 or so low that one would ask for less
    /// than nothing.
    /// </remarks>
    public int Octave
    {
        get;
        set => field = Math.Clamp(value, Lowest, Highest);
    }

    private const int Lowest = -Bottom / 12;

    private const int Highest = (127 - Bottom - 28) / 12;

    /// <summary>
    /// A typist strikes every key the same, so there is nothing to measure. Not
    /// full: a patch that scales something by velocity should have somewhere left
    /// to go when a real keyboard is plugged in, and a computer key that read as
    /// the hardest possible strike would leave none.
    /// </summary>
    public const float Velocity = 0.8f;

    /// <summary>What note <paramref name="key"/> plays, or null where it plays none.</summary>
    public int? Note(Key key) =>
        Layout.TryGetValue(key, out var semitones)
            ? Bottom + Octave * 12 + semitones
            : null;

    /// <summary>
    /// What the two rows currently reach, written the way the notes are — for
    /// saying on the status bar when the octave moves, since two rows of letters
    /// give no clue where they are.
    /// </summary>
    public string Range =>
        $"{Pitch.Name(Bottom + Octave * 12)} to {Pitch.Name(Bottom + Octave * 12 + 28)}";
}
