using Flyback.Plugins.Audio;

namespace Flyback.Plugins.AndroidIO;

/// <summary>Entry point of the Android input and output plugin: sound out through <c>AudioTrack</c>, and in through <c>AudioRecord</c>.</summary>
public sealed class AndroidIOPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new("android.io", "Android sound", "Sound out through AudioTrack and in through AudioRecord.");

    public void Register(IPluginRegistry registry)
    {
        registry.AddAudioOutput(new AudioTrackOutput());
        registry.AddAudioInput(new AudioRecordInput());
    }
}
