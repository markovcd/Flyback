namespace Flyback.Core.Compile;

/// <summary>One clip, as the machine wants it: mono, and float.</summary>
/// <param name="Samples">The audio, mixed to one channel.</param>
/// <param name="SampleRate">What it was recorded at, which is what gives it a duration.</param>
public sealed record LoadedSample(float[] Samples, int SampleRate)
{
    /// <summary>How long it plays for, which is the one thing a patch has to know about it.</summary>
    public float Seconds => SampleRate <= 0 ? 0f : Samples.Length / (float)SampleRate;

    /// <summary>
    /// The value at a moment, in seconds from the start, with silence either
    /// side of the clip.
    /// </summary>
    /// <remarks>
    /// Linearly interpolated, exactly as a delay line's read is and for the same
    /// reason: what drives the position is a signal, so it lands between samples
    /// far more often than on one, and a nearest-sample read would put a
    /// staircase into everything played at a rate the file was not recorded at.
    /// <para>
    /// Silence outside rather than a clamp or a wrap. A clip that held its last
    /// sample for ever would be a click followed by DC; one that wrapped would
    /// loop whether or not anybody asked it to, and looping is something a patch
    /// says with a wire. Running off the end is how a one-shot ends.
    /// </para>
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

/// <summary>
/// Where a patch's samples come from. The compiler asks; something outside it
/// answers, and owns the reading and the caching.
/// </summary>
/// <remarks>
/// An interface because the compiler must not do file I/O. Every edit recompiles
/// the whole patch (ADR-0021), so a compile that opened a file would open it
/// sixty times a second — and the engine has no business knowing what a
/// directory is besides. What arrives here is already-loaded audio, keyed by the
/// text a patch stores.
/// <para>
/// Answering null is not an error to this: it is what the compiler turns into a
/// complaint naming the module and the file, in the same list as everything else
/// it has to say about a patch.
/// </para>
/// </remarks>
public interface ISampleLibrary
{
    /// <summary>The audio a path names, or null where there is none to be had.</summary>
    LoadedSample? Find(string path);

    /// <summary>Why the last <see cref="Find"/> of this path came back empty, for the complaint.</summary>
    string Explain(string path);
}
