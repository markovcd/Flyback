using Flyback.Plugins.Audio;
using Flyback.Plugins.Testing;

namespace Flyback.Plugins.Tests;

/// <summary>A <see cref="JackDaemon"/> for the length of a test class.</summary>
public sealed class JackServerFixture : IDisposable
{
    private readonly JackDaemon daemon;

    public JackServerFixture() => daemon = new JackDaemon(Output);

    public IAudioOutput Output { get; } = ShippedPlugins.Loaded.AudioOutputs.Single(o => o.Id == "jack");

    public bool Available => daemon.Available;

    public void Dispose() => daemon.Dispose();
}
