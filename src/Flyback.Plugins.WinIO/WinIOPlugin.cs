using Flyback.Plugins.Audio;
using Flyback.Plugins.Midi;

namespace Flyback.Plugins.WinIO;

/// <summary>
/// Entry point of the Windows input and output plugin: sound out through WASAPI,
/// notes in through winmm.
/// </summary>
/// <remarks>
/// <para>
/// Sound and MIDI travel together because their condition is the same one: both
/// are supported on exactly the operating system <c>Platform="win"</c> already
/// names, so one folder, one load context and one dependency file carry the
/// pair.
/// </para>
/// <para>
/// Loading this assembly must not touch NAudio or call into winmm. The one is
/// only reached when a device is actually created and the other when devices are
/// listed or one is opened, so the plugin lists itself harmlessly on a machine
/// that has neither.
/// </para>
/// </remarks>
public sealed class WinIOPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "win.io",
        "Windows sound and MIDI",
        "Sound out through WASAPI, and a patch played from a MIDI keyboard through the multimedia library Windows already has.");

    /// <summary>
    /// Two registrations, in the order the shell asks about them. Neither opens
    /// anything: what is registered is the offer, and the backend decides for
    /// itself whether it can run — see <see cref="IAudioOutput.IsSupported"/> and
    /// <see cref="IMidiInput.IsSupported"/>.
    /// </summary>
    public void Register(IPluginRegistry registry)
    {
        registry.AddAudioOutput(new WasapiAudioOutput());
        registry.AddMidiInput(new WinMidiInput());
    }
}
