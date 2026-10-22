namespace Flyback.Plugins.Speech;

/// <summary>
/// What the mouth does for each sound: how far the jaw drops and how round the
/// lips pull, which is all the Mouth needs to tell an 'ah' from an 'oo' from an 'm'.
/// </summary>
/// <remarks>
/// Between them the two numbers place about ten mouth shapes a lip-reader would
/// name: shut for p, b and m, teeth on the lip for f and v, nearly shut for t, d,
/// s and n, pushed out for sh and ch, round for w and oo, and open by degrees for
/// the vowels. The tracks glide between them as the formants do.
/// </remarks>
internal static class Visemes
{
    private static readonly Dictionary<string, (float Open, float Round)> Shapes = new()
    {
        ["iy"] = (0.25f, 0f),
        ["ih"] = (0.4f, 0f),
        ["eh"] = (0.55f, 0f),
        ["ae"] = (0.75f, 0f),
        ["aa"] = (1f, 0.1f),
        ["ao"] = (0.8f, 0.6f),
        ["uh"] = (0.4f, 0.6f),
        ["uw"] = (0.25f, 1f),
        ["ah"] = (0.7f, 0.1f),
        ["ax"] = (0.45f, 0.1f),
        ["er"] = (0.35f, 0.5f),
        ["ey"] = (0.5f, 0f),
        ["ay"] = (0.9f, 0.1f),
        ["oy"] = (0.7f, 0.7f),
        ["aw"] = (0.9f, 0.3f),
        ["ow"] = (0.55f, 0.8f),
        ["w"] = (0.15f, 1f),
        ["y"] = (0.2f, 0f),
        ["r"] = (0.25f, 0.5f),
        ["l"] = (0.3f, 0f),
        ["m"] = (0f, 0.1f),
        ["n"] = (0.15f, 0f),
        ["ng"] = (0.3f, 0f),
        ["f"] = (0.08f, 0f),
        ["v"] = (0.08f, 0f),
        ["th"] = (0.2f, 0f),
        ["dh"] = (0.2f, 0f),
        ["s"] = (0.12f, 0f),
        ["z"] = (0.12f, 0f),
        ["sh"] = (0.2f, 0.7f),
        ["zh"] = (0.2f, 0.7f),
        ["p"] = (0f, 0.1f),
        ["b"] = (0f, 0.1f),
        ["t"] = (0.15f, 0f),
        ["d"] = (0.15f, 0f),
        ["k"] = (0.3f, 0f),
        ["g"] = (0.3f, 0f),
        ["ch"] = (0.2f, 0.7f),
        ["jh"] = (0.2f, 0.7f),
    };

    /// <summary>The mouth's shape for a sound, at its end where a diphthong moves.</summary>
    public static (float Open, float Round) Of(Phoneme phoneme, bool end = false) =>
        end && phoneme.From != phoneme.To ? EndOf(phoneme) : Shapes.GetValueOrDefault(phoneme.Symbol, (0.3f, 0f));

    // A diphthong closes towards the vowel it ends on.
    private static (float Open, float Round) EndOf(Phoneme phoneme) => phoneme.Symbol switch
    {
        "ey" or "ay" or "oy" => Shapes["ih"],
        "aw" or "ow" => Shapes["uh"],
        _ => Shapes[phoneme.Symbol],
    };
}
