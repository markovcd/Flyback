using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.AndroidIO;

/// <summary>The device's microphone, or whatever Android routes recording from.</summary>
public sealed class AudioRecordInput : IAudioInput
{
    public string Id => "audiorecord";

    public string Name => "AudioRecord";

    public int Priority => 100;

    public bool IsSupported => OperatingSystem.IsAndroid();

    public IAudioCapture Create(AudioFormat format, SettingValues settings) => new AudioRecordCapture(format);
}
