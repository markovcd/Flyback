using Avalonia.Input;
using Flyback.Core.Graph;

namespace Flyback.App.Midi;

/// <summary>
/// The keys under your hands, read as two octaves of a piano or as three octaves
/// of a scale.
/// </summary>
/// <remarks>
/// The piano is the tracker layout: white notes on the bottom row, black notes
/// above the gaps, and the upper two rows the same an octave up.
/// <para>
/// The scale puts one octave per row, home row in the middle, with the picked
/// notes from the left and the rest of the row silent, so each key is always the
/// same degree. The bottom row has ten keys, so it drops the top of an eleven- or
/// twelve-note scale.
/// </para>
/// <para>
/// Keyed by physical <see cref="Key"/>, so the layout holds on any keyboard language.
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
    /// The rows a scale is laid along, from the octave below to the octave above,
    /// each as far across as the keyboard goes.
    /// </summary>
    private static readonly Key[][] Rows =
    [
        [Key.Z, Key.X, Key.C, Key.V, Key.B, Key.N, Key.M, Key.OemComma, Key.OemPeriod, Key.OemQuestion],
        [Key.A, Key.S, Key.D, Key.F, Key.G, Key.H, Key.J, Key.K, Key.L, Key.OemSemicolon, Key.OemQuotes, Key.OemPipe],
        [Key.Q, Key.W, Key.E, Key.R, Key.T, Key.Y, Key.U, Key.I, Key.O, Key.P, Key.OemOpenBrackets, Key.OemCloseBrackets],
    ];

    /// <summary>Which row is <see cref="Bottom"/>'s octave: the home row.</summary>
    private const int HomeRow = 1;

    /// <summary>
    /// Where the bottom of the lower row sits by default: C3, an octave and a bit
    /// below middle C, which puts the two rows either side of where a melody
    /// usually is.
    /// </summary>
    private const int Bottom = 48;

    /// <summary>How far the whole layout has been moved, in octaves.</summary>
    /// <remarks>
    /// Held to what leaves every key on the layout a note that exists: this
    /// cannot go so high that a key would ask for a note past 127 or so low that
    /// one would ask for less than nothing.
    /// </remarks>
    public int Octave
    {
        get;
        set => field = Math.Clamp(value, Lowest, Highest);
    }

    /// <summary>
    /// The notes of the octave laid along each row, or null where the keys are a
    /// piano. Setting it holds the octave to what the new layout reaches.
    /// </summary>
    public IReadOnlyList<int>? Scale
    {
        get;
        set
        {
            field = value is null ? null : Pitch.Scale(value);
#pragma warning disable CA2245 // Re-clamped by its setter to the new layout's reach.
            Octave = Octave;
#pragma warning restore CA2245
        }
    }

    /// <summary>How far below <see cref="Bottom"/> the lowest key reaches: an octave, for a scale's bottom row.</summary>
    private int Below => Scale is null ? 0 : 12;

    /// <summary>
    /// How far above <see cref="Bottom"/> the highest key reaches: the piano's run
    /// carries on to the E above the upper C, and a scale's top row ends within
    /// its octave.
    /// </summary>
    private int Above => Scale is null ? 28 : 23;

    private int Lowest => -((Bottom - Below) / 12);

    private int Highest => (127 - Bottom - Above) / 12;

    /// <summary>
    /// A typist strikes every key the same, so there is nothing to measure. Not
    /// full: a patch that scales something by velocity should have somewhere left
    /// to go when a real keyboard is plugged in, and a computer key that read as
    /// the hardest possible strike would leave none.
    /// </summary>
    public const float Velocity = 0.8f;

    /// <summary>What note <paramref name="key"/> plays, or null where it plays none.</summary>
    public int? Note(Key key)
    {
        if (Scale is not { } scale)
            return Layout.TryGetValue(key, out var semitones) ? Bottom + Octave * 12 + semitones : null;

        for (var row = 0; row < Rows.Length; row++)
        {
            var place = Array.IndexOf(Rows[row], key);

            if (place >= 0)
                return place < scale.Count ? Bottom + (Octave + row - HomeRow) * 12 + scale[place] : null;
        }

        return null;
    }

    /// <summary>
    /// How the keys are laid out and what they reach, for the status bar when
    /// the layout changes — the one place it is said while nothing is selected.
    /// </summary>
    public string Described
    {
        get
        {
            if (Scale is not { } scale) return $"Keyboard: piano, {Range}.";
            if (scale.Count == 0) return "Keyboard: scale, with no notes picked, so it plays nothing.";

            var keys = string.Concat("ASDFGHJKL;'\\".Take(scale.Count));
            var notes = string.Join(" ", scale.Select(Pitch.ClassName));

            return $"Keyboard: {notes} on {keys[0]} to {keys[^1]}, Q row an octave up, Z row an octave down — {Range}.";
        }
    }

    /// <summary>
    /// What the rows currently reach, written the way the notes are — for saying
    /// on the status bar when the octave moves, since rows of letters give no
    /// clue where they are.
    /// </summary>
    public string Range
    {
        get
        {
            var bottom = Bottom + Octave * 12;

            if (Scale is not { } scale)
                return $"{Pitch.Name(bottom)} to {Pitch.Name(bottom + Above)}";

            if (scale.Count == 0) return "nothing, since the scale has no notes picked";

            var top = scale[Math.Min(scale.Count, Rows[^1].Length) - 1];

            return $"{Pitch.Name(bottom - 12 + scale[0])} to {Pitch.Name(bottom + 12 + top)}";
        }
    }
}
