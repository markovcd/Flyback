using System.Globalization;
using System.Text.RegularExpressions;
using Flyback.Core.Compile;

namespace Flyback.Plugins.Speech;

/// <summary>
/// The notes a sung Speak gives its syllables, one each, laid out as clips read
/// by syllable: where each begins in beats, how high it is, and where in the
/// words it starts and is held.
/// </summary>
/// <param name="Count">How many syllables there are.</param>
/// <param name="Beats">How long the whole tune is, in beats.</param>
internal sealed partial record Tune(
    int Count,
    float Beats,
    IReadOnlyList<float> Begins,
    LoadedSample BeginAt,
    LoadedSample Hz,
    LoadedSample Starts,
    LoadedSample Holds)
{
    /// <summary>
    /// The tune for <paramref name="said"/>'s syllables sung to <paramref name="notes"/>,
    /// written as a Note Sequencer's are: <c>G3@3 E3@3 C3</c>, a number of beats after the '@'.
    /// </summary>
    /// <remarks>
    /// Notes are taken in turn, round again if there are fewer than the syllables.
    /// With none, each syllable takes a beat at the pitch knob, which a nought in
    /// <see cref="Hz"/> says.
    /// </remarks>
    public static (Tune Tune, IReadOnlyList<string> Unread) Of(Utterance said, string notes)
    {
        var count = Math.Max(1, said.Starts.Count);
        var read = new List<(float Hz, float Beats)>();
        var unread = new List<string>();

        foreach (var word in notes.Split((char[])[' ', '\t', '\n', '|'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (Note(word) is { } note) read.Add(note);
            else unread.Add(word);
        }

        if (read.Count == 0) read.Add((0f, 1f));

        var begins = new float[count];
        var hz = new float[count];
        var beat = 0f;

        for (var n = 0; n < count; n++)
        {
            var (pitch, beats) = read[n % read.Count];

            begins[n] = beat;
            hz[n] = pitch;
            beat += beats;
        }

        var starts = said.Starts.Count > 0 ? said.Starts.ToArray() : [0f];
        var holds = said.Holds.Count > 0 ? said.Holds.ToArray() : [0f];

        return (new Tune(count, beat, begins, Clip(begins), Clip(hz), Clip(starts), Clip(holds)), unread);
    }

    // One sample a syllable, so a whole syllable number reads its own exactly.
    private static LoadedSample Clip(float[] values) => new(values, 1);

    /// <summary>A note name or number and its length in beats, or null.</summary>
    private static (float Hz, float Beats)? Note(string word)
    {
        var match = Written().Match(word);
        if (!match.Success) return null;

        float midi;

        if (match.Groups["number"].Success)
        {
            midi = float.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture);
        }
        else
        {
            var letter = char.ToUpperInvariant(match.Groups["letter"].Value[0]);
            var step = "C D EF G A B".IndexOf(letter);
            var accidental = match.Groups["accidental"].Value switch { "#" => 1, "b" => -1, _ => 0 };
            var octave = int.Parse(match.Groups["octave"].Value, CultureInfo.InvariantCulture);

            midi = 12 * (octave + 1) + step + accidental;
        }

        var beats = match.Groups["beats"].Success
            ? float.Parse(match.Groups["beats"].Value, CultureInfo.InvariantCulture)
            : 1f;

        if (midi is < 0f or > 127f || !(beats > 0f) || beats > 64f) return null;

        return (440f * MathF.Pow(2f, (midi - 69f) / 12f), beats);
    }

    [GeneratedRegex(@"^(?:(?<letter>[A-Ga-g])(?<accidental>[#b]?)(?<octave>-?\d)|(?<number>\d+(?:\.\d+)?))(?:@(?<beats>\d+(?:\.\d+)?))?$")]
    private static partial Regex Written();
}
