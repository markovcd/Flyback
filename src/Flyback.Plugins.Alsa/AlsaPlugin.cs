using Flyback.Plugins.Audio;

namespace Flyback.Plugins.Alsa;

/// <summary>
/// Entry point of the Linux sound plugin. Loading this assembly must not call
/// into libasound — the library is only reached when a device is created, or
/// when the backend is asked whether it exists here at all.
/// </summary>
public sealed class AlsaPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "alsa",
        "ALSA output",
        "Linux sound through libasound, which is what PipeWire and PulseAudio answer to as well.");

    public void Register(IPluginRegistry registry) => registry.AddAudioOutput(new AlsaAudioOutput());
}

/// <summary>
/// Offers the backend without opening anything. On Windows and macOS this is
/// the class that answers "no", and on Linux it is where a machine with no
/// sound library at all is found out.
/// </summary>
public sealed class AlsaAudioOutput : IAudioOutput
{
    public string Id => "alsa";

    public string Name => "ALSA (default device)";

    /// <summary>
    /// The same 100 the other two native backends claim. None of the three ever
    /// competes with another — each is supported only where the others are not.
    /// </summary>
    public int Priority => 100;

    /// <summary>
    /// Two questions, because on Linux the right operating system is not enough:
    /// a container or a headless server frequently has no libasound, and this is
    /// the last moment the answer can be "no" rather than an exception.
    /// </summary>
    public bool IsSupported => OperatingSystem.IsLinux() && LibAsound.IsInstalled;

    public IAudioDevice Create(AudioFormat format) => new AlsaAudioDevice(format);
}
