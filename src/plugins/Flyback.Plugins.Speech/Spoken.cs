namespace Flyback.Plugins.Speech;

/// <summary>One step of what is said: a sound, or a pause where the punctuation was.</summary>
/// <param name="Phoneme">The sound, or null for a pause.</param>
/// <param name="Stressed">Whether this is the vowel a word leans on, which is longer and higher.</param>
/// <param name="Pause">How long a pause lasts, in milliseconds at a speed of one.</param>
/// <param name="Question">Whether a pause ends a question, so the pitch before it rises.</param>
internal sealed record Spoken(Phoneme? Phoneme, bool Stressed = false, float Pause = 0f, bool Question = false)
{
    /// <summary>Which line of the words it is on, from nought.</summary>
    public int Line { get; init; }

    /// <summary>How many letters of its line are shown once it is said.</summary>
    public int Shown { get; init; }

    /// <summary>How many letters its line has.</summary>
    public int LineLength { get; init; } = 1;
}
