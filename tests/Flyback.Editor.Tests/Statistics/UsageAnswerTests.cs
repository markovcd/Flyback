using Flyback.Editor.Statistics;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Statistics;

/// <summary>A run that waits on the first-start question: nothing leaves until a yes, and a no forgets it all.</summary>
public sealed class UsageAnswerTests
{
    private readonly CollectedEvents sink = new();

    private Usage Held() => new(sink, new Launch(First: true), hold: true);

    [Fact]
    public void Nothing_is_sent_before_the_answer()
    {
        var run = Held();

        run.Started([], "wasapi");
        run.Played(["flyback.oscillator"]);

        run.Asks.ShouldBeTrue();
        sink.Events.ShouldBeEmpty();
    }

    [Fact]
    public void A_yes_sends_what_was_kept_first_start_and_all()
    {
        var run = Held();

        run.Started([], "wasapi");
        run.Answer(true);
        run.Played(["flyback.oscillator"]);

        sink.Events.Select(e => e.Name).ShouldBe(["started", "played"]);
        sink.Events[0].Props["first"].ShouldBe(true);
        run.Asks.ShouldBeFalse();
    }

    [Fact]
    public void A_no_forgets_what_was_kept_and_says_nothing_after()
    {
        var run = Held();

        run.Started([], "wasapi");
        run.Answer(false);
        run.Played(["flyback.oscillator"]);
        run.Ended();

        sink.Events.ShouldBeEmpty();
        run.Asks.ShouldBeFalse();
    }

    [Fact]
    public void A_run_never_answered_sends_nothing_as_it_ends()
    {
        var run = Held();

        run.Started([], "wasapi");
        run.Ended();
        run.Drain(TimeSpan.Zero);

        sink.Events.ShouldBeEmpty();
    }

    [Fact]
    public void A_run_that_was_not_held_never_asks()
    {
        new Usage(sink).Asks.ShouldBeFalse();
        Usage.Off.Asks.ShouldBeFalse();
    }
}
