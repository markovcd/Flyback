using Flyback.Plugins.Audio;
using Flyback.Plugins.Midi;

namespace Flyback.Plugins.LinuxIO;

/// <summary>
/// Entry point of the Linux input and output plugin: sound out through the ALSA PCM
/// device, notes in through the ALSA sequencer.
/// </summary>
/// <remarks>
/// The two travel together because they are the same library —
/// <c>libasound.so.2</c> plays the one and routes the other — so "is there a sound
/// library at all" is one question, asked in <see cref="LibAsound.IsInstalled"/>.
/// Loading this assembly must not call into libasound, so the plugin lists itself
/// harmlessly on a machine that has none.
/// </remarks>
public sealed class LinuxIOPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "linux.io",
        "Linux sound and MIDI",
        "Sound and MIDI through libasound, which is what PipeWire and PulseAudio answer to as well.");

    /// <summary>
    /// Two registrations, in the order the shell asks about them. Neither opens
    /// anything: what is registered is the offer, and the backend decides for
    /// itself whether it can run — see <see cref="IAudioOutput.IsSupported"/> and
    /// <see cref="IMidiInput.IsSupported"/>.
    /// </summary>
    public void Register(IPluginRegistry registry)
    {
        registry.AddAudioOutput(new AlsaAudioOutput());
        registry.AddMidiInput(new AlsaMidiInput());
    }
}
