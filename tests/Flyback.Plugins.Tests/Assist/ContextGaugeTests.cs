using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Assist;

/// <summary>How large a conversation is taken to be, reported or estimated.</summary>
public class ContextGaugeTests
{
    private static readonly PatchWorkbench Bench = new(NodeCatalog.BuiltIn, new Patch(), vision: false);

    [Fact]
    public void With_nothing_reported_the_briefing_and_tools_are_estimated_at_four_characters_a_token()
    {
        var gauge = ContextGauge.Of(Bench);
        var characters = Bench.Briefing.Length + Bench.Tools.Sum(tool => tool.Name.Length + tool.Description.Length + tool.Schema.Length);

        gauge.Tokens.ShouldBe(characters / ContextGauge.CharactersPerToken);
    }

    [Fact]
    public void What_is_said_and_shown_adds_to_the_estimate_and_pictures_go_after_their_turn()
    {
        var gauge = ContextGauge.Of(Bench);
        var start = gauge.Tokens;

        gauge.Add(new string('x', 4_000));
        gauge.AddMedia();

        gauge.Tokens.ShouldBe(start + 1_000 + ContextGauge.MediaTokens);

        gauge.ForgetMedia();

        gauge.Tokens.ShouldBe(start + 1_000);
    }

    [Fact]
    public void A_reported_count_stands_in_place_of_the_estimate_and_nought_reports_nothing()
    {
        var gauge = ContextGauge.Of(Bench);

        gauge.Report(123);
        gauge.Add(new string('x', 400_000));
        gauge.Report(0);

        gauge.Tokens.ShouldBe(123);
    }

    [Fact]
    public void A_saved_conversation_is_estimated_from_what_was_said_in_it()
    {
        ContextGauge.Of(Bench, said: [new string('x', 8_000)]).Tokens.ShouldBe(ContextGauge.Of(Bench).Tokens + 2_000);
    }
}
