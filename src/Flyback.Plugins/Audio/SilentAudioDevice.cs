using Flyback.Core;
using Flyback.Core.Render;

namespace Flyback.Plugins.Audio;

/// <summary>
/// The device used when no backend is available. It accepts the callback and never
/// calls it, so the rest of the program needs no null checks.
/// </summary>
/// <remarks>
/// Deliberately not a clock: it reports <see cref="IsRunning"/> honestly and produces
/// no samples, so a caller driving the picture from the audio cursor would freeze —
/// the shell disables sound outright when this is what it got.
/// </remarks>
public sealed class SilentAudioDevice(int sampleRate = GlobalConstants.SampleRate) : IAudioDevice
{
    public int SampleRate { get; } = sampleRate;

    public bool IsRunning { get; private set; }

    public void Start(AudioCallback fill) => IsRunning = true;

    public void Stop() => IsRunning = false;

    public void Dispose() => Stop();
}
