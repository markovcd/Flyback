using System.Runtime.InteropServices;
using Flyback.App.Statistics;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Statistics;

/// <summary>
/// What a run says about itself, and — the point of the exercise — what it does not
/// (ADR-0094). Nothing here reaches a network: the sink collects.
/// </summary>
public sealed class UsageTests
{
    private sealed class Collected : IUsageSink
    {
        public List<UsageEvent> Events { get; } = [];

        public void Send(UsageEvent happened) => Events.Add(happened);
    }

    private readonly Collected sink = new();

    private Usage Run => new(sink);

    private UsageEvent Only => sink.Events.ShouldHaveSingleItem();

    [Fact]
    public void A_run_says_what_it_started_as()
    {
        Run.Started(["flyback.effects", "flyback.voice"], "wasapi");

        Only.Name.ShouldBe("started");
        Only.Props["platform"].ShouldBe(RuntimeInformation.RuntimeIdentifier);
        Only.Props["sound"].ShouldBe("wasapi");
        Only.Props["flyback.effects"].ShouldBe(true);
        Only.Props["flyback.voice"].ShouldBe(true);
    }

    [Fact]
    public void A_plugin_nobody_shipped_is_not_named()
    {
        Run.Started(["flyback.effects", "acme.modular"], "acme.card");

        Only.Props.ShouldNotContainKey("acme.modular");
        Only.Props[Usage.Other].ShouldBe(true);
        Only.Props["sound"].ShouldBe(Usage.Other);
    }

    [Fact]
    public void A_run_with_no_sound_says_so()
    {
        Run.Started([], sound: null);

        Only.Props["sound"].ShouldBe("none");
    }

    [Fact]
    public void Starting_is_said_once()
    {
        var run = Run;

        run.Started([], "wasapi");
        run.Started([], "wasapi");

        sink.Events.Count.ShouldBe(1);
    }

    [Fact]
    public void A_play_is_counted_by_kind_of_module()
    {
        Run.Played(["flyback.oscillator", "flyback.oscillator", "flyback.picture.blur"]);

        Only.Name.ShouldBe("played");
        Only.Props["flyback.oscillator"].ShouldBe(2);
        Only.Props["flyback.picture.blur"].ShouldBe(1);
        Only.Props["modules"].ShouldBe(3);
    }

    [Fact]
    public void A_module_from_a_plugin_nobody_shipped_is_counted_as_another()
    {
        Run.Played(["acme.modular.vco", "acme.modular.vcf", "flyback.oscillator"]);

        Only.Props[Usage.Other].ShouldBe(2);
        Only.Props.ShouldNotContainKey("acme.modular.vco");
    }

    /// <summary>The engine's own modules are counted by name.</summary>
    /// <remarks>
    /// A plugin's module is named when its id starts with a shipped plugin's.
    /// The engine's own ids carry no such prefix — "osc.sine", "output" — so
    /// they are asked of its catalogue instead. The ids fed above are invented
    /// ones that do carry a prefix, so this feeds a preset's.
    /// </remarks>
    [Fact]
    public void A_played_patch_names_the_engines_own_modules()
    {
        var patch = Presets.All
            .Select(preset => preset.Build(NodeCatalog.BuiltIn))
            .First(built => built.Nodes.Count > 2);

        Run.Played(patch.Nodes.Select(node => node.TypeId));

        Only.Props.ShouldNotContainKey(Usage.Other, "every module in a preset the engine ships is one it ships");
    }

    [Fact]
    public void The_same_patch_played_again_is_said_once()
    {
        var run = Run;

        run.Played(["flyback.oscillator"]);
        run.Played(["flyback.oscillator"]);

        sink.Events.Count.ShouldBe(1);
    }

    [Fact]
    public void A_run_reports_at_most_three_plays()
    {
        var run = Run;

        for (var modules = 1; modules <= Usage.MostPlays + 2; modules++)
            run.Played(Enumerable.Repeat("flyback.oscillator", modules));

        sink.Events.Count.ShouldBe(Usage.MostPlays);
    }

    [Fact]
    public void The_assistant_is_named_once_and_never_what_was_asked()
    {
        var run = Run;

        run.Assistant("openai");
        run.Assistant("openai");

        Only.Name.ShouldBe("assistant");
        Only.Props.ShouldHaveSingleItem().Value.ShouldBe("openai");
    }

    [Fact]
    public void An_assistant_nobody_shipped_is_not_named()
    {
        Run.Assistant("acme.oracle");

        Only.Props["id"].ShouldBe(Usage.Other);
    }

    [Fact]
    public void Switched_off_it_says_nothing_more()
    {
        var run = Run;

        run.Started([], "wasapi");
        run.Stop();

        run.Played(["flyback.oscillator"]);
        run.Assistant("openai");

        sink.Events.Count.ShouldBe(1, "what was already said was already said");
    }

    [Fact]
    public void A_run_with_nowhere_to_send_anything_says_nothing()
    {
        Should.NotThrow(() =>
        {
            Usage.Off.Started(["flyback.effects"], "wasapi");
            Usage.Off.Played(["flyback.oscillator"]);
            Usage.Off.Assistant("openai");
        });
    }
}
