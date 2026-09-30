using Shouldly;
using Xunit;

namespace Flyback.App.Tests;

/// <summary>A page's recompiles, paced on a clock these tests move by hand.</summary>
public class RecompilePacingTests
{
    private TimeSpan now;
    private readonly List<(TimeSpan At, Action Act)> timers = [];
    private int compiles;
    private TimeSpan cost = TimeSpan.FromMilliseconds(10);
    private readonly RecompilePacing pacing;

    public RecompilePacingTests() =>
        pacing = new RecompilePacing(Compile, () => now, (delay, act) => timers.Add((now + delay, act)));

    private void Compile()
    {
        compiles++;
        now += cost;
        pacing.Ran(cost);
    }

    /// <summary>Moves the clock on, firing whatever timer falls due on the way.</summary>
    private void Wait(double milliseconds)
    {
        var until = now + TimeSpan.FromMilliseconds(milliseconds);

        while (timers.Count > 0 && timers.Min(t => t.At) <= until)
        {
            var next = timers.MinBy(t => t.At);
            timers.Remove(next);
            now = next.At;
            next.Act();
        }

        now = until;
    }

    [Fact]
    public void The_first_ask_compiles_at_once()
    {
        pacing.Ask();

        compiles.ShouldBe(1);
    }

    [Fact]
    public void A_drag_compiles_once_a_gap_and_lands_on_its_last_step()
    {
        // Sixty steps a second for a second.
        for (var step = 0; step < 60; step++)
        {
            pacing.Ask();
            Wait(1000 / 60d);
        }

        var during = compiles;
        Wait(500);

        during.ShouldBeInRange(9, 11, "one compile every tenth of a second");
        compiles.ShouldBe(during + 1, "the last step compiles once the gap is over");
    }

    [Fact]
    public void A_slow_compile_widens_the_gap_to_twice_its_cost()
    {
        cost = TimeSpan.FromMilliseconds(150);

        for (var step = 0; step < 60; step++)
        {
            pacing.Ask();
            Wait(1000 / 60d);
        }

        // Each compile costs 150 ms and is followed by 300 ms of gap.
        compiles.ShouldBeInRange(2, 4);
    }

    [Fact]
    public void A_compile_from_elsewhere_answers_an_ask_waiting_on_the_gap()
    {
        pacing.Ask();
        pacing.Ask();

        // Something else, a probe selected say, recompiles in the meantime.
        Compile();
        Wait(500);

        compiles.ShouldBe(2);
    }
}
