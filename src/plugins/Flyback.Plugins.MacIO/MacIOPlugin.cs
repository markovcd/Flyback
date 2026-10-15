using Flyback.Plugins.Audio;
using Flyback.Plugins.Midi;

namespace Flyback.Plugins.MacIO;

/// <summary>
/// Entry point of the macOS input and output plugin: sound out through the default
/// output audio unit, sound in through an Audio Toolbox input queue, notes in through CoreMIDI.
/// </summary>
/// <remarks>
/// The two travel together because their condition is the same one, so one folder, one
/// load context and one dependency file carry the pair. Loading this assembly must not
/// call into either framework, so the plugin lists itself harmlessly on a machine that
/// has neither.
/// </remarks>
public sealed class MacIOPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "mac.io",
        "macOS sound and MIDI",
        "Sound out through the default output audio unit, sound in through an input queue, and a patch played from a MIDI keyboard through the framework every Mac routes MIDI over.");

    /// <summary>
    /// Three registrations, in the order the shell asks about them. None opens
    /// anything: what is registered is the offer, and the backend decides for
    /// itself whether it can run — see <see cref="IAudioOutput.IsSupported"/>,
    /// <see cref="IAudioInput.IsSupported"/> and <see cref="IMidiInput.IsSupported"/>.
    /// </summary>
    public void Register(IPluginRegistry registry)
    {
        registry.AddAudioOutput(new CoreAudioOutput());
        registry.AddAudioInput(new CoreAudioInput());
        registry.AddMidiInput(new CoreMidiInput());
    }
}
