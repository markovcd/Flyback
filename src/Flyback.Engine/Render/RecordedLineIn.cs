using Flyback.Core.Compile;

namespace Flyback.Engine.Render;

/// <summary>
/// A clip standing in for the microphone: frame by frame from its start, then silence.
/// </summary>
/// <remarks>
/// What an offline render hears, since a file is the one input that sounds the same
/// twice. A mono clip is heard on both sides.
/// </remarks>
/// <param name="lefts">The left channel, or the only one.</param>
/// <param name="rights">The right channel, or null for a mono clip.</param>
public sealed class RecordedLineIn(float[] lefts, float[]? rights = null) : ILineInSource
{
    private int at;

    /// <summary>
    /// A loaded clip, read at <paramref name="sampleRate"/> from its start: the rate the
    /// render is made at, whatever the file was recorded at. Mono, as <see cref="LoadedSample"/> is.
    /// </summary>
    public static RecordedLineIn From(LoadedSample clip, int sampleRate)
    {
        var frames = clip.SampleRate <= 0 || sampleRate <= 0 ? 0 : (int)Math.Ceiling(clip.Seconds * sampleRate);
        var heard = new float[frames];

        for (var i = 0; i < frames; i++) heard[i] = (float)clip.At(i / (double)sampleRate);

        return new RecordedLineIn(heard);
    }

    /// <inheritdoc />
    public void Next(out float left, out float right)
    {
        if (at >= lefts.Length)
        {
            left = right = 0f;
            return;
        }

        left = lefts[at];
        right = rights is { } other && at < other.Length ? other[at] : left;
        at++;
    }
}
