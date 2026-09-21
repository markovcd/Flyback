using Reqnroll;
using Shouldly;
using Flyback.Core.Compile;
using Flyback.Core.Specs.Support;

namespace Flyback.Core.Specs.Steps;

/// <summary>What Flyback says about a patch, and what running it costs.</summary>
[Binding]
public sealed class CompilerSteps(PatchContext context)
{
    /// <summary>Nothing said about it for either the screen or the speakers.</summary>
    [Then("the patch is accepted without complaint")]
    public void ThenAccepted()
    {
        Silent(context.Picture, "the screen");
        Silent(context.Sound, "the speakers");
    }

    [Then("Flyback says the patch has no Output")]
    public void ThenNoOutput() => ShouldMention(context.Picture, "no Output");

    [Then("Flyback reports an unknown module")]
    public void ThenUnknownModule() => ShouldMention(context.Picture, "Unknown module");

    /// <summary>A remark, not an error: the patch compiles to exactly what it says.</summary>
    [Then("Flyback points out that nothing reaches the Output")]
    public void ThenNothingReachesTheOutput()
    {
        foreach (var compiled in new[] { context.Picture, context.Sound })
        {
            ShouldMention(compiled, "Nothing is wired into the Output");
            compiled.HasErrors.ShouldBeFalse(Said(compiled));
        }
    }

    /// <summary>A remark, not an error: the socket it feeds rests on its knob.</summary>
    [Then("Flyback points out that nothing is sent on {string}")]
    public void ThenNothingIsSentOn(string bus)
    {
        ShouldMention(context.Sound, $"No Send is on '{bus}'");
        context.Sound.HasErrors.ShouldBeFalse(Said(context.Sound));
    }

    [Then("drawing the picture does not compute the tone")]
    public void ThenThePictureSkipsTheTone()
    {
        Count(context.Picture, OpCode.Noise3).ShouldBeGreaterThan(0, "the picture itself is missing");
        Count(context.Picture, OpCode.Sin).ShouldBe(0);
    }

    [Then("playing the sound does not compute the picture")]
    public void ThenTheSoundSkipsThePicture()
    {
        Count(context.Sound, OpCode.Sin).ShouldBeGreaterThan(0, "the tone itself is missing");
        Count(context.Sound, OpCode.Noise3).ShouldBe(0);
    }

    [Then("the patch reads the clock")]
    public void ThenReadsTheClock() => Count(context.Picture, OpCode.LoadT).ShouldBeGreaterThan(0);

    [Then("the patch does not read the clock")]
    public void ThenDoesNotReadTheClock() => Count(context.Picture, OpCode.LoadT).ShouldBe(0);

    [Then("the patch reads the clock once")]
    public void ThenReadsTheClockOnce() => Count(context.Picture, OpCode.LoadT).ShouldBe(1);

    [Then("the unwired noise costs nothing")]
    public void ThenTheNoiseIsFree() => Count(context.Picture, OpCode.Noise3).ShouldBe(0);

    [Then("the position is worked out once")]
    public void ThenThePositionIsWorkedOutOnce() => Count(context.Picture, OpCode.LoadX).ShouldBe(1);

    [Then("the halving costs nothing")]
    public void ThenTheHalvingIsFree() => Count(context.Picture, OpCode.Mul).ShouldBe(0);

    private static int Count(CompileResult compiled, OpCode code) => compiled.Program.Ops.Count(op => op.Code == code);

    private static void Silent(CompileResult compiled, string sink) =>
        compiled.Issues.ShouldBeEmpty($"for {sink}: {Said(compiled)}");

    private static void ShouldMention(CompileResult compiled, string fragment) =>
        compiled.Issues.Any(i => i.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            .ShouldBeTrue($"issues were: {Said(compiled)}");

    private static string Said(CompileResult compiled) => string.Join(" | ", compiled.Issues.Select(i => i.Message));
}
