using Flyback.Core;
using Flyback.Plugins.Audio;

namespace Flyback.Specs.Support;

/// <summary>A sound card that plays nothing and hands back what it was given.</summary>
public sealed class Loopback : IAudioDevice
{
    private AudioCallback? fill;

    public int SampleRate => GlobalConstants.SampleRate;

    public bool IsRunning => fill is not null;

    public void Start(AudioCallback fill) => this.fill = fill;

    public void Stop() => fill = null;

    public void Dispose() => Stop();

    public void Pull(Span<float> buffer) =>
        (fill ?? throw new InvalidOperationException("The engine never started the device."))(buffer);
}
