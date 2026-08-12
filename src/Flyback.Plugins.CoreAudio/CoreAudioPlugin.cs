using Flyback.Plugins.Audio;

namespace Flyback.Plugins.CoreAudio;

/// <summary>
/// Entry point of the macOS sound plugin. Loading this assembly must not call
/// into Audio Toolbox — the framework is only reached when a device is actually
/// created, so the plugin lists itself harmlessly on a machine that has no such
/// framework at all.
/// </summary>
public sealed class CoreAudioPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "coreaudio",
        "CoreAudio output",
        "macOS sound output through the default output audio unit.");

    public void Register(IPluginRegistry registry) => registry.AddAudioOutput(new CoreAudioOutput());
}

/// <summary>
/// Offers the backend without opening anything. On Windows and Linux this is
/// the class that answers "no", which is what keeps the plugin loadable
/// everywhere even though the device is not.
/// </summary>
public sealed class CoreAudioOutput : IAudioOutput
{
    public string Id => "coreaudio";

    public string Name => "CoreAudio (default output)";

    /// <summary>
    /// The same 100 the WASAPI backend claims, and for the same reason: where it
    /// works it is the native path, and a portable backend installed alongside
    /// it should lose. The two never compete — each is supported only where the
    /// other is not — so the equal priority costs nothing.
    /// </summary>
    public int Priority => 100;

    public bool IsSupported => OperatingSystem.IsMacOS();

    public IAudioDevice Create(AudioFormat format) => new CoreAudioDevice(format);
}
