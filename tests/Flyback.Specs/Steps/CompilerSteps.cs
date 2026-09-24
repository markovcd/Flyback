using Reqnroll;
using Shouldly;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

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

    [Then("every Send is heard")]
    public void ThenEverySendIsHeard() =>
        context.Sound.Issues.ShouldNotContain(i => i.Message.Contains("Another Send", StringComparison.Ordinal), Said(context.Sound));

    private static void Silent(CompileResult compiled, string sink) =>
        compiled.Issues.ShouldBeEmpty($"for {sink}: {Said(compiled)}");

    /// <summary>A remark, not an error: the wire carries what it carries.</summary>
    [Then("Flyback points out that the sine swings past what the brightness takes")]
    public void ThenTheSineSwingsPast()
    {
        ShouldMention(context.Picture, "swings -1 to 1, past the 0 to 1 HSV's 'value' takes");
        context.Picture.HasErrors.ShouldBeFalse(Said(context.Picture));
    }

    [Then("Flyback offers to fit the ranges on the wire")]
    public void ThenOffered() => AutoRemap.Offered(context.Patch, context.Patch.Connections.Single()).ShouldBeTrue();

    [Then("Flyback offers nothing on the wire")]
    public void ThenNotOffered() => AutoRemap.Offered(context.Patch, context.Patch.Connections.Single()).ShouldBeFalse();

    private static void ShouldMention(CompileResult compiled, string fragment) =>
        compiled.Issues.Any(i => i.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            .ShouldBeTrue($"issues were: {Said(compiled)}");

    private static string Said(CompileResult compiled) => string.Join(" | ", compiled.Issues.Select(i => i.Message));
}
