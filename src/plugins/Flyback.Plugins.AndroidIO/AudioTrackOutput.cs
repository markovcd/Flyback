using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.AndroidIO;

/// <summary>The device's speakers, or whatever Android routes media to.</summary>
public sealed class AudioTrackOutput : IAudioOutput
{
    public string Id => "audiotrack";

    public string Name => "AudioTrack";

    /// <summary>The native backends' 100; it is the only one supported on Android.</summary>
    public int Priority => 100;

    public bool IsSupported => OperatingSystem.IsAndroid();

    public IAudioDevice Create(AudioFormat format, SettingValues settings) => new AudioTrackDevice(format);
}
