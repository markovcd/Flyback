using Flyback.Core;
using Flyback.Plugins.Audio;

namespace Flyback.Plugins.Testing;

/// <summary>
/// A sound card the test is the thread of: it keeps the callback it was started with,
/// and a buffer is made when the test asks for one rather than on a clock.
/// </summary>
public sealed class LoopbackDevice : IAudioDevice
{
    private AudioCallback? fill;

    public int SampleRate => GlobalConstants.SampleRate;

    public bool IsRunning => fill is not null;

    public TimeSpan Latency { get; set; }

    /// <summary>Whether whoever opened the device let it go.</summary>
    public bool Disposed { get; private set; }

    public void Start(AudioCallback fill) => this.fill = fill;

    public void Stop() => fill = null;

    public void Dispose()
    {
        Stop();
        Disposed = true;
    }

    /// <summary>Asks for <paramref name="frames"/> of stereo sound, as the card would, and hands them back.</summary>
    public float[] Pump(int frames = 512)
    {
        var buffer = new float[frames * 2];

        Fill(buffer);

        return buffer;
    }

    /// <summary>Has <paramref name="buffer"/> filled by whatever started the device.</summary>
    public void Fill(Span<float> buffer) =>
        (fill ?? throw new InvalidOperationException("nothing started the device"))(buffer);
}
