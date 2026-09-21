using System.Globalization;
using Reqnroll;
using Shouldly;
using Flyback.Core.Compile;
using Flyback.Core.Specs.Support;

namespace Flyback.Core.Specs.Steps;

/// <summary>
/// Phrases a patch author would use, each building or checking the patch behind
/// it. The wiring, knob values and tolerances live here so the scenarios can
/// read as requirements.
/// </summary>
[Binding]
public sealed class RequirementSteps(PatchContext context)
{
    private const float Grey = 1.5f / 255f;

    private const double Sample = 1e-3;

    /// <summary>Samples a loop needs before halving-plus-a-quarter sits on a half to within a sample tolerance.</summary>
    private const int Settle = 30;

    /// <summary>The highest frequency heard so far, which bounds how far a smooth wave can move in one sample.</summary>
    private double highest;

    // --- a loop ---------------------------------------------------------------

    [Given("a loop that halves what it made last and adds a quarter")]
    public void GivenALoop()
    {
        context.Add("half", "math.mul");
        context.Add("nudge", "math.add");
        context.Add("screen", "output");
        context.SetInput("half", "b", 0.5f);
        context.SetInput("nudge", "b", 0.25f);
        context.Wire("half", "out", "nudge", "a");
        context.Wire("nudge", "out", "half", "a");
    }

    [Given("the loop is heard at the speakers")]
    public void GivenTheLoopIsHeard()
    {
        context.Wire("nudge", "out", "screen", "left");
        context.SetInput("screen", "volume", 1f);
        context.CompileFor("audio");
    }

    [Given("the loop is shown on the screen")]
    public void GivenTheLoopIsShown()
    {
        context.Wire("nudge", "out", "screen", "color");
        context.Compile();
    }

    [Then("the patch is accepted without complaint")]
    public void ThenAccepted() =>
        context.Result.Issues.ShouldBeEmpty(string.Join(" | ", context.Result.Issues.Select(i => i.Message)));

    [Then(@"^each sample builds on the last: (.+)$")]
    public void ThenEachSampleBuilds(string list)
    {
        var expected = Numbers(list);

        for (var i = 0; i < expected.Length; i++)
            context.SampleAt(i).ShouldBe(expected[i], Sample, $"sample {i}");
    }

    [Then(@"^each frame builds on the last: (.+)$")]
    public void ThenEachFrameBuilds(string list)
    {
        var expected = Numbers(list);

        for (var i = 0; i < expected.Length; i++)
            ShouldBeGrey(context.RenderCentre(i + 1), (float)expected[i], $"frame {i + 1}");
    }

    [Then("after {int} frames and a rewind the next frame is back at {float}")]
    public void ThenRewound(int frames, float expected) =>
        ShouldBeGrey(context.RenderCentreAfterReset(frames, 1), expected, "after rewind");

    [When("the loop has played until it settles")]
    public void WhenTheLoopSettles() => context.Play(Settle);

    [When("what it adds is turned to {float}")]
    public void WhenWhatItAddsIsTurned(float value) => context.Turn("nudge", "b", value);

    [When("it plays one more sample")]
    public void WhenOneMoreSample() => context.Play(1);

    [Then("that sample is about {float}")]
    public void ThenThatSample(float expected) => context.Heard[^1].ShouldBe(expected, Sample);

    // --- a tone ---------------------------------------------------------------

    [Given("a {float} Hz sine is playing")]
    public void GivenASine(float frequency)
    {
        context.Add("tone", "osc.sine");
        context.SetInput("tone", "freq", frequency);
        Hear("tone");
        highest = frequency;
    }

    /// <summary>A Threshold on Time picks between the two pitches, so the frequency moves with no recompile.</summary>
    [Given("a sine whose frequency jumps from {float} Hz to {float} Hz at {float} seconds")]
    public void GivenAJumpingSine(float from, float to, float seconds)
    {
        context.Add("clock", "time");
        context.Add("switch", "math.step");
        context.Add("pitch", "math.remap");
        context.Add("tone", "osc.sine");
        context.SetInput("switch", "edge", seconds);
        context.Wire("clock", "t", "switch", "in");
        context.Wire("switch", "out", "pitch", "in");
        context.SetInput("pitch", "in low", 0f);
        context.SetInput("pitch", "in high", 1f);
        context.SetInput("pitch", "out low", from);
        context.SetInput("pitch", "out high", to);
        context.Wire("pitch", "out", "tone", "freq");
        Hear("tone");
        highest = Math.Max(from, to);
    }

    [When("it plays for {float} seconds")]
    [When("it has played {float} seconds")]
    [When("it plays on for {float} seconds")]
    public void WhenItPlays(float seconds) => context.Play((int)Math.Round(seconds * PatchContext.SampleRate));

    [When("its frequency is turned to {float} Hz")]
    public void WhenTheFrequencyIsTurned(float frequency)
    {
        context.Turn("tone", "freq", frequency);
        highest = Math.Max(highest, frequency);
    }

    [Then("the sound is about {float} at {float} seconds")]
    public void ThenTheSoundAt(float expected, float seconds) =>
        context.SampleAt((int)Math.Round(seconds * PatchContext.SampleRate))
            .ShouldBe(expected, Sample, $"at {seconds} s");

    /// <summary>
    /// A click is a step between neighboring samples steeper than the wave itself
    /// can go: a unit sine at f Hz moves at most 2πf / rate in one sample.
    /// </summary>
    [Then("the sound never clicks")]
    public void ThenNoClick()
    {
        var heard = context.Heard;
        var steepest = 2 * Math.PI * highest / PatchContext.SampleRate * 1.05;

        heard.Count.ShouldBeGreaterThan(1, "nothing has been played");

        for (var i = 1; i < heard.Count; i++)
            Math.Abs(heard[i] - heard[i - 1])
                .ShouldBeLessThanOrEqualTo(steepest, $"between samples {i - 1} and {i}");
    }

    // --- switching off --------------------------------------------------------

    [Given("a level of {float} shown through a module that halves it")]
    public void GivenALevelHalved(float level)
    {
        Level(level);
        Halver("halve", "level");
        Show("halve");
    }

    [Given("a level of {float} shown through two modules that each halve it")]
    public void GivenALevelHalvedTwice(float level)
    {
        Level(level);
        Halver("halve", "level");
        Halver("halve again", "halve");
        Show("halve again");
    }

    [Given("the horizontal position shown through a module that halves it")]
    public void GivenThePositionHalved()
    {
        context.Add("coords", "coord");
        context.Add("halve", "math.mul");
        context.SetInput("halve", "b", 0.5f);
        context.Wire("coords", "x", "halve", "a");
        Show("halve");
    }

    [Given("a switched-off module with nothing patched in, feeding an adder set to {float} plus {float}")]
    public void GivenAnEmptyModuleFeedingAnAdder(float a, float b)
    {
        context.Add("halve", "math.mul");
        context.Add("sum", "math.add");
        context.SetInput("sum", "a", a);
        context.SetInput("sum", "b", b);
        context.Wire("halve", "out", "sum", "a");
        context.Node("halve").Off = true;
        Show("sum");
    }

    [When("the halving module is switched off")]
    public void WhenTheHalverIsOff() => context.Node("halve").Off = true;

    [When("both halving modules are switched off")]
    public void WhenBothHalversAreOff()
    {
        context.Node("halve").Off = true;
        context.Node("halve again").Off = true;
    }

    [Then("the screen shows {float}")]
    public void ThenTheScreenShows(float expected)
    {
        context.Compile();
        ThenAccepted();
        ShouldBeGrey(context.RenderCentre(1), expected, "centre");
    }

    [Then("the halving costs nothing")]
    public void ThenTheHalvingCostsNothing()
    {
        context.Compile();
        context.CountOps(OpCode.Mul).ShouldBe(0);
    }

    // --- building blocks ------------------------------------------------------

    private void Hear(string source)
    {
        context.Add("screen", "output");
        context.Wire(source, "out", "screen", "left");
        context.SetInput("screen", "volume", 1f);
        context.CompileFor("audio");
    }

    private void Level(float level)
    {
        context.Add("level", "value");
        context.SetInput("level", "value", level);
    }

    private void Halver(string name, string from)
    {
        context.Add(name, "math.mul");
        context.SetInput(name, "b", 0.5f);
        context.Wire(from, "out", name, "a");
    }

    private void Show(string source)
    {
        context.Add("screen", "output");
        context.Wire(source, "out", "screen", "color");
    }

    private static void ShouldBeGrey((float R, float G, float B) pixel, float expected, string where)
    {
        pixel.R.ShouldBe(expected, Grey, $"red, {where}");
        pixel.G.ShouldBe(expected, Grey, $"green, {where}");
        pixel.B.ShouldBe(expected, Grey, $"blue, {where}");
    }

    private static double[] Numbers(string list) =>
        [.. list.Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture))];
}
