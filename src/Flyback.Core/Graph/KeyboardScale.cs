namespace Flyback.Core.Graph;

/// <summary>
/// The scale the computer keyboard is laid out in: one of <see cref="Chords.Scales"/>
/// on a tonic, the scales an Auto Chord offers.
/// </summary>
/// <param name="Tonic">The tonic as a pitch class, 0 for C and 9 for A.</param>
/// <param name="Scale">The id of one of <see cref="Chords.Scales"/>.</param>
public sealed record KeyboardScale(int Tonic, string Scale)
{
    /// <summary>What a fresh scale layout starts on: C major, what a fresh Auto Chord starts on.</summary>
    public static KeyboardScale Major { get; } = new(0, Chords.Scales[0].Id);

    /// <summary>The scale itself, and the major scale for an id nothing answers to.</summary>
    public ScaleMode Mode => Chords.Scale(Scale);

    /// <summary>The tonic held to the octave.</summary>
    public int TonicClass => (Tonic % Pitch.Classes + Pitch.Classes) % Pitch.Classes;

    /// <summary>
    /// The notes along a row, in semitones above the C the row's octave starts on:
    /// the tonic first, and upward from it.
    /// </summary>
    public IReadOnlyList<int> Row => [.. Mode.Classes.Select(c => TonicClass + c)];

    /// <summary>What it is called: "D Dorian".</summary>
    public string Name => $"{Pitch.ClassName(TonicClass)} {Mode.Name}";
}
