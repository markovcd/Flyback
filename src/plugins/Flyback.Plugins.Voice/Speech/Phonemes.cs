namespace Flyback.Plugins.Voice.Speech;

/// <summary>
/// The sounds of English an adult voice makes, with formants after Klatt (1980)
/// and Peterson and Barney (1952).
/// </summary>
/// <remarks>
/// A consonant's formants are its locus: where the vowels beside it glide from
/// and to, which is most of what tells a 'b' from a 'd' from a 'g'.
/// </remarks>
internal static class Phonemes
{
    private static readonly Phoneme[] All =
    [
        Vowel("iy", (270, 2290, 3010), 140),
        Vowel("ih", (390, 1990, 2550), 100),
        Vowel("eh", (530, 1840, 2480), 110),
        Vowel("ae", (660, 1720, 2410), 150),
        Vowel("aa", (730, 1090, 2440), 150),
        Vowel("ao", (570, 840, 2410), 150),
        Vowel("uh", (440, 1020, 2240), 100),
        Vowel("uw", (300, 870, 2240), 140),
        Vowel("ah", (640, 1190, 2390), 110),
        Vowel("ax", (500, 1400, 2450), 60),
        Vowel("er", (490, 1350, 1690), 150),
        Glide("ey", (480, 1720, 2520), (330, 2200, 2600), 170),
        Glide("ay", (700, 1220, 2600), (400, 1900, 2550), 200),
        Glide("oy", (550, 960, 2400), (360, 1820, 2450), 210),
        Glide("aw", (700, 1220, 2600), (450, 1050, 2250), 210),
        Glide("ow", (540, 1100, 2300), (450, 900, 2300), 170),

        new("w", Manner.Approximant, (290, 610, 2150), (290, 610, 2150), 70, 0.7f),
        new("y", Manner.Approximant, (260, 2070, 3020), (260, 2070, 3020), 70, 0.7f),
        new("r", Manner.Approximant, (310, 1060, 1380), (310, 1060, 1380), 70, 0.75f),
        new("l", Manner.Approximant, (310, 1050, 2880), (310, 1050, 2880), 70, 0.75f),

        new("m", Manner.Nasal, (250, 1100, 2150), (250, 1100, 2150), 80, 0.45f),
        new("n", Manner.Nasal, (250, 1500, 2500), (250, 1500, 2500), 75, 0.45f),
        new("ng", Manner.Nasal, (250, 2000, 2800), (250, 2000, 2800), 85, 0.45f),

        new("f", Manner.Fricative, (340, 1100, 2080), (340, 1100, 2080), 110, 0f, 0.15f, 8000),
        new("v", Manner.Fricative, (340, 1100, 2080), (340, 1100, 2080), 70, 0.45f, 0.08f, 8000),
        new("th", Manner.Fricative, (320, 1290, 2540), (320, 1290, 2540), 110, 0f, 0.12f, 7000),
        new("dh", Manner.Fricative, (320, 1290, 2540), (320, 1290, 2540), 55, 0.45f, 0.06f, 7000),
        new("s", Manner.Fricative, (320, 1390, 2530), (320, 1390, 2530), 120, 0f, 0.8f, 5500),
        new("z", Manner.Fricative, (320, 1390, 2530), (320, 1390, 2530), 85, 0.45f, 0.5f, 5500),
        new("sh", Manner.Fricative, (300, 1840, 2750), (300, 1840, 2750), 120, 0f, 0.8f, 2800),
        new("zh", Manner.Fricative, (300, 1840, 2750), (300, 1840, 2750), 85, 0.45f, 0.5f, 2800),
        new("hh", Manner.Aspirate, (500, 1500, 2500), (500, 1500, 2500), 70, 0f),

        new("p", Manner.Stop, (400, 1100, 2150), (400, 1100, 2150), 90, 0f, 0.8f, 1500),
        new("b", Manner.Stop, (400, 1100, 2150), (400, 1100, 2150), 75, 0.15f, 0.7f, 1500),
        new("t", Manner.Stop, (400, 1600, 2600), (400, 1600, 2600), 90, 0f, 0.9f, 4500),
        new("d", Manner.Stop, (400, 1600, 2600), (400, 1600, 2600), 75, 0.15f, 0.8f, 4000),
        new("k", Manner.Stop, (300, 1990, 2850), (300, 1990, 2850), 95, 0f, 0.7f, 2500),
        new("g", Manner.Stop, (300, 1990, 2850), (300, 1990, 2850), 80, 0.15f, 0.5f, 2300),
        new("ch", Manner.Affricate, (300, 1840, 2750), (300, 1840, 2750), 130, 0f, 0.8f, 2800),
        new("jh", Manner.Affricate, (300, 1840, 2750), (300, 1840, 2750), 110, 0.3f, 0.5f, 2800),
    ];

    private static readonly Dictionary<string, Phoneme> BySymbol = All.ToDictionary(p => p.Symbol);

    /// <summary>The symbols there are, for a message naming them.</summary>
    public static IEnumerable<string> Symbols => All.Select(p => p.Symbol);

    /// <summary>The sound with that ARPAbet name, or null.</summary>
    public static Phoneme? Find(string symbol) => BySymbol.GetValueOrDefault(symbol);

    private static Phoneme Vowel(string symbol, (float, float, float) formants, float milliseconds) =>
        new(symbol, Manner.Vowel, formants, formants, milliseconds, 1f);

    private static Phoneme Glide(string symbol, (float, float, float) from, (float, float, float) to, float milliseconds) =>
        new(symbol, Manner.Vowel, from, to, milliseconds, 1f);
}
