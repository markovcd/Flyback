using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;

namespace Flyback.Specs.Support;

/// <summary>A sound backend called whatever the scenario says, which plays nothing.</summary>
public sealed class NamedSoundOutput(string name) : IAudioOutput
{
    public string Id => name.ToLowerInvariant();

    public string Name => name;

    public int Priority => 0;

    public bool IsSupported => true;

    public IAudioDevice Create(AudioFormat format, SettingValues settings) => new SilentAudioDevice(format.SampleRate);
}
