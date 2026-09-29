using Avalonia;
using Flyback.Core.Render;

namespace Flyback.App.Capture;

/// <summary>What a recording is, before it exists.</summary>
/// <param name="Path">Where it goes. The extension already agrees with <paramref name="Format"/>.</param>
/// <param name="Format">Which of <see cref="ClipFormats"/> it is written as.</param>
/// <param name="Size">Frame size, or an empty size for a sound-only take.</param>
/// <param name="Channels">Zero writes a silent video, the way a video-only export does.</param>
/// <param name="Ffmpeg">Where ffmpeg is, for a format that needs it.</param>
internal readonly record struct RecordingSettings(
    string Path,
    ClipFormat Format,
    PixelSize Size,
    double FramesPerSecond,
    int Quality,
    int SampleRate,
    int Channels,
    string? Ffmpeg = null)
{
    public bool HasPicture => Format.HasPicture && Size.Width > 0 && Size.Height > 0;

    public bool HasSound => Channels > 0 && SampleRate > 0;

    /// <summary>What the writer is opened against — the same shape a render uses.</summary>
    public ClipTarget Target => new(
        Path,
        Format,
        Size.Width,
        Size.Height,
        FramesPerSecond,
        Quality,
        HasSound ? SampleRate : 0,
        HasSound ? Channels : 0,
        Ffmpeg);
}