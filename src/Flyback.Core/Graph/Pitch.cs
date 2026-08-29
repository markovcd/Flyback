using System.Globalization;

namespace Flyback.Core.Graph;

/// <summary>
/// The note numbering the synth uses, in one place. Notes are MIDI numbers:
/// whole steps are semitones, 69 is A4 at 440 Hz, and 60 is middle C.
/// </summary>
/// <remarks>
/// The Note module compiles this same arithmetic into register ops, because an
/// emit function cannot call back into C# — what runs per sample is the op list,
/// not this. These are here for everything outside the inner loop: naming a knob
/// value in the editor, and giving the tests something independent to check the
/// emitted ops against.
/// </remarks>
public static class Pitch
{
    /// <summary>Concert A, the one note whose frequency is fixed by definition.</summary>
    public const float ConcertPitch = 440f;

    /// <summary>The note number <see cref="ConcertPitch"/> belongs to — A4.</summary>
    public const float ConcertNote = 69f;

    public const float Semitones = 12f;

    /// <summary>
    /// The same twelve, counted rather than measured — how many notes there are
    /// before the pattern repeats, which is what a scale is a subset of.
    /// </summary>
    public const int Classes = 12;

    /// <summary>
    /// Sharps rather than flats throughout. A quantiser has no key to decide
    /// which spelling is meant, and picking one keeps every number to one name.
    /// </summary>
    private static readonly string[] Names =
        ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    /// <summary>
    /// What a note is called with the octave left off — 0 is "C" and 9 is "A".
    /// A pitch class is a note in every octave at once, which is what a scale
    /// names: choosing A puts every A in the scale rather than one of them.
    /// </summary>
    public static string ClassName(int pitchClass) =>
        Names[(pitchClass % Classes + Classes) % Classes];

    /// <summary>
    /// A scale held to what one can be: inside the octave, each note named at
    /// most once, and in ascending order.
    /// </summary>
    /// <remarks>
    /// A scale is a set and is written as a list, which is the same trade a
    /// saved patch makes everywhere else — a file is text somebody may have
    /// edited, so the shape it can hold is wider than the shape that means
    /// anything. Order is imposed rather than kept because a set has none, and
    /// two scales with the same notes in them should be the same scale: it is
    /// what makes the twelve toggles in the panel the whole of the state.
    /// <para>
    /// Also what settles a tie. A value exactly halfway between two of the
    /// scale's notes takes the one named later, and after this that is always
    /// the higher pitch class.
    /// </para>
    /// </remarks>
    public static List<int> Scale(IEnumerable<int>? classes) =>
        classes is null ? [] : [.. classes.Where(c => c is >= 0 and < Classes).Distinct().Order()];

    /// <summary>
    /// The whole note a value snaps to. Ties round upward, matching the ops the
    /// Note module emits.
    /// </summary>
    public static float Nearest(float note) => MathF.Floor(note + 0.5f);

    /// <summary>What a note number sounds like, in hertz.</summary>
    public static float Frequency(float note) =>
        ConcertPitch * MathF.Pow(2f, (note - ConcertNote) / Semitones);

    /// <summary>
    /// A note number as it is written — 57 is "A3". The octave is the scientific
    /// one, where middle C is C4, so MIDI's lowest note lands in octave -1.
    /// </summary>
    public static string Name(float note)
    {
        var whole = Nearest(note);

        // Anything a patch could put on this input is fair game, and a note far
        // outside what a keyboard has is better shown as the number it is than
        // as a name nobody could play.
        if (!float.IsFinite(whole) || MathF.Abs(whole) > 1_000f)
            return whole.ToString("0", CultureInfo.InvariantCulture);

        var index = (int)whole;
        var octave = (int)MathF.Floor(whole / Semitones) - 1;

        // Floored rather than truncated, so the names below C0 keep running
        // backwards instead of mirroring around zero.
        var step = index - (octave + 1) * (int)Semitones;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Names[step]}{octave}");
    }
}
