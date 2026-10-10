using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.Testing;

/// <summary>The sound backend a <see cref="LoopbackDevice"/> came from, which is what lets a host start it.</summary>
public sealed class LoopbackOutput(IAudioDevice device) : IAudioOutput
{
    public string Id => "loopback";

    public string Name => "Loopback";

    public int Priority => 0;

    public bool IsSupported => true;

    public IAudioDevice Create(AudioFormat format, SettingValues settings) => device;
}
