namespace Flyback.Plugins.Video;

/// <summary>
/// One drawn frame: its pixels, top row first, four bytes each in blue, green, red,
/// alpha order, and the moment of the patch it shows.
/// </summary>
/// <remarks>A ref struct, so the pixels cannot be kept past <see cref="IVideoFeed.Send"/>.</remarks>
public readonly ref struct VideoFrame
{
    internal VideoFrame(ReadOnlySpan<byte> bgra, int stride, TimeSpan time)
    {
        Bgra = bgra;
        Stride = stride;
        Time = time;
    }

    /// <summary>The pixels, blue first, four bytes each.</summary>
    public ReadOnlySpan<byte> Bgra { get; }

    /// <summary>Bytes from one row to the next, which need not be the width times four.</summary>
    public int Stride { get; }

    /// <summary>Where the patch's clock stood for this frame, which a sound sent alongside can be lined up by.</summary>
    public TimeSpan Time { get; }
}
