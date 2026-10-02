using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The Output's Volume, turned from the toolbar.</summary>
[Binding]
public sealed class VolumeSteps(PatchContext context, EditorDriver editor)
{
    [Given("a level of {float} is wired into the Output's Volume")]
    public void GivenAWiredVolume(float level)
    {
        context.Add("loudness", "value");
        context.SetInput("loudness", "value", level);
        context.Wire("loudness", "out", "screen", "volume");
    }

    [When("the toolbar's Volume is clicked all the way down")]
    public void WhenAllTheWayDown() => editor.ClickVolume(0);

    [When("the toolbar's Volume is clicked all the way up")]
    public void WhenAllTheWayUp() => editor.ClickVolume(1);

    [Then("the Output's Volume is {float}")]
    public void ThenTheVolumeIs(float expected) => context.StoredInput("screen", "volume").ShouldBe(expected, 0.001f);

    [Then("the toolbar's Volume cannot be turned")]
    public void ThenItCannotBeTurned() => editor.CanTurnVolume.ShouldBeFalse();
}
