using Flyback.Core;

namespace Flyback.Plugins.Audio;

/// <summary>
/// What the host asks a backend to open. A backend that cannot honor the
/// request exactly is free to open the nearest thing it can and report the
/// truth through <see cref="IAudioDevice.SampleRate"/>.
/// </summary>
public readonly record struct AudioFormat(int SampleRate, int Channels, int LatencyMilliseconds)
{
    public static AudioFormat Default => new(GlobalConstants.SampleRate, 2, 30);
}