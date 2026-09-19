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

        public void Drain(TimeSpan most) { }
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

    [Fact]
    public void A_run_says_how_it_was_launched_and_how_big_the_machine_is_in_bands()
    {
        new Usage(sink, new Launch(First: true, Updated: false, File: true))
            .Started([], "wasapi", screens: [1080, 1600]);

        Only.Props["first"].ShouldBe(true);
        Only.Props["updated"].ShouldBe(false);
        Only.Props["file"].ShouldBe(true);
        Only.Props["screens"].ShouldBe("2");
        Only.Props["screen"].ShouldBe("1440-2159");
        Only.Props["cores"].ShouldBeOfType<string>();
        Only.Props["memory"].ShouldBeOfType<string>();
    }

    [Theory]
    [InlineData(0, "<1")]
    [InlineData(1, "1-4")]
    [InlineData(7, "5-14")]
    [InlineData(480, "480+")]
    [InlineData(9000, "480+")]
    public void A_length_is_sent_as_its_band(int minutes, string band) =>
        Usage.Band(minutes, [1, 5, 15, 30, 60, 120, 240, 480]).ShouldBe(band);

    [Fact]
    public void A_band_one_wide_is_a_single_number() =>
        Usage.Band(1, [0, 1, 2, 5]).ShouldBe("1");

    [Fact]
    public void A_play_says_its_wires_and_the_preset_it_came_from()
    {
        var shipped = Presets.All[0].Name;

        Run.Played(["flyback.oscillator"], wires: 4, preset: shipped);

        Only.Props["wires"].ShouldBe(4);
        Only.Props["preset"].ShouldBe(shipped);
    }

    [Fact]
    public void A_patch_from_a_file_names_no_preset()
    {
        Run.Played(["flyback.oscillator"]);

        Only.Props["preset"].ShouldBe("none");
    }

    [Fact]
    public void A_plugins_preset_is_not_named_beside_a_plugin_nobody_shipped()
    {
        var run = Run;

        run.Started(["acme.modular"], "wasapi");
        run.Played(["flyback.oscillator"], preset: "Acme's Own");

        sink.Events[1].Props["preset"].ShouldBe(Usage.Other);
    }

    [Fact]
    public void A_plugins_preset_is_named_while_every_plugin_is_one_Flyback_ships()
    {
        var run = Run;

        run.Started(["flyback.effects"], "wasapi");
        run.Played(["flyback.oscillator"], preset: "Acid");

        sink.Events[1].Props["preset"].ShouldBe("Acid");
    }

    [Fact]
    public void The_end_of_a_run_says_what_it_did_in_bands()
    {
        var run = new Usage(sink, running: () => TimeSpan.FromMinutes(42));

        for (var modules = 1; modules <= 7; modules++) run.Played(Enumerable.Repeat("flyback.oscillator", modules));
        run.Count(Used.Recorded);
        run.Count(Used.Saved);
        run.Count(Used.Saved);
        run.Drew(59.7, gpu: true);
        run.Drew(60.1, gpu: true);
        run.Drew(12, gpu: false);

        run.Ended();

        var ended = sink.Events.Last();

        ended.Name.ShouldBe("ended");
        ended.Props["minutes"].ShouldBe("30-59");
        ended.Props["plays"].ShouldBe("5-9", "every play is counted, not only the ones reported");
        ended.Props["recorded"].ShouldBe("1");
        ended.Props["saved"].ShouldBe("2-4");
        ended.Props["fullScreen"].ShouldBe("0");
        ended.Props["renderer"].ShouldBe("gpu");
        ended.Props["fps"].ShouldBe("55-89");
    }

    [Fact]
    public void A_run_that_never_drew_says_nothing_about_drawing()
    {
        var run = Run;

        run.Ended();
        run.Ended();

        Only.Props.ShouldNotContainKey("fps");
        Only.Props.ShouldNotContainKey("renderer");
    }

    [Fact]
    public void Every_message_to_an_assistant_is_counted_though_it_is_named_once()
    {
        var run = Run;

        run.Assistant("openai");
        run.Assistant("openai");
        run.Ended();

        sink.Events.Count(e => e.Name == "assistant").ShouldBe(1);
        sink.Events.Last().Props["asked"].ShouldBe("2-4");
    }

    [Fact]
    public void A_crash_says_its_kind_and_where_and_never_its_message()
    {
        Exception thrown;

        try
        {
            Usage.Band(1, []);
            throw new InvalidOperationException("unreachable");
        }
        catch (Exception ex)
        {
            thrown = new InvalidOperationException(@"C:\Users\somebody\secret.fbk", ex);
        }

        var run = Run;

        run.Crashed(thrown);
        run.Crashed(thrown);

        Only.Name.ShouldBe("crashed");
        Only.Props["type"].ShouldBe(typeof(IndexOutOfRangeException).FullName);
        Only.Props["at"].ShouldBe("Flyback.App.Statistics.Usage.Band");
        Only.Props.Values.ShouldAllBe(value => !value.ToString()!.Contains("somebody"));
    }

    [Fact]
    public void A_crash_that_never_reached_Flyback_is_placed_nowhere() =>
        Usage.Place(new InvalidOperationException()).ShouldBe(Usage.Other);

    private sealed class AcmeException : Exception;

    [Fact]
    public void An_exception_type_nobody_shipped_is_not_named() =>
        Usage.KnownType(typeof(AcmeException)).ShouldBe(Usage.Other);

    [Fact]
    public void Switched_off_it_says_nothing_at_the_end_or_at_a_crash()
    {
        var run = Run;

        run.Stop();
        run.Ended();
        run.Crashed(new InvalidOperationException());

        sink.Events.ShouldBeEmpty();
    }
}
