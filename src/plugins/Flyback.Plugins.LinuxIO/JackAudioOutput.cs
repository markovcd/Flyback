using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.LinuxIO;

/// <summary>
/// Offers output to a JACK server, and wins over ALSA while one is running.
/// </summary>
public sealed class JackAudioOutput : IAudioOutput
{
    /// <summary>What the choice of where the ports connect is filed under.</summary>
    public const string ConnectKey = "connect";

    /// <summary>Patch both ports into the first two physical playback ports.</summary>
    public const string SystemPlayback = "system";

    /// <summary>Leave the ports unconnected, for a patchbay to route.</summary>
    public const string Unconnected = "none";

    private readonly Func<bool> running;

    public JackAudioOutput()
        : this(() => OperatingSystem.IsLinux() && LibJack.IsInstalled && JackServer.IsRunning)
    {
    }

    /// <summary>An output that asks <paramref name="running"/> whether a server answers.</summary>
    internal JackAudioOutput(Func<bool> running) => this.running = running;

    public string Id => "jack";

    public string Name => "JACK";

    /// <summary>Above ALSA's 100: somebody who started a server wants to be heard through it.</summary>
    public int Priority => 150;

    public bool IsSupported => running();

    public IReadOnlyList<SettingField> Form(SettingValues values)
    {
        if (!IsSupported) return [];

        return
        [
            new SettingField.Pick(
                ConnectKey,
                "Connect to",
                [
                    new SettingOption(SystemPlayback, "System playback"),
                    new SettingOption(Unconnected, "Nothing (route it yourself)"),
                ],
                SystemPlayback)
            {
                Note = "The JACK server sets the sample rate and the latency.",
            },
        ];
    }

    public IAudioDevice Create(AudioFormat format, SettingValues settings) =>
        new JackAudioDevice(settings.Text(ConnectKey, SystemPlayback) != Unconnected);
}
