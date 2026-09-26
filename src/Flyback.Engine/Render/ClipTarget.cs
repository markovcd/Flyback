namespace Flyback.Core.Render;

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