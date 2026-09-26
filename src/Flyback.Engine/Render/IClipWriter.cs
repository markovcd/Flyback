namespace Flyback.Core.Render;

/// <summary>
/// Where a clip's frames and samples go. One of these per file, whichever format
/// it is and whoever encodes it.
/// </summary>
/// <remarks>
/// Frames come in as BGRA rather than as anything compressed, because what each
/// format wants of them differs and the caller has no business knowing which:
/// <see cref="AviClipWriter"/> makes a JPEG of every one and
/// <see cref="FfmpegClipWriter"/> hands them over raw.
/// <para>
/// <see cref="IDisposable.Dispose"/> is not a formality on any of these. Every
/// format here learns its own length at the end — a patched header, an index, or
/// a muxer's trailer — so a writer abandoned without it has not written a clip.
/// </para>
/// </remarks>
public interface IClipWriter : IDisposable
{
    /// <summary>Frames written so far, counting a repeated frame once per copy.</summary>
    long FrameCount { get; }

    /// <summary>
    /// Appends one frame, top row first.
    /// </summary>
    /// <param name="bgra">The pixels, blue first, four bytes each.</param>
    /// <param name="stride">Bytes from one row to the next, which need not be tight.</param>
    /// <param name="repeat">
    /// How many times this frame goes in. More than one is a live take whose
    /// source did not draw fast enough — see <c>CapturePacer</c> — and is a
    /// parameter rather than a loop at the call site so that a format paying to
    /// encode a frame pays once.
    /// </param>
    void WriteFrame(ReadOnlySpan<byte> bgra, int stride, int repeat = 1);

    /// <summary>Appends interleaved samples, in the channel count this was opened with.</summary>
    void WriteAudio(ReadOnlySpan<float> interleaved);
}