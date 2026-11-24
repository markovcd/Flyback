using Flyback.Plugins.Video;

namespace Flyback.Plugins.Ndi;

/// <summary>One NDI sender, seen on the network for as long as it is open.</summary>
internal sealed unsafe class NdiFeed : IVideoFeed
{
    private readonly NdiRuntime.Api api;
    private readonly VideoFormat format;
    private readonly (int Numerator, int Denominator) rate;
    private IntPtr sender;

    public NdiFeed(NdiRuntime.Api api, VideoFormat format, string name)
    {
        if (format.Width <= 0 || format.Height <= 0) throw new ArgumentOutOfRangeException(nameof(format), "A frame needs both dimensions.");

        this.api = api;
        this.format = format;
        rate = Rate(format.FramesPerSecond);

        sender = api.CreateSender(name);

        if (sender == IntPtr.Zero) throw new InvalidOperationException($"NDI would not make a source called {name}.");
    }

    /// <summary>A frame rate as the whole-number fraction NDI takes, to a thousandth of a frame.</summary>
    internal static (int Numerator, int Denominator) Rate(double fps) =>
        double.IsFinite(fps) && fps > 0 ? ((int)Math.Round(fps * 1000), 1000) : (30000, 1000);

    /// <summary>What <see cref="Send"/> hands NDI for a frame, its pixels aside.</summary>
    internal static NdiVideoFrame Describe(VideoFormat format, (int Numerator, int Denominator) rate, int stride, TimeSpan time) => new()
    {
        Width = format.Width,
        Height = format.Height,
        FourCC = NdiVideoFrame.Bgrx,
        FrameRateNumerator = rate.Numerator,
        FrameRateDenominator = rate.Denominator,
        AspectRatio = format.Width / (float)format.Height,
        FrameFormat = NdiVideoFrame.Progressive,
        Timecode = time.Ticks,
        LineStride = stride,
    };

    public void Send(in VideoFrame frame)
    {
        ObjectDisposedException.ThrowIf(sender == IntPtr.Zero, this);

        if (frame.Stride < format.Width * 4 || frame.Bgra.Length < (long)frame.Stride * (format.Height - 1) + format.Width * 4)
            throw new ArgumentException($"A frame of {format.Width}x{format.Height} needs more pixels than were sent.", nameof(frame));

        var described = Describe(format, rate, frame.Stride, frame.Time);

        fixed (byte* pixels = frame.Bgra)
        {
            described.Data = (IntPtr)pixels;
            api.SendVideo(sender, described);
        }
    }

    public void Dispose()
    {
        if (sender == IntPtr.Zero) return;

        api.DestroySender(sender);
        sender = IntPtr.Zero;
    }
}
