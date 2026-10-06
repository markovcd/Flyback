using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;

namespace Flyback.Editor.Web;

/// <summary>
/// The page's microphone as the editor's sound input, so a Line In listens through the
/// browser; <see cref="PageCapture"/> opens it, and no device is ever made.
/// </summary>
internal sealed class PageMicrophone : IAudioInput
{
    /// <param name="trouble">What to say when the browser will not give the microphone, or takes it back.</param>
    public PageMicrophone(Action<string> trouble) => PageCapture.OnTrouble(trouble);

    public string Id => "page";

    public string Name => "This page's microphone";

    public int Priority => 0;

    public bool IsSupported => true;

    public IAudioCapture Create(AudioFormat format, SettingValues settings) => new PageCapture(format.SampleRate);
}
