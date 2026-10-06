using System.Runtime.InteropServices.JavaScript;
using Flyback.Plugins.Audio;

namespace Flyback.Editor.Web;

/// <summary>
/// The page's microphone, open while a Line In wants it.
/// </summary>
/// <remarks>
/// Nothing is delivered here: the browser hands what it hears to the sound's worker
/// beside the speaker's queue, so a busy page never keeps the voice waiting. Starting
/// and stopping only say whether the microphone is wanted.
/// </remarks>
internal sealed partial class PageCapture(int sampleRate) : IAudioCapture
{
    public int SampleRate { get; } = sampleRate;

    public int Channels => 2;

    /// <summary>Whether the microphone is wanted; a refusal is said by <see cref="PageMicrophone"/>'s callback, not by stopping.</summary>
    public bool IsRunning { get; private set; }

    public void Start(AudioCaptureCallback deliver)
    {
        IsRunning = true;
        JsListen(true);
    }

    public void Stop()
    {
        IsRunning = false;
        JsListen(false);
    }

    public void Dispose() => Stop();

    /// <summary>Has <paramref name="say"/> called with a sentence whenever the microphone will not open or stops.</summary>
    internal static void OnTrouble(Action<string> say) => JsOnTrouble(say);

    [JSImport("listen", Module)] private static partial void JsListen(bool on);

    [JSImport("onMicrophoneTrouble", Module)]
    private static partial void JsOnTrouble([JSMarshalAs<JSType.Function<JSType.String>>] Action<string> say);

    private const string Module = "speakers";
}
