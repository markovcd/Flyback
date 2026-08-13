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
    /// Sharps rather than flats throughout. A quantiser has no key to decide
    /// which spelling is meant, and picking one keeps every number to one name.
    /// </summary>
    private static readonly string[] Names =
        ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

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
