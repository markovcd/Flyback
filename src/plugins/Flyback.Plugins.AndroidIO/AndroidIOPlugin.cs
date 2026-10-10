using Flyback.Plugins.Audio;
using Flyback.Plugins.Midi;

namespace Flyback.Plugins.AndroidIO;

/// <summary>Entry point of the Android input and output plugin: sound out through <c>AudioTrack</c>, in through <c>AudioRecord</c>, and MIDI in through <c>MidiManager</c>.</summary>
public sealed class AndroidIOPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new("android.io", "Android sound and MIDI", "Sound out through AudioTrack, in through AudioRecord, and MIDI in through MidiManager.");

    public void Register(IPluginRegistry registry)
    {
        registry.AddAudioOutput(new AudioTrackOutput());
        registry.AddAudioInput(new AudioRecordInput());
        registry.AddMidiInput(new MidiManagerInput());
    }
}
