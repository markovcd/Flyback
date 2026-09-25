using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    public const string ChordTypeId = "audio.chord";

    public const string AutoChordTypeId = "audio.autochord";

    /// <summary>What an Auto Chord's scale is filed under.</summary>
    public const string AutoChordStateKey = "autochord";

    public const string AutoChordScaleField = "scale";

    /// <summary>The four frequencies both chord modules give.</summary>
    private static PortSpec[] ChordOutputs() =>
    [
        Num("hz1") with { Help = "The root, for an oscillator's 'freq'." },
        Num("hz2") with { Help = "The second note up." },
        Num("hz3") with { Help = "The third note up." },
        Num("hz4") with { Help = "The fourth note up." },
    ];

    /// <summary>A root note and a chord picked by number, as four frequencies.</summary>
    /// <remarks>
    /// The chord is a signal, so a sequencer can step through a progression. Which
    /// one plays is a sum of steps over the list, as a sequencer picks its note.
    /// </remarks>
    private static NodeDef Chord() => new(
        ChordTypeId, "Chord", ModuleCategories.Pitch,
        [
            Pitched("note", 60f) with { Help = "The root, as a Note's 'note': a signal is snapped to the nearest semitone." },
            new PortSpec("chord", PortKind.Scalar, Chords.Major, 0f, Chords.All.Count - 1, Display: PortDisplay.Chord)
            {
                Help = "Which chord, by number. A signal is rounded, and held to the list at either end.",
            },
        ],
        ChordOutputs(),
        (em, i) =>
        {
            var voiced = Enumerable.Range(0, Chords.All.Count).Select(Chords.Voiced).ToArray();

            return Pitches(em, i[0], Picked(em, i[1], voiced));
        },
        "Four frequencies of a chord on a root note: patch each into an oscillator's 'freq'. "
        + "A three-note chord adds its root an octave up; a two-note chord adds both notes an "
        + "octave up. The chords by number: "
        + string.Join(", ", Chords.All.Select((c, n) => $"{n} {c.Name}")) + ".");

    /// <summary>
    /// The seventh chord a scale builds on a root: the root and every other note of
    /// the scale above it.
    /// </summary>
    /// <remarks>
    /// The scale is a setting rather than a socket: it decides the tables the note and
    /// the root are looked up in. Both are signals, so a table has a row for each note
    /// of an octave, and the octave is added after.
    /// </remarks>
    private static NodeDef AutoChord() => new(
        AutoChordTypeId, "Auto Chord", ModuleCategories.Pitch,
        [
            Pitched("tonic", 60f) with { Help = "The scale's tonic, as a note number: where step nought is." },
            new PortSpec("root", PortKind.Scalar, 0f, -14f, 14f, Display: PortDisplay.Integer)
            {
                Help = "The chord's root, in steps of the scale from the tonic: 0 the tonic, 1 the next note up, "
                    + "-1 the note below, 7 the tonic an octave up. Added to 'note'. A signal is rounded.",
            },
            Pitched("note", 60f) with
            {
                NormalledFrom = 0,
                Help = "A note number to build the chord on, such as a MIDI In's pitch, carrying 'tonic' when "
                    + "nothing is patched. A note off the scale moves to the nearest one on it, up on a tie.",
            },
        ],
        ChordOutputs(),
        (em, i) =>
        {
            var scale = Chords.Scale(i.Extra<ExtraState>(AutoChordStateKey)?.Chosen(AutoChordScaleField) ?? string.Empty);

            var steps = scale.Classes.Length;
            var tonic = em.Unary(OpCode.Floor, em.Add(i[0], 0.5f));
            var note = em.Unary(OpCode.Floor, em.Add(i[2], 0.5f));

            // The note in steps of the scale: whole octaves of it, rounded because
            // seven twelfths is not exact, and where in its octave it lands.
            var semitones = em.Sub(note, tonic);
            var above = em.Binary(OpCode.Mod, semitones, em.Constant(Pitch.Semitones));
            var whole = em.Mul(em.Sub(semitones, above), steps / Pitch.Semitones);
            var played = em.Add(
                em.Unary(OpCode.Floor, em.Add(whole, 0.5f)),
                Picked(em, above, [.. Enumerable.Range(0, Pitch.Classes).Select(s => new[] { Chords.Steps(scale, s) })])[0]);

            var root = em.Add(em.Unary(OpCode.Floor, em.Add(i[1], 0.5f)), played);
            var degree = em.Binary(OpCode.Mod, root, em.Constant(steps));
            var octaves = em.Mul(em.Sub(root, degree), Pitch.Semitones / steps);

            var voiced = Enumerable.Range(0, steps).Select(d => Chords.Diatonic(scale, d)).ToArray();

            return Pitches(em, em.Add(i[0], octaves), Picked(em, degree, voiced));
        },
        "The four-note chord a scale builds on one of its notes: that note and the scale's third, "
        + "fifth and seventh above it, as four frequencies for four oscillators. Pick the scale on "
        + "the module, then play the note into 'note', count it in steps from the tonic on 'root', "
        + "or both, and the two add up: in C major, D on 'note' or 1 on 'root' gives D minor 7, "
        + "and -1 on 'root' the B half-diminished below.")
    {
        Extras =
        [
            new SettingsExtra(
                AutoChordStateKey,
                [
                    new ExtraField.Choice(
                        AutoChordScaleField,
                        "scale",
                        [.. Chords.Scales.Select(s => new ChoiceOption(s.Id, s.Name))],
                        Chords.Scales[0].Id) { Help = "The scale the chord is built in, starting on 'tonic'." },
                ]),
        ],
    };

    /// <summary>
    /// Row <paramref name="at"/> of <paramref name="rows"/>, one slot per column,
    /// with <paramref name="at"/> rounded and held to the table.
    /// </summary>
    /// <remarks>
    /// Each row is the one above plus a step at its edge, so a column costs a
    /// multiply and an add per row it differs from the last in, and the edges are
    /// shared by every column.
    /// </remarks>
    private static Slot[] Picked(Emitter em, Slot at, int[][] rows)
    {
        var edges = new Slot[rows.Length];

        for (var r = 1; r < rows.Length; r++)
            edges[r] = em.Binary(OpCode.Step, em.Constant(r - 0.5f), at);

        var picked = new Slot[rows[0].Length];

        for (var k = 0; k < picked.Length; k++)
        {
            var value = em.Constant(rows[0][k]);

            for (var r = 1; r < rows.Length; r++)
            {
                var rise = rows[r][k] - rows[r - 1][k];

                if (rise != 0) value = em.Add(value, em.Mul(edges[r], rise));
            }

            picked[k] = value;
        }

        return picked;
    }

    /// <summary>A root and a semitone offset per output, as frequencies.</summary>
    private static Slot[] Pitches(Emitter em, Slot root, Slot[] offsets)
    {
        var nought = em.Constant(0f);

        return [.. offsets.Select(offset => Sounded(em, em.Add(root, offset), nought, nought)[0])];
    }
}
