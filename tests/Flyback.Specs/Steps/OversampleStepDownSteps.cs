using Flyback.Engine.Render;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The live sound's step down, judged on buffer counts and a clock the steps move by hand.</summary>
[Binding]
public sealed class OversampleStepDownSteps
{
    /// <summary>Buffers a second at the engine's usual 512 frames and 48 kHz.</summary>
    private const int BuffersASecond = 94;

    private readonly OversampleStepDown judge = new();
    private TimeSpan now;
    private long timed;
    private long late;
    private long played;
    private int factor;
    private bool behind;

    [Given("live sound worked out at {int} times the output rate")]
    public void GivenLive(int at)
    {
        factor = at;
        Look();
    }

    [When("a third of its buffers come late for {int} second(s)")]
    [When("a third of its buffers come late for {int} second(s) more")]
    public void WhenAThirdLate(int seconds) => Play(seconds, lateEvery: 3);

    [When("every buffer is on time for {int} second(s)")]
    public void WhenOnTime(int seconds) => Play(seconds, lateEvery: 0);

    [When("{int} of its buffers come late in {int} second(s)")]
    public void WhenAFewLate(int count, int seconds)
    {
        late += count;
        Play(seconds, lateEvery: 0);
    }

    [When("it is made anew")]
    public void WhenMadeAnew() => judge.Renewed(now);

    [Then("it is worked out at {int} times the output rate")]
    public void ThenAt(int at) => factor.ShouldBe(at);

    [Then("it is said to be behind, with no lower rate to go to")]
    public void ThenBehind() => behind.ShouldBeTrue();

    [Then("it is not said to be behind")]
    public void ThenNotBehind() => behind.ShouldBeFalse();

    /// <summary>A buffer a tick, looked at twice a second as the editor looks.</summary>
    private void Play(int seconds, int lateEvery)
    {
        for (var buffer = 1; buffer <= seconds * BuffersASecond; buffer++)
        {
            timed++;
            if (lateEvery > 0 && buffer % lateEvery == 0) late++;

            now = TimeSpan.FromSeconds((double)++played / BuffersASecond);

            if (buffer % (BuffersASecond / 2) == 0) Look();
        }
    }

    private void Look()
    {
        var verdict = judge.Check(now, playing: true, new SoundTiming(timed, late), factor);

        if (verdict.Lower is { } lower) factor = lower;
        behind |= verdict.Behind;
    }
}
