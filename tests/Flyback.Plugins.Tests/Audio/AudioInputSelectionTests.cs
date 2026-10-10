using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Audio;

/// <summary>
/// Which backend listens, given several. Decided from what a backend says about itself,
/// without opening a device, as an output's is.
/// </summary>
public class AudioInputSelectionTests
{
    private static PluginCatalog CatalogOf(params IAudioInput[] inputs) =>
        new([], [], Core.Graph.NodeCatalog.BuiltIn, Engine.Graph.Presets.All, [], audioInputs: inputs);

    [Fact]
    public void Nothing_installed_means_nothing_listens()
    {
        CatalogOf().PreferredAudioInput.ShouldBeNull();
    }

    [Fact]
    public void The_highest_priority_backend_wins()
    {
        CatalogOf(new FakeInput("portable", Priority: 0), new FakeInput("native", Priority: 100))
            .PreferredAudioInput!.Id.ShouldBe("native");
    }

    [Fact]
    public void A_backend_that_cannot_run_here_is_passed_over()
    {
        CatalogOf(new FakeInput("native", Priority: 100, Supported: false), new FakeInput("portable"))
            .PreferredAudioInput!.Id.ShouldBe("portable");
    }

    [Fact]
    public void Equal_priorities_break_on_id_so_the_choice_is_repeatable()
    {
        CatalogOf(new FakeInput("oss"), new FakeInput("alsa")).PreferredAudioInput!.Id.ShouldBe("alsa");
        CatalogOf(new FakeInput("alsa"), new FakeInput("oss")).PreferredAudioInput!.Id.ShouldBe("alsa");
    }

    [Fact]
    public void A_backend_that_throws_when_asked_has_said_no()
    {
        CatalogOf(new ThrowingInput(), new FakeInput("portable")).PreferredAudioInput!.Id.ShouldBe("portable");
    }

    private sealed record FakeInput(string Id, int Priority = 0, bool Supported = true) : IAudioInput
    {
        public string Name => Id;

        public bool IsSupported => Supported;

        public IAudioCapture Create(AudioFormat format, SettingValues settings) => throw new NotSupportedException();
    }

    private sealed class ThrowingInput : IAudioInput
    {
        public string Id => "broken";

        public string Name => "Broken";

        public int Priority => 1000;

        public bool IsSupported => throw new InvalidOperationException("no");

        public IAudioCapture Create(AudioFormat format, SettingValues settings) => throw new InvalidOperationException("no");
    }
}
