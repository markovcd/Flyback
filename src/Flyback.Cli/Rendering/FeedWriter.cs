using System.Diagnostics;
using Flyback.Engine.Render;
using Flyback.Plugins.Video;

namespace Flyback.Cli.Rendering;

/// <summary>
/// A clip that is a <see cref="IVideoFeed"/> rather than a file: each frame is sent on
/// as it is drawn, and the sound is dropped, since a feed's sound is a sound backend's.
/// </summary>
/// <param name="first">The patch's time at the first frame written.</param>
/// <param name="paced">Holds each frame back until its moment comes round, so the feed plays at the speed it would be watched.</param>
internal sealed class FeedWriter(IVideoFeed feed, double fps, TimeSpan first, bool paced) : IClipWriter
{
    private readonly Stopwatch clock = Stopwatch.StartNew();

    public long FrameCount { get; private set; }

    public void WriteFrame(ReadOnlySpan<byte> bgra, int stride, int repeat = 1)
    {
        for (var copy = 0; copy < repeat; copy++)
        {
            var since = TimeSpan.FromSeconds(FrameCount / fps);

            if (paced && since - clock.Elapsed is { Ticks: > 0 } early) Thread.Sleep(early);

            feed.Send(new VideoFrame(bgra, stride, first + since));
            FrameCount++;
        }
    }

    public void WriteAudio(ReadOnlySpan<float> interleaved) { }

    public void Dispose() => feed.Dispose();
}
