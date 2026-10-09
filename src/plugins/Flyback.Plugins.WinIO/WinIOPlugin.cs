using Flyback.Plugins.Audio;
using Flyback.Plugins.Midi;

namespace Flyback.Plugins.WinIO;

/// <summary>
/// Entry point of the Windows input and output plugin: sound out through WASAPI or an ASIO
/// driver, notes in through winmm.
/// </summary>
/// <remarks>
/// The two travel together because their condition is the same one, so one folder, one
/// load context and one dependency file carry the pair. Loading this assembly must not
/// touch NAudio or call into winmm, so the plugin lists itself harmlessly on a machine
/// that has neither.
/// </remarks>
public sealed class WinIOPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "win.io",
        "Windows sound and MIDI",
        "Sound out through WASAPI or an ASIO driver, and a patch played from a MIDI keyboard through the multimedia library Windows already has.");

    /// <summary>
    /// Three registrations, in the order the shell asks about them. Neither opens
    /// anything: what is registered is the offer, and the backend decides for
    /// itself whether it can run — see <see cref="IAudioOutput.IsSupported"/> and
    /// <see cref="IMidiInput.IsSupported"/>.
    /// </summary>
    public void Register(IPluginRegistry registry)
    {
        registry.AddAudioOutput(new WasapiAudioOutput());
        registry.AddAudioOutput(new AsioAudioOutput());
        registry.AddMidiInput(new WinMidiInput());
    }
}
