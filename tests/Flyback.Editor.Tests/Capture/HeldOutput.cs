using System.Collections.Concurrent;
using Flyback.Plugins.Settings;
using Flyback.Plugins.Video;

namespace Flyback.Editor.Tests.Capture;

/// <summary>A picture output that keeps what it was opened with and every frame it was sent.</summary>
internal sealed class HeldOutput(string id = "held", bool supported = true, string? refuses = null) : IVideoOutput
{
    public string Id => id;

    public string Name => $"Held ({id})";

    public bool IsSupported => supported;

    public IReadOnlyList<SettingField> Form(SettingValues values) => [new SettingField.Text("name", "Seen as", "Flyback")];

    /// <summary>Every opening, in order, with the name it was given.</summary>
    public ConcurrentQueue<(VideoFormat Format, string Name)> Opened { get; } = [];

    /// <summary>Every frame sent, as its pixels top row first.</summary>
    public ConcurrentQueue<byte[]> Frames { get; } = [];

    public int Closed => closed;

    private int closed;

    public IVideoFeed Create(VideoFormat format, SettingValues settings)
    {
        if (refuses is not null) throw new InvalidOperationException(refuses);

        Opened.Enqueue((format, settings.Text("name")));

        return new Feed(this, format);
    }

    private sealed class Feed(HeldOutput output, VideoFormat format) : IVideoFeed
    {
        public void Send(in VideoFrame frame)
        {
            var copy = new byte[format.Width * format.Height * 4];

            for (var y = 0; y < format.Height; y++)
                frame.Bgra.Slice(y * frame.Stride, format.Width * 4).CopyTo(copy.AsSpan(y * format.Width * 4));

            output.Frames.Enqueue(copy);
        }

        public void Dispose() => Interlocked.Increment(ref output.closed);
    }
}
