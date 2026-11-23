namespace Flyback.Plugins.Video;

/// <summary>An open <see cref="IVideoOutput"/>, handed every frame until it is disposed.</summary>
public interface IVideoFeed : IDisposable
{
    /// <summary>
    /// Takes one frame, of the size the feed was opened with. Called on the thread that
    /// drew it, once a frame, so it should hand the pixels on and return.
    /// </summary>
    /// <remarks>The pixels are only valid during the call: copy what has to outlive it.</remarks>
    void Send(in VideoFrame frame);
}
