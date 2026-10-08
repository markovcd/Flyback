using Flyback.Core;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;

namespace Flyback.Specs.Support;

/// <summary>A sound input plugged into a scenario, and the microphone it opens: one object, so a step can see whether it is listening.</summary>
public sealed class FakeSoundInput : IAudioInput, IAudioCapture
{
    public string Id => "fake";

    public string Name => "Fake";

    public int Priority => 0;

    public bool IsSupported => true;

    public int SampleRate => GlobalConstants.SampleRate;

    public int Channels => 2;

    public bool IsRunning { get; private set; }

    public IAudioCapture Create(AudioFormat format, SettingValues settings) => this;

    public void Start(AudioCaptureCallback deliver) => IsRunning = true;

    public void Stop() => IsRunning = false;

    public void Dispose() => Stop();
}
