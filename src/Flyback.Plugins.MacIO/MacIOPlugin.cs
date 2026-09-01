using Flyback.Plugins.Audio;
using Flyback.Plugins.Midi;

namespace Flyback.Plugins.MacIO;

/// <summary>
/// Entry point of the macOS input and output plugin: sound out through the
/// default output audio unit, notes in through CoreMIDI.
/// </summary>
/// <remarks>
/// <para>
/// Sound and MIDI travel together because their condition is the same one: both
/// frameworks are part of the operating system <c>Platform="osx"</c> already
/// names, so one folder, one load context and one dependency file carry the
/// pair.
/// </para>
/// <para>
/// Loading this assembly must not call into Audio Toolbox or CoreMIDI. The one
/// is only reached when a device is actually created and the other when devices
/// are listed or one is opened, so the plugin lists itself harmlessly on a
/// machine that has no such framework at all.
/// </para>
/// </remarks>
public sealed class MacIOPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "mac.io",
        "macOS sound and MIDI",
        "Sound out through the default output audio unit, and a patch played from a MIDI keyboard through the framework every Mac routes MIDI over.");

    /// <summary>
    /// Two registrations, in the order the shell asks about them. Neither opens
    /// anything: what is registered is the offer, and the backend decides for
    /// itself whether it can run — see <see cref="IAudioOutput.IsSupported"/> and
    /// <see cref="IMidiInput.IsSupported"/>.
    /// </summary>
    public void Register(IPluginRegistry registry)
    {
        registry.AddAudioOutput(new CoreAudioOutput());
        registry.AddMidiInput(new CoreMidiInput());
    }
}
