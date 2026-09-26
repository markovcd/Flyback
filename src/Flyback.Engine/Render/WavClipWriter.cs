namespace Flyback.Core.Render;

/// <summary>
/// The sound-only clip this program writes by itself: a WAV whose length is
/// patched in at the end.
/// </summary>
public sealed class WavClipWriter : IClipWriter
{
    private readonly Stream? owned;
    private readonly WavStreamWriter wav;

    /// <inheritdoc cref="AviClipWriter(Stream, ClipTarget, bool)"/>
    public WavClipWriter(Stream output, ClipTarget target, bool owned = false)
    {
        if (!target.HasSound) throw new ArgumentException("A sound-only clip needs a rate and channels.", nameof(target));

        this.owned = owned ? output : null;

        wav = new WavStreamWriter(output, target.SampleRate, target.Channels);
    }

    /// <summary>Always nought. There is no picture in this one to count.</summary>
    public long FrameCount => 0;

    public void WriteFrame(ReadOnlySpan<byte> bgra, int stride, int repeat = 1) =>
        throw new InvalidOperationException("This clip has no picture in it.");

    public void WriteAudio(ReadOnlySpan<float> interleaved) => wav.WriteAudio(interleaved);

    public void Dispose()
    {
        try
        {
            wav.Dispose();
        }
        finally
        {
            owned?.Dispose();
        }
    }
}