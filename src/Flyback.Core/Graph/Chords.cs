namespace Flyback.Core.Graph;

/// <summary>
/// The chords a Chord picks from and the scales an Auto Chord builds in, with the
/// arithmetic both compile to.
/// </summary>
/// <remarks>
/// Every chord is played as four notes. A triad's fourth is its root an octave up;
/// a two-note chord's third and fourth are its two notes an octave up.
/// </remarks>
public static class Chords
{
    /// <summary>Every chord a Chord knows, in the order its 'chord' input counts them.</summary>
    public static IReadOnlyList<ChordShape> All { get; } =
    [
        new("5th", [0, 7]),
        new("4th", [0, 5]),
        new("maj 3rd", [0, 4]),
        new("min 3rd", [0, 3]),

        new("maj", [0, 4, 7]),
        new("min", [0, 3, 7]),
        new("dim", [0, 3, 6]),
        new("aug", [0, 4, 8]),
        new("sus2", [0, 2, 7]),
        new("sus4", [0, 5, 7]),

        new("maj7", [0, 4, 7, 11]),
        new("dom7", [0, 4, 7, 10]),
        new("min7", [0, 3, 7, 10]),
        new("min7b5", [0, 3, 6, 10]),
        new("dim7", [0, 3, 6, 9]),
        new("minMaj7", [0, 3, 7, 11]),
        new("aug7", [0, 4, 8, 10]),
        new("augMaj7", [0, 4, 8, 11]),
        new("maj6", [0, 4, 7, 9]),
        new("min6", [0, 3, 7, 9]),
        new("add9", [0, 4, 7, 14]),
        new("minAdd9", [0, 3, 7, 14]),
        new("7sus4", [0, 5, 7, 10]),
    ];

    /// <summary>Where the major triad sits in <see cref="All"/>: what a fresh Chord plays.</summary>
    public const int Major = 4;

    /// <summary>How many notes every chord comes out as.</summary>
    public const int Notes = 4;

    /// <summary>The chord a value on the 'chord' input picks: rounded, and held to the list.</summary>
    public static int Index(float value) =>
        float.IsFinite(value) ? (int)Math.Clamp(MathF.Floor(value + 0.5f), 0f, All.Count - 1) : 0;

    /// <summary>What a 'chord' knob reads as: the name of the chord it picks, "maj" for 4.</summary>
    public static string Label(float value) => All[Index(value)].Name;

    /// <summary>
    /// The four notes of chord <paramref name="index"/>, in semitones above its root,
    /// with a short chord filled out by octaves.
    /// </summary>
    public static int[] Voiced(int index)
    {
        var shape = All[index].Intervals;

        return shape.Length switch
        {
            2 => [shape[0], shape[1], shape[0] + 12, shape[1] + 12],
            3 => [shape[0], shape[1], shape[2], shape[0] + 12],
            _ => [.. shape],
        };
    }

    private static readonly int[] Ionian = [0, 2, 4, 5, 7, 9, 11];

    private static readonly int[] HarmonicMinor = [0, 2, 3, 5, 7, 8, 11];

    private static readonly int[] MelodicMinor = [0, 2, 3, 5, 7, 9, 11];

    /// <summary>
    /// The scales an Auto Chord offers: the modes of the major scale, of the harmonic
    /// minor and of the melodic minor, then five more that are played as they stand.
    /// </summary>
    public static IReadOnlyList<ScaleMode> Scales { get; } =
    [
        Mode("ionian", "Ionian (major)", Ionian, 0),
        Mode("dorian", "Dorian", Ionian, 1),
        Mode("phrygian", "Phrygian", Ionian, 2),
        Mode("lydian", "Lydian", Ionian, 3),
        Mode("mixolydian", "Mixolydian", Ionian, 4),
        Mode("aeolian", "Aeolian (minor)", Ionian, 5),
        Mode("locrian", "Locrian", Ionian, 6),

        Mode("harmonic-minor", "Harmonic minor", HarmonicMinor, 0),
        Mode("locrian-nat6", "Locrian nat6", HarmonicMinor, 1),
        Mode("ionian-sharp5", "Ionian #5", HarmonicMinor, 2),
        Mode("dorian-sharp4", "Dorian #4", HarmonicMinor, 3),
        Mode("phrygian-dominant", "Phrygian dominant", HarmonicMinor, 4),
        Mode("lydian-sharp2", "Lydian #2", HarmonicMinor, 5),
        Mode("ultralocrian", "Ultralocrian", HarmonicMinor, 6),

        Mode("melodic-minor", "Melodic minor", MelodicMinor, 0),
        Mode("dorian-flat2", "Dorian b2", MelodicMinor, 1),
        Mode("lydian-augmented", "Lydian augmented", MelodicMinor, 2),
        Mode("lydian-dominant", "Lydian dominant", MelodicMinor, 3),
        Mode("mixolydian-flat6", "Mixolydian b6", MelodicMinor, 4),
        Mode("locrian-nat2", "Locrian nat2", MelodicMinor, 5),
        Mode("altered", "Altered", MelodicMinor, 6),

        new("harmonic-major", "Harmonic major", [0, 2, 4, 5, 7, 8, 11]),
        new("double-harmonic", "Double harmonic major", [0, 1, 4, 5, 7, 8, 11]),
        new("hungarian-minor", "Hungarian minor", [0, 2, 3, 6, 7, 8, 11]),
        new("neapolitan-minor", "Neapolitan minor", [0, 1, 3, 5, 7, 8, 11]),
        new("neapolitan-major", "Neapolitan major", [0, 1, 3, 5, 7, 9, 11]),
    ];

    /// <summary>The scale named <paramref name="id"/>, and the major scale for an id nothing answers to.</summary>
    public static ScaleMode Scale(string id) => Scales.FirstOrDefault(s => s.Id == id) ?? Scales[0];

    /// <summary>
    /// How many steps of the scale a note <paramref name="semitones"/> above its tonic
    /// is. A note off the scale counts as the nearest one on it, the higher on a tie.
    /// </summary>
    public static int Steps(ScaleMode scale, int semitones)
    {
        var classes = scale.Classes;
        var octave = (int)Math.Floor(semitones / 12.0);
        var above = semitones - 12 * octave;

        // The tonic an octave up is a candidate too, so a note just under it
        // moves up to it rather than down a whole gap.
        var (step, nearest) = (0, 0);

        for (var i = 0; i <= classes.Length; i++)
        {
            var at = i == classes.Length ? 12 : classes[i];

            if (Math.Abs(above - at) <= Math.Abs(above - nearest)) (step, nearest) = (i, at);
        }

        return octave * classes.Length + step;
    }

    /// <summary>
    /// The seventh chord a scale builds on its note <paramref name="degree"/> steps
    /// from the tonic, as semitones above the tonic. Seven steps is an octave, and a
    /// step below nought counts down from the tonic.
    /// </summary>
    public static int[] Diatonic(ScaleMode scale, int degree)
    {
        var classes = scale.Classes;
        var voiced = new int[Notes];

        for (var k = 0; k < Notes; k++)
        {
            var step = degree + 2 * k;
            var octave = (int)Math.Floor(step / (double)classes.Length);

            voiced[k] = classes[step - octave * classes.Length] + 12 * octave;
        }

        return voiced;
    }

    private static ScaleMode Mode(string id, string name, int[] parent, int from) =>
        new(id, name, [.. Enumerable.Range(0, parent.Length)
            .Select(i => ((parent[(from + i) % parent.Length] - parent[from]) % 12 + 12) % 12)]);
}
