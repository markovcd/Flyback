using Flyback.Plugins.Audio;

namespace Flyback.Plugins.WinIO;

/// <summary>
/// Offers the backend without opening anything. On Linux and macOS this is the
/// class that answers "no", which is what keeps the plugin loadable everywhere
/// even though the device is not.
/// </summary>
public sealed class WasapiAudioOutput : IAudioOutput
{
    public string Id => "wasapi";

    public string Name => "WASAPI (shared mode)";

    /// <summary>
    /// Above the default of zero: where WASAPI works it is the lowest-latency
    /// path available, so a portable backend installed alongside it should lose.
    /// </summary>
    public int Priority => 100;

    public bool IsSupported => OperatingSystem.IsWindows();

    public IAudioDevice Create(AudioFormat format) => new WasapiAudioDevice(format);
}
