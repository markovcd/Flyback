using Flyback.Plugins.Settings;
using Flyback.Plugins.Video;

namespace Flyback.Plugins.Sample;

/// <summary>
/// The worked example of a picture output: it sends the frames nowhere, and only
/// checks each is the size it was opened for and no earlier than the last.
/// </summary>
public sealed class TallyOutput : IVideoOutput
{
    public string Id => "flyback.sample.tally";

    public string Name => "Tally (sends nowhere)";

    public bool IsSupported => true;

    public IVideoFeed Create(VideoFormat format, SettingValues settings) => new Feed(format);

    private sealed class Feed(VideoFormat format) : IVideoFeed
    {
        private TimeSpan last = TimeSpan.MinValue;

        public void Send(in VideoFrame frame)
        {
            if (frame.Stride < format.Width * 4 || frame.Bgra.Length < frame.Stride * (format.Height - 1) + format.Width * 4)
                throw new ArgumentException($"A frame of {format.Width}x{format.Height} needs more pixels than were sent.", nameof(frame));

            if (frame.Time < last)
                throw new ArgumentException("A frame came before the one sent ahead of it.", nameof(frame));

            last = frame.Time;
        }

        public void Dispose() { }
    }
}
