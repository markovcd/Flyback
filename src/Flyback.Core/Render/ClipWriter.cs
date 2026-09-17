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

/// <summary>What a clip is, before it exists.</summary>
/// <param name="Path">Where it goes, extension and all.</param>
/// <param name="Format">Which of <see cref="ClipFormats"/> it is written as.</param>
/// <param name="Width">Frame width, ignored by a sound-only format.</param>
/// <param name="Height">Frame height, ignored by a sound-only format.</param>
/// <param name="Quality">
/// 1 to 100, read as a JPEG quality or as a constant rate factor depending on the
/// format. The sound formats fix their own and ignore this.
/// </param>
/// <param name="Channels">Zero writes a clip with no sound in it.</param>
/// <param name="Ffmpeg">
/// Where ffmpeg is, for a format that needs one. Null with such a format is a
/// caller that did not check <see cref="Ffmpeg.Resolve"/> first, and is refused.
/// </param>
public readonly record struct ClipTarget(
    string Path,
    ClipFormat Format,
    int Width = 0,
    int Height = 0,
    double FramesPerSecond = MovieRenderer.DefaultFrameRate,
    int Quality = JpegWriter.DefaultQuality,
    int SampleRate = 0,
    int Channels = 0,
    string? Ffmpeg = null)
{
    public bool HasSound => Channels > 0 && SampleRate > 0;
}

/// <summary>Opens the writer a <see cref="ClipTarget"/> asks for.</summary>
public static class ClipWriter
{
    /// <summary>
    /// The one place a format becomes an encoder. Everything that writes a clip
    /// comes through here, so the shell's take and the command line's render
    /// cannot end up supporting different lists.
    /// </summary>
    public static IClipWriter Open(ClipTarget target)
    {
        if (target.Format.NeedsFfmpeg)
        {
            var ffmpeg = target.Ffmpeg
                ?? throw new InvalidOperationException($"{target.Format.Label} needs ffmpeg, and none was found.");

            return new FfmpegClipWriter(target, ffmpeg);
        }

        // Left to the writers below to dispose along with themselves, so that a
        // caller holds one thing per clip whichever format it is.
        var file = File.Create(target.Path);

        try
        {
            return target.Format.HasPicture
                ? new AviClipWriter(file, target, owned: true)
                : new WavClipWriter(file, target, owned: true);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }
}
