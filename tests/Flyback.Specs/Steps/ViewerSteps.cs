using Avalonia.Input;
using Reqnroll;
using Shouldly;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

/// <summary>flyback-viewer playing the scenario's patch, and what its picture shows.</summary>
[Binding]
public sealed class ViewerSteps(ViewerRun viewer)
{
    [When("the viewer plays it")]
    public void WhenPlayed() => viewer.Play();

    [When("the viewer plays it with {string}")]
    public void WhenPlayedWith(string flag) => viewer.Play(flag);

    [When("F3 is pressed over the viewer's picture")]
    public void WhenF3() => viewer.Press(PhysicalKey.F3);

    [Then("the viewer's picture says how many frames a second it draws")]
    public void ThenItSays() => viewer.Stats.ShouldNotBeNull("nothing is showing").ShouldContain("fps");

    [Then("the viewer's picture says nothing about how it is drawn")]
    public void ThenItSaysNothing() => viewer.Stats.ShouldBeNull();
}
