namespace Flyback.Core.Compile;

/// <summary>One clip, as the machine wants it: mono, and float.</summary>
/// <param name="Samples">The audio, mixed to one channel.</param>
/// <param name="SampleRate">What it was recorded at, which is what gives it a duration.</param>
public sealed record LoadedSample(float[] Samples, int SampleRate)
{
    /// <summary>How long it plays for, which is the one thing a patch has to know about it.</summary>
    public float Seconds => SampleRate <= 0 ? 0f : Samples.Length / (float)SampleRate;

    /// <summary>
    /// The value at a moment, in seconds from the start, with silence either side of
    /// the clip.
    /// </summary>
    /// <remarks>
    /// Linearly interpolated, as a delay line's read is: what drives the position is a
    /// signal, so it lands between samples far more often than on one. Silence outside
    /// rather than a clamp or a wrap — a clip that held its last sample would be a
    /// click followed by DC, and looping is something a patch says with a wire.
    /// </remarks>
    public double At(double seconds)
    {
        if (!double.IsFinite(seconds) || Samples.Length == 0) return 0d;

        var position = seconds * SampleRate;
        if (position < 0d || position >= Samples.Length) return 0d;

        var whole = (int)position;
        var fraction = position - whole;

        var first = Samples[whole];
        var second = whole + 1 < Samples.Length ? Samples[whole + 1] : 0f;

        return first + (second - first) * fraction;
    }
}