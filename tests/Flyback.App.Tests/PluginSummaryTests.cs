using Flyback.App.Audio;
using Flyback.App.PluginPackages;
using Flyback.Core.Graph;
using Flyback.Plugins;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests;

/// <summary>Which plugin each thing that went wrong is blamed on, for the plugins window to mark.</summary>
public sealed class PluginSummaryTests
{
    private static readonly string Plugins = Path.Combine(Path.GetTempPath(), "flyback-summary", "plugins");

    private static readonly string Sound = PluginSummary.Folder(Path.Combine(Plugins, "WinIO"));

    private static readonly PluginInfo WinIO = new("win.io", "Windows sound");

    private static readonly FakeOutput Wasapi = new();

    private static PluginCatalog Catalog(params PluginProblem[] problems) => new(
        [new LoadedPlugin(WinIO, Path.Combine(Sound, "Flyback.Plugins.WinIO.dll"))],
        [Wasapi],
        NodeCatalog.BuiltIn,
        [],
        problems,
        providers: new Dictionary<object, PluginInfo> { [Wasapi] = WinIO });

    [Fact]
    public void A_problem_is_blamed_on_the_folder_it_came_from()
    {
        var tape = Path.Combine(Plugins, "Tape");
        var (run, troubles) = PluginSummary.Run(
            Catalog(new PluginProblem("Tape.dll", "needs rebuilding.") { Folder = tape }), Plugins, new AudioSetup(new SilentAudioDevice(), Wasapi, null));

        troubles[PluginSummary.Folder(tape)].ShouldBe("Tape.dll: needs rebuilding.");
        run.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void Sound_that_would_not_open_is_blamed_on_the_plugin_it_came_from()
    {
        var (run, troubles) = PluginSummary.Run(Catalog(), Plugins, new AudioSetup(new SilentAudioDevice(), Wasapi, "WASAPI — busy"));

        troubles[Sound].ShouldBe("Could not open sound: WASAPI — busy");
        run.Problems.ShouldBeEmpty();
    }

    /// <summary>Nothing to mark means it is listed on its own instead.</summary>
    [Fact]
    public void What_no_plugin_can_be_blamed_for_is_listed_on_its_own()
    {
        var (run, troubles) = PluginSummary.Run(
            Catalog(new PluginProblem("Loose", "went wrong.")), Plugins, new AudioSetup(new SilentAudioDevice(), null, "nothing can play"));

        run.Problems.ShouldBe(["Could not open sound: nothing can play", "Loose: went wrong."]);
        troubles.ShouldBeEmpty();
    }

    private sealed class FakeOutput : IAudioOutput
    {
        public string Id => "wasapi";

        public string Name => "WASAPI";

        public int Priority => 0;

        public bool IsSupported => true;

        public IAudioDevice Create(AudioFormat format, SettingValues settings) => new SilentAudioDevice(format.SampleRate);
    }
}
