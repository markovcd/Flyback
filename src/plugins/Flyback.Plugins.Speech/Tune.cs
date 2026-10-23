using System.Globalization;
using System.Text.RegularExpressions;
using Flyback.Core.Compile;

namespace Flyback.Plugins.Speech;

/// <summary>
/// The notes a sung Speak gives its syllables, one each, and the rests between
/// them, laid out as clips read by slot: where each begins in beats, how high it
/// is, where in the words it starts and is held, and whether it is heard.
/// </summary>
/// <param name="Count">How many slots there are: a syllable or a rest each.</param>
/// <param name="Beats">How long the whole tune is, in beats.</param>
internal sealed partial record Tune(
    int Count,
    float Beats,
    IReadOnlyList<float> Begins,
    LoadedSample BeginAt,
    LoadedSample Hz,
    LoadedSample Starts,
    LoadedSample Holds,
    LoadedSample Heard)
{
    /// <summary>The most rests a tune keeps, so a pasted run of them cannot grow the program.</summary>
    private const int MostRests = 512;

    /// <summary>
    /// The tune for <paramref name="said"/>'s syllables sung to <paramref name="notes"/>,
    /// written as a Note Sequencer's are: <c>G3@3 E3@3 C3</c>, a number of beats after the '@',
    /// and <c>~</c> or <c>~@2</c> a rest.
    /// </summary>
    /// <remarks>
    /// Notes are taken in turn, round again if there are fewer than the syllables;
    /// a rest takes no syllable, and the rests straight after the last syllable's note
    /// end the tune. With no notes, each syllable takes a beat at the pitch knob,
    /// which a nought in <see cref="Hz"/> says.
    /// </remarks>
    public static (Tune Tune, IReadOnlyList<string> Unread) Of(Utterance said, string notes)
    {
        var syllables = Math.Max(1, said.Starts.Count);
        var read = new List<(float Hz, float Beats, bool Silent)>();
        var unread = new List<string>();

        foreach (var word in notes.Split((char[])[' ', '\t', '\n', '|'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (Note(word) is { } note) read.Add(note);
            else unread.Add(word);
        }

        if (!read.Exists(n => !n.Silent)) read = [(0f, 1f, false)];

        var restsWritten = 0;
        read.RemoveAll(n => n.Silent && ++restsWritten > MostRests);

        var begins = new List<float>();
        var hz = new List<float>();
        var starts = new List<float>();
        var holds = new List<float>();
        var heard = new List<float>();
        var firstHz = read.First(n => !n.Silent).Hz;
        var lastHz = firstHz;
        var beat = 0f;
        var sung = 0;
        var rests = 0;

        for (var i = 0; sung < syllables || (i % read.Count != 0 && read[i % read.Count].Silent); i++)
        {
            var (pitch, beats, rest) = read[i % read.Count];

            if (rest)
            {
                if (rests++ >= MostRests) continue;

                // A rest stays where the syllable before it was held, or before
                // any, where the first one starts.
                var kept = sung > 0 ? Track(said.Holds, sung - 1) : Track(said.Starts, 0);

                begins.Add(beat);
                hz.Add(lastHz);
                starts.Add(kept);
                holds.Add(kept);
                heard.Add(0f);
            }
            else
            {
                begins.Add(beat);
                hz.Add(pitch);
                starts.Add(Track(said.Starts, sung));
                holds.Add(Track(said.Holds, sung));
                heard.Add(1f);
                lastHz = pitch;
                sung++;
            }

            beat += beats;
        }

        // A rest before the first note is held at that note's pitch.
        for (var n = 0; n < hz.Count && heard[n] == 0f; n++) hz[n] = firstHz;

        return (new Tune(
            begins.Count,
            beat,
            begins,
            Clip(begins),
            Clip(hz),
            Clip(starts),
            Clip(holds),
            Clip(heard)), unread);
    }

    private static float Track(IReadOnlyList<float> track, int n) => n < track.Count ? track[n] : 0f;

    // One sample a slot, so a whole slot number reads its own exactly.
    private static LoadedSample Clip(List<float> values) => new([.. values], 1);

    /// <summary>A note name or number, or a rest, and its length in beats, or null.</summary>
    private static (float Hz, float Beats, bool Silent)? Note(string word)
    {
        var match = Written().Match(word);
        if (!match.Success) return null;

        float midi;

        if (match.Groups["rest"].Success)
        {
            midi = 0f;
        }
        else if (match.Groups["number"].Success)
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

        return (440f * MathF.Pow(2f, (midi - 69f) / 12f), beats, match.Groups["rest"].Success);
    }

    [GeneratedRegex(@"^(?:(?<rest>~)|(?<letter>[A-Ga-g])(?<accidental>[#b]?)(?<octave>-?\d)|(?<number>\d+(?:\.\d+)?))(?:@(?<beats>\d+(?:\.\d+)?))?$")]
    private static partial Regex Written();
}
