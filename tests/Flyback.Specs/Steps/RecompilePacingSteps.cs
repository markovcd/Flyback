using Flyback.App;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>A page's recompile pacing, on a clock the steps move by hand.</summary>
[Binding]
public sealed class RecompilePacingSteps
{
    private TimeSpan now;
    private TimeSpan cost;
    private readonly List<(TimeSpan At, Action Act)> timers = [];
    private RecompilePacing? pacing;
    private int compiles;
    private int during;

    [Given("the web editor compiles a patch in {int} milliseconds")]
    public void GivenACost(int milliseconds)
    {
        cost = TimeSpan.FromMilliseconds(milliseconds);
        pacing = new RecompilePacing(Compile, () => now, (delay, act) => timers.Add((now + delay, act)));
    }

    [When("a knob is dragged for {int} second(s), sixty steps a second")]
    public void WhenDragged(int seconds)
    {
        for (var step = 0; step < seconds * 60; step++)
        {
            pacing!.Ask();
            Wait(1000 / 60d);
        }

        during = compiles;
        Wait(1000);
    }

    [Then("the patch compiled about ten times during the drag")]
    public void ThenAboutTen() => during.ShouldBeInRange(9, 11);

    [Then("once more after it, at the step it stopped on")]
    public void ThenOnceMore() => compiles.ShouldBe(during + 1);

    private void Compile()
    {
        compiles++;
        now += cost;
        pacing!.Ran(cost);
    }

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
}
