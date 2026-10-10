using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// Which backend plays, given several. Everything here is decided from what a
/// backend says about itself, without opening a device.
/// </summary>
public class AudioOutputSelectionTests
{
    private static PluginCatalog CatalogOf(params IAudioOutput[] outputs) =>
        new([], outputs, Core.Graph.NodeCatalog.BuiltIn, Engine.Graph.Presets.All, []);

    [Fact]
    public void Nothing_installed_means_nothing_plays()
    {
        CatalogOf().PreferredAudioOutput.ShouldBeNull();
    }

    [Fact]
    public void The_highest_priority_backend_wins()
    {
        var catalog = CatalogOf(
            new FakeOutput("portable", Priority: 0),
            new FakeOutput("native", Priority: 100));

        catalog.PreferredAudioOutput!.Id.ShouldBe("native");
    }

    [Fact]
    public void A_backend_that_cannot_run_here_is_passed_over()
    {
        var catalog = CatalogOf(
            new FakeOutput("native", Priority: 100, Supported: false),
            new FakeOutput("portable", Priority: 0));

        catalog.PreferredAudioOutput!.Id.ShouldBe("portable");
    }

    [Fact]
    public void Nothing_supported_means_nothing_plays()
    {
        CatalogOf(new FakeOutput("native", Supported: false)).PreferredAudioOutput.ShouldBeNull();
    }

    /// <summary>
    /// Otherwise which device opens would depend on directory enumeration
    /// order, and the same install would behave differently on two machines.
    /// </summary>
    [Fact]
    public void Equal_priorities_break_on_id_so_the_choice_is_repeatable()
    {
        var forwards = CatalogOf(new FakeOutput("alsa"), new FakeOutput("oss"));
        var backwards = CatalogOf(new FakeOutput("oss"), new FakeOutput("alsa"));

        forwards.PreferredAudioOutput!.Id.ShouldBe("alsa");
        backwards.PreferredAudioOutput!.Id.ShouldBe("alsa");
    }

    /// <summary>
    /// A plugin is third-party code. One that throws while being asked a
    /// question has answered no, and must not take the program with it.
    /// </summary>
    [Fact]
    public void A_backend_that_throws_when_asked_has_said_no()
    {
        var catalog = CatalogOf(new ThrowingOutput(), new FakeOutput("portable"));

        catalog.PreferredAudioOutput!.Id.ShouldBe("portable");
    }

    [Fact]
    public void A_backend_picked_by_id_plays_over_the_one_that_ranks_higher()
    {
        var catalog = CatalogOf(new FakeOutput("wasapi", Priority: 100), new FakeOutput("asio", Priority: 50));

        catalog.AudioOutput("asio")!.Id.ShouldBe("asio");
        catalog.AudioOutput("")!.Id.ShouldBe("wasapi");
        catalog.PlayableAudioOutputs.Select(o => o.Id).ShouldBe(["wasapi", "asio"]);
    }

    /// <summary>A driver uninstalled, or a server not started today, plays what would have played without the pick.</summary>
    [Fact]
    public void A_picked_backend_that_cannot_play_here_gives_way_to_the_preferred_one()
    {
        var catalog = CatalogOf(new FakeOutput("wasapi", Priority: 100), new FakeOutput("asio", Supported: false));

        catalog.AudioOutput("asio")!.Id.ShouldBe("wasapi");
        catalog.AudioOutput("gone")!.Id.ShouldBe("wasapi");
        catalog.PlayableAudioOutputs.Select(o => o.Id).ShouldBe(["wasapi"]);
    }

    private sealed record FakeOutput(string Id, int Priority = 0, bool Supported = true) : IAudioOutput
    {
        public string Name => Id;

        public bool IsSupported => Supported;

        public IAudioDevice Create(AudioFormat format, SettingValues settings) => new SilentAudioDevice(format.SampleRate);
    }

    private sealed class ThrowingOutput : IAudioOutput
    {
        public string Id => "broken";

        public string Name => "Broken";

        public int Priority => 1000;

        public bool IsSupported => throw new InvalidOperationException("no");

        public IAudioDevice Create(AudioFormat format, SettingValues settings) => throw new InvalidOperationException("no");
    }
}
