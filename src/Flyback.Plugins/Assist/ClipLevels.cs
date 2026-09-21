using System.Globalization;
using System.Text;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Assist;

/// <summary>What <c>listen</c> measures in a rendered clip: its peak, its rms, its crest and its level over time.</summary>
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
    public static string Measured(ReadOnlySpan<float> samples, float peak, float rms)
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

        text.Append(". A row of near-identical figures is something continuous; a rhythm moves.");

        return text.ToString();
    }

    /// <summary>The distance between two levels, which is a ratio rather than a level.</summary>
    private static string Gap(float above, float below) =>
        above <= 0f || below <= 0f
            ? "n/a"
            : (20 * Math.Log10(above / below)).ToString("0.0", CultureInfo.InvariantCulture) + " dB";

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
