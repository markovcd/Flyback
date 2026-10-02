using Flyback.Core.Graph;

namespace Flyback.Engine.Graph;

/// <summary>
/// The keys under your hands, read as two octaves of a piano or as three octaves
/// of a scale.
/// </summary>
/// <remarks>
/// The piano is the tracker layout: white notes on the bottom row, black notes
/// above the gaps, and the upper two rows the same an octave up.
/// <para>
/// The scale puts one octave per row, home row in the middle, from the tonic on
/// the left and the rest of the row silent, so each key is always the same degree.
/// </para>
/// <para>
/// Keyed by the physical key's name as a browser gives it (<c>KeyZ</c>, <c>Digit2</c>,
/// <c>Comma</c>), so the layout holds on any keyboard language and in any shell.
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
    private static readonly Dictionary<string, int> Layout = new(StringComparer.Ordinal)
    {
        // Lower octave: the bottom two rows.
        ["KeyZ"] = 0,
        ["KeyS"] = 1,
        ["KeyX"] = 2,
        ["KeyD"] = 3,
        ["KeyC"] = 4,
        ["KeyV"] = 5,
        ["KeyG"] = 6,
        ["KeyB"] = 7,
        ["KeyH"] = 8,
        ["KeyN"] = 9,
        ["KeyJ"] = 10,
        ["KeyM"] = 11,
        ["Comma"] = 12,
        ["KeyL"] = 13,
        ["Period"] = 14,
        ["Semicolon"] = 15,
        ["Slash"] = 16,

        // Upper octave: the two rows above, starting an octave up.
        ["KeyQ"] = 12,
        ["Digit2"] = 13,
        ["KeyW"] = 14,
        ["Digit3"] = 15,
        ["KeyE"] = 16,
        ["KeyR"] = 17,
        ["Digit5"] = 18,
        ["KeyT"] = 19,
        ["Digit6"] = 20,
        ["KeyY"] = 21,
        ["Digit7"] = 22,
        ["KeyU"] = 23,
        ["KeyI"] = 24,
        ["Digit9"] = 25,
        ["KeyO"] = 26,
        ["Digit0"] = 27,
        ["KeyP"] = 28,
    };

    /// <summary>
    /// The rows a scale is laid along, from the octave below to the octave above,
    /// each as far across as the keyboard goes.
    /// </summary>
    private static readonly string[][] Rows =
    [
        ["KeyZ", "KeyX", "KeyC", "KeyV", "KeyB", "KeyN", "KeyM", "Comma", "Period", "Slash"],
        ["KeyA", "KeyS", "KeyD", "KeyF", "KeyG", "KeyH", "KeyJ", "KeyK", "KeyL", "Semicolon", "Quote", "Backslash"],
        ["KeyQ", "KeyW", "KeyE", "KeyR", "KeyT", "KeyY", "KeyU", "KeyI", "KeyO", "KeyP", "BracketLeft", "BracketRight"],
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
    /// The scale laid along each row, or null where the keys are a piano. Setting
    /// it holds the octave to what the new layout reaches.
    /// </summary>
    public KeyboardScale? Scale
    {
        get;
        set
        {
            field = value;
#pragma warning disable CA2245 // Re-clamped by its setter to the new layout's reach.
            Octave = Octave;
#pragma warning restore CA2245
        }
    }

    /// <summary>How far below <see cref="Bottom"/> the lowest key reaches: an octave, for a scale's bottom row.</summary>
    private int Below => Scale is null ? 0 : 12;

    /// <summary>
    /// How far above <see cref="Bottom"/> the highest key reaches: the piano's run
    /// carries on to the E above the upper C, and a scale's top row ends where its
    /// octave above the tonic does.
    /// </summary>
    private int Above => Scale is { } scale ? 12 + scale.Row[^1] : 28;

    private int Lowest => -((Bottom - Below) / 12);

    private int Highest => (127 - Bottom - Above) / 12;

    /// <summary>
    /// A typist strikes every key the same, so there is nothing to measure. Not
    /// full: a patch that scales something by velocity should have somewhere left
    /// to go when a real keyboard is plugged in, and a computer key that read as
    /// the hardest possible strike would leave none.
    /// </summary>
    public const float Velocity = 0.8f;

    /// <summary>What note the key named <paramref name="key"/> plays, or null where it plays none.</summary>
    public int? Note(string key)
    {
        if (Scale is not { } scale)
            return Layout.TryGetValue(key, out var semitones) ? Bottom + Octave * 12 + semitones : null;

        var notes = scale.Row;

        for (var row = 0; row < Rows.Length; row++)
        {
            var place = Array.IndexOf(Rows[row], key);

            if (place >= 0)
                return place < notes.Count ? Bottom + (Octave + row - HomeRow) * 12 + notes[place] : null;
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

            var keys = string.Concat("ASDFGHJKL;'\\".Take(scale.Row.Count));

            return $"Keyboard: {scale.Name} on {keys[0]} to {keys[^1]}, Q row an octave up, Z row an octave down — {Range}.";
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

            var notes = scale.Row;

            return $"{Pitch.Name(bottom - 12 + notes[0])} to {Pitch.Name(bottom + 12 + notes[^1])}";
        }
    }
}
