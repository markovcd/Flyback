using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;

namespace Flyback.Editor.Web;

/// <summary>
/// The page's speakers as the editor's sound backend, so Volume turns the sound on;
/// <see cref="PageSound"/> plays through them, and no device is ever made.
/// </summary>
internal sealed class PageSpeakers : IAudioOutput
{
    public string Id => "page";

    /// <summary>The browser's API the page's sound plays through.</summary>
    public const string Named = "Web Audio";

    public string Name => Named;

    public int Priority => 0;

    public bool IsSupported => true;

    public IAudioDevice Create(AudioFormat format, SettingValues settings) => new SilentAudioDevice(format.SampleRate);
}
