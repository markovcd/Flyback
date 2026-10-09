using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Host;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
using Flyback.Ui.Audio;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>Which backend opens, given two that say whether they can play.</summary>
[Binding]
public sealed class PickingWhatPlaysSteps : IDisposable
{
    private readonly OutputSettings settings = new();
    private PluginCatalog plugins = PluginCatalog.Empty;
    private AudioSetup? opened;

    [Given("WASAPI and ASIO can both play")]
    public void GivenBoth() => plugins = CatalogOf(asioPlays: true);

    [Given("WASAPI can play and ASIO cannot")]
    public void GivenOnlyWasapi() => plugins = CatalogOf(asioPlays: false);

    [When("{word} is picked to play through")]
    public void WhenPicked(string name) => settings.SoundOutput = name.ToLowerInvariant();

    [Then("the sound plays through {word}")]
    public void ThenItPlaysThrough(string name)
    {
        opened = Sound.Open(plugins, settings);

        opened.Output!.Name.ShouldBe(name);
    }

    public void Dispose() => opened?.Device.Dispose();

    private static PluginCatalog CatalogOf(bool asioPlays) =>
        new([], [new Backend("wasapi", "WASAPI", 100, true), new Backend("asio", "ASIO", 50, asioPlays)], NodeCatalog.BuiltIn, Presets.All, []);

    private sealed record Backend(string Id, string Name, int Priority, bool IsSupported) : IAudioOutput
    {
        public IAudioDevice Create(AudioFormat format, SettingValues settings) => new SilentAudioDevice(format.SampleRate);
    }
}
