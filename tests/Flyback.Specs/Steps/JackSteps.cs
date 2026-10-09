using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
using Flyback.Plugins.Testing;
using Flyback.Specs.Support;
using Reqnroll;
using Reqnroll.UnitTestProvider;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The JACK output against a server this scenario starts, or one already running.</summary>
[Binding]
public sealed class JackSteps(IUnitTestRuntimeProvider runtime) : IDisposable
{
    private static readonly PluginCatalog Installed = PluginHost.Load(PluginHost.DefaultDirectory, PluginTrust.Shipped(PluginHost.DefaultDirectory));

    private JackDaemon? daemon;
    private IAudioDevice? device;
    private long samples;

    [Given("a JACK server is running")]
    public void GivenAServer()
    {
        daemon = new JackDaemon(Installed.AudioOutputs.Single(o => o.Id == "jack"));

        Needs.Tool(runtime, daemon.Available, daemon.Why);
    }

    [Then("the sound plays through JACK")]
    public static void ThenItPlaysThroughJack() => Installed.PreferredAudioOutput!.Id.ShouldBe("jack");

    [When("the sound plays through JACK for {int} seconds")]
    public void WhenItPlays(int seconds)
    {
        var output = Installed.PreferredAudioOutput!;

        device = output.Create(new AudioFormat(48000, 2, 30), SettingValues.None.With("connect", "none"));

        var wanted = (long)seconds * device.SampleRate * 2;
        using var enough = new ManualResetEventSlim();

        device.Start(buffer =>
        {
            buffer.Clear();

            if (Interlocked.Add(ref samples, buffer.Length) >= wanted) enough.Set();
        });

        enough.Wait(TimeSpan.FromSeconds(seconds * 5 + 10)).ShouldBeTrue();
    }

    [Then("the server has taken {int} seconds of sound at its own rate")]
    public void ThenItTookSound(int seconds) =>
        Interlocked.Read(ref samples).ShouldBeGreaterThanOrEqualTo((long)seconds * device!.SampleRate * 2);

    public void Dispose()
    {
        device?.Dispose();
        daemon?.Dispose();
    }
}
