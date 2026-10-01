using System.Globalization;
using System.Text;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.Plugins.Assist;

/// <summary>What <c>listen</c> measures in a rendered clip: its peak, its rms, its crest, its loudness and its level over time.</summary>
internal static class ClipLevels
{
    /// <summary>Below this a buffer is called silence: -66 dBFS, and nothing a speaker would utter.</summary>
    public const float SilenceFloor = 0.0005f;

    /// <summary>How many slices the level is reported over. Enough to see a beat in a second or two.</summary>
    private const int Slices = 16;

    /// <summary>
    /// What the samples say about themselves, as against what a listener says about
    /// them.
    /// </summary>
    /// <remarks>
    /// A model asked to describe a patch built from three steady tones once reported
    /// a kickdrum and a hihat — the words it had been given rather than the sound it
    /// was played. Crest catches exactly that, being the distance between the
    /// loudest sample and the average one: a steady tone has almost none and
    /// percussion has a great deal. The slices are the same question over time.
    /// Reported as numbers with the yardstick beside them rather than as a verdict.
    /// </remarks>
    public static string Measured(ReadOnlySpan<float> samples, float peak, float rms, int sampleRate)
    {
        var text = new StringBuilder("Measured from the samples, not heard: peak ")
            .Append(Decibels(peak))
            .Append(", rms ")
            .Append(Decibels(rms))
            .Append(", crest ")
            .Append(Gap(peak, rms))
            .Append(". Crest is peak above rms: a steady tone sits near 3 dB, a mix with drum "
                + "hits in it 12 dB or more. Level in ")
            .Append(Slices)
            .Append(" slices across the clip, in dBFS:");

        var frames = samples.Length / NodeCatalog.AudioChannels;
        var slice = Math.Max(1, frames / Slices);

        for (var i = 0; i < Slices; i++)
        {
            var start = i * slice * NodeCatalog.AudioChannels;
            if (start >= samples.Length) break;

            var length = Math.Min(slice * NodeCatalog.AudioChannels, samples.Length - start);

            text.Append(' ').Append(Decibels(Levels(samples.Slice(start, length)).Rms).Replace(" dBFS", ""));
        }

        text.Append(". A row of near-identical figures is something continuous; a rhythm moves. ")
            .Append(Loudness(samples, sampleRate));

        return text.ToString();
    }

    /// <summary>
    /// The clip's loudness as ITU-R BS.1770 gates it, and its true peak: the figures
    /// a loudness target such as -14 LUFS is stated in, which rms is not.
    /// </summary>
    private static string Loudness(ReadOnlySpan<float> samples, int sampleRate)
    {
        var meter = new LoudnessMeter(sampleRate, NodeCatalog.AudioChannels);

        meter.Add(samples);

        var integrated = double.IsNegativeInfinity(meter.Integrated)
            ? "too quiet to gate"
            : meter.Integrated.ToString("0.0", CultureInfo.InvariantCulture) + " LUFS";

        return $"Loudness over the clip, as ITU-R BS.1770 measures it: {integrated} integrated, "
            + $"true peak {Decibels((float)meter.TruePeak).Replace("dBFS", "dBTP", StringComparison.Ordinal)}. "
            + "Streaming services play at about -14 LUFS; a true peak above -1 dBTP may clip once encoded.";
    }

    /// <summary>The distance between two levels, which is a ratio rather than a level.</summary>
    private static string Gap(float above, float below) =>
        above <= 0f || below <= 0f
            ? "n/a"
            : (20 * Math.Log10(above / below)).ToString("0.0", CultureInfo.InvariantCulture) + " dB";

    /// <summary>How long a render from zero is left to settle before it is judged silent or not, in seconds.</summary>
    /// <remarks>The oversampling filter rings for a few samples on a step from nothing.</remarks>
    public const double Onset = 0.01d;

    /// <summary>
    /// Whether nothing in an interleaved buffer swings both ways above
    /// <see cref="SilenceFloor"/> in either channel, after its first
    /// <paramref name="settle"/> frames.
    /// </summary>
    /// <remarks>
    /// Not the peak alone: a constant reaching the speakers is a step the DC blocker
    /// settles as one long decay of a single sign, a thump at the start and then
    /// nothing, and a peak would call that sound.
    /// </remarks>
    public static bool Silent(ReadOnlySpan<float> samples, int settle = 0)
    {
        for (var channel = 0; channel < NodeCatalog.AudioChannels; channel++)
        {
            var sign = 0;

            for (var i = settle * NodeCatalog.AudioChannels + channel; i < samples.Length; i += NodeCatalog.AudioChannels)
            {
                if (Math.Abs(samples[i]) < SilenceFloor) continue;

                var now = Math.Sign(samples[i]);

                if (sign != 0 && now != sign) return false;

                sign = now;
            }
        }

        return true;
    }

    /// <summary>Peak and rms of an interleaved buffer, over both channels at once.</summary>
    public static (float Peak, float Rms) Levels(ReadOnlySpan<float> samples)
    {
        var peak = 0f;
        var sum = 0d;

        foreach (var sample in samples)
        {
            var size = Math.Abs(sample);
            if (size > peak) peak = size;
            sum += (double)sample * sample;
        }

        return (peak, samples.Length == 0 ? 0f : (float)Math.Sqrt(sum / samples.Length));
    }

    private static string Decibels(float level) => level <= 0f
        ? "-inf dBFS"
        : (20 * Math.Log10(level)).ToString("0.0", CultureInfo.InvariantCulture) + " dBFS";
}
