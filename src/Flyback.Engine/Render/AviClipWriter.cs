namespace Flyback.Core.Render;

/// <summary>
/// The clip this program writes by itself: Motion JPEG in an AVI, as ADR-0036
/// settled it.
/// </summary>
/// <remarks>
/// A frame arriving more than once is compressed once, which is the reason
/// <see cref="IClipWriter.WriteFrame"/> takes a count rather than being called in
/// a loop: the JPEG is the expensive part of a take and a repeated frame is the
/// same bytes twice.
/// </remarks>
public sealed class AviClipWriter : IClipWriter
{
    private readonly Stream? owned;
    private readonly AviWriter avi;
    private readonly JpegWriter jpeg;
    private readonly MemoryStream encoded;
    private readonly int width;
    private readonly int height;

    /// <param name="output">Where the file is written. It has to seek — the header is patched at the end.</param>
    /// <param name="target">Size, rate, quality and whether there is sound.</param>
    /// <param name="owned">Whether disposing this closes <paramref name="output"/> too.</param>
    public AviClipWriter(Stream output, ClipTarget target, bool owned = false)
    {
        this.owned = owned ? output : null;

        width = target.Width;
        height = target.Height;

        avi = new AviWriter(
            output,
            target.Width,
            target.Height,
            target.FramesPerSecond,
            target.HasSound ? target.SampleRate : 0,
            target.HasSound ? target.Channels : 0);

        jpeg = new JpegWriter(target.Quality);
        encoded = new MemoryStream(target.Width * target.Height / 4);
    }

    public long FrameCount => avi.FrameCount;

    public void WriteFrame(ReadOnlySpan<byte> bgra, int stride, int repeat = 1)
    {
        if (repeat <= 0) return;

        encoded.SetLength(0);
        jpeg.WriteBgra(encoded, bgra, width, height, stride);

        var frame = encoded.GetBuffer().AsSpan(0, (int)encoded.Length);

        for (var i = 0; i < repeat; i++) avi.WriteFrame(frame);
    }

    public void WriteAudio(ReadOnlySpan<float> interleaved) => avi.WriteAudio(interleaved);

    public void Dispose()
    {
        // The index can refuse to be written — an AVI at its ceiling — and the
        // file has to be let go of all the same.
        try
        {
            avi.Dispose();
        }
        finally
        {
            encoded.Dispose();
            owned?.Dispose();
        }
    }
}