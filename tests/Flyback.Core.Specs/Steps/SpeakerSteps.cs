using System.Globalization;
using Reqnroll;
using Shouldly;
using Flyback.Core.Specs.Support;

namespace Flyback.Core.Specs.Steps;

/// <summary>What the speakers play, heard sample by sample across any edits.</summary>
[Binding]
public sealed class SpeakerSteps(PatchContext context)
{
    private const double Tolerance = 1e-3;

    [When("it plays for {float} seconds")]
    [When("it has played {float} seconds")]
    [When("it plays on for {float} seconds")]
    public void WhenItPlays(float seconds) => context.Play(Samples(seconds));

    [When("it plays one more sample")]
    public void WhenOneMoreSample() => context.Play(1);

    /// <summary>Exactly zero, not nearly zero: see AudioRendererTests.</summary>
    [Then("the speakers are silent")]
    public void ThenSilent() => context.RenderAudio().ShouldAllBe(v => v == 0f);

    [Then("both speakers play the same sound")]
    public void ThenBothSpeakersMatch()
    {
        var buffer = context.RenderAudio();

        buffer.Any(v => Math.Abs(v) > 0.01f).ShouldBeTrue("the speakers are silent");

        for (var frame = 0; frame < buffer.Length / 2; frame++)
            buffer[frame * 2 + 1].ShouldBe(buffer[frame * 2], $"frame {frame}");
    }

    [Then("the sound is about {float} at {float} seconds")]
    public void ThenTheSoundAt(float expected, float seconds) =>
        context.SampleAt(Samples(seconds)).ShouldBe(expected, Tolerance, $"at {seconds} s");

    [Then(@"^each sample builds on the last: (.+)$")]
    public void ThenEachSampleBuilds(string list)
    {
        var expected = list.Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();

        for (var i = 0; i < expected.Length; i++)
            context.SampleAt(i).ShouldBe(expected[i], Tolerance, $"sample {i}");
    }

    [Then("that sample is about {float}")]
    public void ThenThatSample(float expected) => context.Heard[^1].ShouldBe(expected, Tolerance);

    /// <summary>
    /// A click is a step between neighboring samples steeper than the wave itself
    /// can go: a unit sine at f Hz moves at most 2πf / rate in one sample.
    /// </summary>
    [Then("the sound never clicks")]
    public void ThenNoClick()
    {
        var heard = context.Heard;
        var steepest = 2 * Math.PI * context.HighestFrequency / PatchContext.SampleRate * 1.05;

        heard.Count.ShouldBeGreaterThan(1, "nothing has been played");

        for (var i = 1; i < heard.Count; i++)
            Math.Abs(heard[i] - heard[i - 1])
                .ShouldBeLessThanOrEqualTo(steepest, $"between samples {i - 1} and {i}");
    }

    private static int Samples(float seconds) => (int)Math.Round(seconds * PatchContext.SampleRate);
}
