namespace Flyback.Tests;

/// <summary>Drums made for listening tests, each drum in a band of its own so what a detector should hear is known.</summary>
/// <remarks>Linked into each test project that needs it rather than shared through a reference.</remarks>
internal static class DrumLoop
{
    private const int Rate = 48_000;

    /// <summary>A four-on-the-floor loop at 120 BPM: kicks on the beats, snares on two and four, hats on the eighths.</summary>
    public static float[] Drums(int seconds, float volume = 1f) =>
        [.. Mix(
            seconds,
            (Kick, Every(0.5, seconds)),
            (Snare, Every(1, seconds, from: 0.5)),
            (Hat, Every(0.25, seconds, from: 0.125)))
            .Select(sample => sample * volume)];

    /// <summary>Hit times <paramref name="gap"/> apart from <paramref name="from"/>, inside <paramref name="seconds"/>.</summary>
    public static double[] Every(double gap, int seconds, double from = 0.05) =>
        [.. Enumerable.Range(0, (int)((seconds - from) / gap) + 1).Select(n => from + n * gap).Where(at => at < seconds)];

    /// <summary>Each drum played at its times, summed, over <paramref name="seconds"/> at 48 kHz.</summary>
    public static float[] Mix(int seconds, params (Func<double, float> Drum, double[] At)[] parts)
    {
        var sound = new float[seconds * Rate];

        foreach (var (drum, at) in parts)
            foreach (var start in at)
                for (var i = (int)(start * Rate); i < Math.Min(sound.Length, (int)((start + 0.4) * Rate)); i++)
                    sound[i] += drum(i / (double)Rate - start);

        return sound;
    }

    /// <summary>A kick: a sine falling from 120 Hz to 50, dying away over a few tenths of a second.</summary>
    public static float Kick(double t) =>
        (float)(0.8 * Attack(t) * Math.Exp(-t / 0.06) * Math.Sin(2d * Math.PI * (50d * t + 70d * 0.03 * (1d - Math.Exp(-t / 0.03)))));

    /// <summary>A snare, as a tone of its own band: 1.2 kHz, shorter than the kick.</summary>
    public static float Snare(double t) =>
        (float)(0.5 * Attack(t) * Math.Exp(-t / 0.04) * Math.Sin(2d * Math.PI * 1_200d * t));

    /// <summary>A hat: two high tones that do not sit on one another, very short.</summary>
    public static float Hat(double t) =>
        (float)(0.15 * Attack(t) * Math.Exp(-t / 0.015) * (Math.Sin(2d * Math.PI * 10_000d * t) + Math.Sin(2d * Math.PI * 11_700d * t)));

    /// <summary>A held tone, faded in over the same two milliseconds a drum is.</summary>
    public static float[] Tone(double hertz, float amplitude, int seconds) =>
        [.. Enumerable.Range(0, seconds * Rate).Select(i => (float)(amplitude * Attack(i / (double)Rate) * Math.Sin(2d * Math.PI * hertz * i / Rate)))];

    /// <summary>Two milliseconds in, so a drum starts without a click across every band.</summary>
    private static double Attack(double t) => Math.Min(1d, t / 0.002);
}
