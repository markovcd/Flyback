using Flyback.Plugins.Audio;

namespace Flyback.Plugins.AndroidIO;

/// <summary>Entry point of the Android input and output plugin: sound out through <c>AudioTrack</c>.</summary>
public sealed class AndroidIOPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new("android.io", "Android sound", "Sound through AudioTrack.");

    public void Register(IPluginRegistry registry) => registry.AddAudioOutput(new AudioTrackOutput());
}
