namespace Flyback.Plugins.Voice.Speech;

/// <summary>One speech sound: where its formants sit, how long it lasts and what sounds it.</summary>
/// <param name="Symbol">Its ARPAbet name in lower case, as typed between brackets.</param>
/// <param name="From">The first three formants at its start, in hertz.</param>
/// <param name="To">The formants at its end; the same as <paramref name="From"/> but for a diphthong.</param>
/// <param name="Milliseconds">How long it lasts at a speed of one.</param>
/// <param name="Voice">How loud the throat is under it, 0 to 1.</param>
/// <param name="Hiss">How loud the noise at the teeth or lips is, 0 to 1.</param>
/// <param name="HissHz">Where that noise is centered.</param>
internal sealed record Phoneme(
    string Symbol,
    Manner Manner,
    (float F1, float F2, float F3) From,
    (float F1, float F2, float F3) To,
    float Milliseconds,
    float Voice,
    float Hiss = 0f,
    float HissHz = 0f)
{
    /// <summary>Whether it is a vowel or a diphthong, which is what takes the stress and the pitch.</summary>
    public bool Syllabic => Manner == Manner.Vowel;
}
