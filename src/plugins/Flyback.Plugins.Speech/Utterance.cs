using Flyback.Core.Compile;

namespace Flyback.Plugins.Speech;

/// <summary>
/// What is said, laid out as tracks a frame every five milliseconds: how loud
/// the throat, the breath and the hiss are, where the formants sit and how the
/// pitch moves.
/// </summary>
/// <remarks>
/// Each track is a clip the program reads with a Table, so the whole sentence is
/// worked out once when the patch is built and costs eight lookups a sample to
/// play. Formants are smoothed over tens of milliseconds, which is what makes
/// one sound glide into the next rather than step.
/// </remarks>
internal sealed record Utterance(
    LoadedSample Voice,
    LoadedSample Breath,
    LoadedSample Hiss,
    LoadedSample HissHz,
    LoadedSample F1,
    LoadedSample F2,
    LoadedSample F3,
    LoadedSample Pitch,
    LoadedSample Open,
    LoadedSample Round,
    LoadedSample Line,
    LoadedSample Reveal,
    IReadOnlyList<float> Starts,
    IReadOnlyList<float> Holds)
{
    /// <summary>Frames a second.</summary>
    public const int Rate = 200;

    private const float Frame = 1000f / Rate;

    /// <summary>The longest sentence kept, so a pasted book cannot fill memory.</summary>
    private const int Longest = Rate * 120;

    /// <summary>How long a stop is shut before it opens, and the burst and breath after.</summary>
    private const float Closure = 50f;
    private const float Burst = 10f;
    private const float Aspiration = 40f;

    /// <summary>How long a vowel takes to leave a consonant's locus, and to reach the next one's.</summary>
    private const float Transition = 40f;

    /// <summary>
    /// The tracks for <paramref name="sounds"/>, and where each syllable starts and
    /// where its vowel can be held, in seconds.
    /// </summary>
    /// <param name="sung">Whether the words are sung, which leaves out the pauses and draws out nothing.</param>
    public static Utterance Of(IReadOnlyList<Spoken> sounds, bool sung = false)
    {
        if (sung) sounds = [.. sounds.Where(s => s.Phoneme is not null)];

        var voice = new List<float>();
        var breath = new List<float>();
        var hiss = new List<float>();
        var hissHz = new List<float>();
        var f1 = new List<float>();
        var f2 = new List<float>();
        var f3 = new List<float>();
        var lift = new List<float>();
        var pause = new List<bool>();
        var question = new List<bool>();
        var open = new List<float>();
        var round = new List<float>();
        var line = new List<float>();
        var reveal = new List<float>();
        var starts = new List<int>();
        var holds = new List<int>();
        var syllable = 0;

        var mouthFrom = (Open: 0f, Round: 0f);
        (float Open, float Round) mouthTo;
        Spoken current;

        (float F1, float F2, float F3) held = (500, 1500, 2500);

        for (var s = 0; s < sounds.Count && voice.Count < Longest; s++)
        {
            var spoken = current = sounds[s];

            if (spoken.Phoneme is not { } p)
            {
                mouthFrom = mouthTo = (0f, 0f);
                Hold(spoken.Pause, 0f, 0f, 0f, 0f, held, held, 0f, spoken.Question, silent: true);
                continue;
            }

            var following = Next(sounds, s)?.From ?? p.To;

            mouthFrom = Visemes.Of(p);
            mouthTo = Visemes.Of(p, end: true);
            if (p.Manner == Manner.Aspirate && Next(sounds, s) is { } aspirated) mouthFrom = mouthTo = Visemes.Of(aspirated);

            switch (p.Manner)
            {
                case Manner.Vowel:
                {
                    var length = p.Milliseconds * (spoken.Stressed ? 1.15f : 0.8f) * (!sung && Final(sounds, s) ? 1.4f : 1f);
                    var stress = spoken.Stressed ? 1f : 0f;

                    // Out of a consonant and into the next one, the formants start and end
                    // halfway to its locus, which is how a 'b' is told from a 'd'.
                    var before = s > 0 && sounds[s - 1].Phoneme is { Syllabic: false } c ? Halfway(c.To, p.From) : p.From;
                    var after = Next(sounds, s) is { Syllabic: false } d ? Halfway(p.To, d.From) : p.To;
                    var glide = Math.Min(Transition, length / 3f);

                    var (mouthStart, mouthEnd) = (mouthFrom, mouthTo);
                    mouthTo = mouthFrom;
                    Hold(glide, 1f, 0.03f, 0f, 0f, before, p.From, stress, false);

                    // A sung vowel is held on its first sound, so a diphthong is held early.
                    var steady = voice.Count;
                    (mouthFrom, mouthTo) = (mouthStart, mouthEnd);
                    Hold(length - 2f * glide, 1f, 0.03f, 0f, 0f, p.From, p.To, stress, false);
                    var middle = steady + (voice.Count - steady) * (p.From == p.To ? 0.5f : 0.3f);

                    mouthFrom = mouthEnd;
                    Hold(glide, 1f, 0.03f, 0f, 0f, p.To, after, stress, false);

                    starts.Add(syllable);
                    holds.Add((int)middle);
                    syllable = voice.Count;
                    break;
                }

                case Manner.Approximant or Manner.Nasal:
                    Hold(p.Milliseconds, p.Voice, 0f, 0f, 0f, p.From, p.To, 0f, false);
                    break;

                case Manner.Fricative:
                    Hold(p.Milliseconds, p.Voice, 0f, p.Hiss, p.HissHz, p.From, p.To, 0f, false);
                    break;

                case Manner.Aspirate:
                    Hold(p.Milliseconds, 0f, 0.5f, 0f, 0f, following, following, 0f, false);
                    break;

                case Manner.Stop:
                    Hold(Closure, p.Voice, 0f, 0f, p.HissHz, p.From, p.From, 0f, false);
                    Hold(Burst, 0f, 0.5f, p.Hiss, p.HissHz, p.From, p.From, 0f, false);
                    if (p.Voice == 0f) Hold(Aspiration, 0f, 0.6f, 0.05f, p.HissHz, p.From, Halfway(p.From, following), 0f, false);
                    break;

                case Manner.Affricate:
                    Hold(Closure, p.Voice * 0.5f, 0f, 0f, p.HissHz, p.From, p.From, 0f, false);
                    Hold(p.Milliseconds - Closure, p.Voice, 0f, p.Hiss, p.HissHz, p.From, p.To, 0f, false);
                    break;
            }

            held = p.To;
        }

        // A breath of silence either side, so the first and last sounds are not cut.
        var lead = (int)(30f / Frame);
        var tail = lead * 2;

        var pitch = Contour(lift, pause, question, lead, tail);

        float Seconds(int frame) => (lead + frame) / (float)Rate;

        return new Utterance(
            Clip(Smooth(Pad(voice, lead, tail, 0f), 3)),
            Clip(Smooth(Pad(breath, lead, tail, 0f), 3)),
            Clip(Smooth(Pad(hiss, lead, tail, 0f), 1)),
            Clip(Pad(hissHz, lead, tail, 4000f, fill: true)),
            Clip(Smooth(Pad(f1, lead, tail, 500f, fill: true), 5)),
            Clip(Smooth(Pad(f2, lead, tail, 1500f, fill: true), 5)),
            Clip(Smooth(Pad(f3, lead, tail, 2500f, fill: true), 5)),
            Clip(pitch),
            Clip(Smooth(Pad(open, lead, tail, 0f), 7)),
            Clip(Smooth(Pad(round, lead, tail, 0f), 7)),
            Clip(Pad(line, lead, tail, 0f, fill: true)),
            Clip(Pad(reveal, lead, tail, 0f)),
            [.. starts.Select(Seconds)],
            [.. holds.Select(Seconds)]);

        void Hold(
            float milliseconds,
            float v,
            float b,
            float h,
            float hz,
            (float F1, float F2, float F3) from,
            (float F1, float F2, float F3) to,
            float stress,
            bool asks,
            bool silent = false)
        {
            var frames = Math.Max(1, (int)MathF.Round(milliseconds / Frame));
            var noiseHz = hz > 0f ? hz : hissHz.Count > 0 ? hissHz[^1] : 4000f;

            for (var k = 0; k < frames; k++)
            {
                var t = frames == 1 ? 0f : k / (frames - 1f);

                voice.Add(v);
                breath.Add(b);
                hiss.Add(h);
                hissHz.Add(noiseHz);
                f1.Add(from.F1 + (to.F1 - from.F1) * t);
                f2.Add(from.F2 + (to.F2 - from.F2) * t);
                f3.Add(from.F3 + (to.F3 - from.F3) * t);
                lift.Add(stress);
                question.Add(asks);
                pause.Add(silent);
                open.Add(mouthFrom.Open + (mouthTo.Open - mouthFrom.Open) * t);
                round.Add(mouthFrom.Round + (mouthTo.Round - mouthFrom.Round) * t);
                line.Add(current.Line);
                reveal.Add(current.Shown / (float)current.LineLength);
            }
        }
    }

    private static (float F1, float F2, float F3) Halfway((float F1, float F2, float F3) a, (float F1, float F2, float F3) b) =>
        ((a.F1 + b.F1) / 2f, (a.F2 + b.F2) / 2f, (a.F3 + b.F3) / 2f);

    private static Phoneme? Next(IReadOnlyList<Spoken> sounds, int s) =>
        s + 1 < sounds.Count ? sounds[s + 1].Phoneme : null;

    /// <summary>Whether this is the last vowel before a pause or the end, which is drawn out.</summary>
    private static bool Final(IReadOnlyList<Spoken> sounds, int s)
    {
        for (var k = s + 1; k < sounds.Count; k++)
        {
            if (sounds[k].Phoneme is null) return true;
            if (sounds[k].Phoneme!.Syllabic) return false;
        }

        return true;
    }

    /// <summary>
    /// The pitch as a multiple of the knob: falling slowly through each phrase,
    /// up on each stressed vowel, and up at the end of a question.
    /// </summary>
    private static float[] Contour(List<float> lift, List<bool> pause, List<bool> question, int lead, int tail)
    {
        const int Rise = 30;

        var count = lift.Count;
        var contour = new float[count];
        var k = 0;

        while (k < count)
        {
            if (pause[k])
            {
                contour[k] = k > 0 ? contour[k - 1] : 1f;
                k++;
                continue;
            }

            var start = k;
            while (k < count && !pause[k]) k++;

            var length = Math.Max(1, k - start);
            for (var j = start; j < k; j++)
                contour[j] = 1.1f - 0.2f * (j - start) / length + 0.12f * lift[j];

            if (k < count && question[k])
                for (var j = Math.Max(start, k - Rise); j < k; j++)
                    contour[j] += 0.4f * (j - (k - Rise)) / Rise;
        }

        return Smooth(Pad([.. contour], lead, tail, 1f, fill: true), 12);
    }

    private static float[] Pad(List<float> track, int lead, int tail, float rest, bool fill = false)
    {
        var padded = new float[lead + track.Count + tail];
        var first = fill && track.Count > 0 ? track[0] : rest;
        var last = fill && track.Count > 0 ? track[^1] : rest;

        for (var k = 0; k < lead; k++) padded[k] = first;
        track.CopyTo(padded, lead);
        for (var k = lead + track.Count; k < padded.Length; k++) padded[k] = last;

        return padded;
    }

    /// <summary>A moving average <paramref name="width"/> frames wide.</summary>
    private static float[] Smooth(float[] track, int width)
    {
        var smoothed = new float[track.Length];
        var half = width / 2;

        for (var k = 0; k < track.Length; k++)
        {
            var sum = 0f;
            var count = 0;

            for (var j = k - half; j <= k + half; j++)
            {
                sum += track[Math.Clamp(j, 0, track.Length - 1)];
                count++;
            }

            smoothed[k] = sum / count;
        }

        return smoothed;
    }

    private static LoadedSample Clip(float[] track) => new(track, Rate);
}
