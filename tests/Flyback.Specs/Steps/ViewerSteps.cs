using Avalonia.Input;
using Reqnroll;
using Shouldly;
using Flyback.Specs.Support;
using Flyback.Host;

namespace Flyback.Specs.Steps;

/// <summary>flyback-viewer playing the scenario's patch, and what its picture shows.</summary>
[Binding]
public sealed class ViewerSteps(ViewerRun viewer)
{
    [When("the viewer plays it")]
    public void WhenPlayed() => viewer.Play();

    [When("the viewer plays it with a sound input plugged in")]
    public void WhenPlayedWithAnInput() => viewer.PlayWithAnInput();

    [Then("the viewer is listening to the sound input")]
    public void ThenListening() => viewer.Input.ShouldNotBeNull().IsRunning.ShouldBeTrue();

    [When("the viewer plays it with {string}")]
    public void WhenPlayedWith(string flags) => viewer.Play(flags.Split(' '));

    [When("the viewer plays it through {string} with {string}")]
    public void WhenPlayedThrough(string backend, string flags) => viewer.PlayThrough(backend, flags.Split(' '));

    [When("the viewer's picture is tapped")]
    public void WhenTapped() => viewer.TapPicture();

    [When("the viewer's picture is double-clicked")]
    public void WhenDoubleClicked() => viewer.TapPicture(2);

    [Then("the viewer's report says the picture was drawn by {string}")]
    public void ThenReportRenderer(string renderer) => viewer.Report.ShouldContain($"picture: {renderer}");

    [Then("the viewer's report gives the frames a second and the slowest frame")]
    public void ThenReportFrames()
    {
        viewer.Report.ShouldContain(line => line.StartsWith("fps: "));
        viewer.Report.ShouldContain(line => line.StartsWith("slowest-frame-ms: "));
    }

    [Then("the viewer's report says there is no sound")]
    public void ThenReportNoSound() => viewer.Report.ShouldContain("sound: none");

    [Then("the viewer's report says the sound played through {string}")]
    public void ThenReportBackend(string backend) => viewer.Report.ShouldContain($"sound: {backend}");

    [Then("the viewer's picture says the sound plays through {string}")]
    public void ThenItNamesTheBackend(string backend) => viewer.Stats.ShouldNotBeNull("nothing is showing").ShouldContain($"CPU · {backend} · t ");

    [Then("the viewer's picture names no sound backend")]
    public void ThenItNamesNoBackend() => viewer.Stats.ShouldNotBeNull("nothing is showing").ShouldContain("CPU · t ");

    [Then("the viewer is paused")]
    public void ThenPaused() => viewer.Paused.ShouldBe(true);

    [Then("the viewer is playing")]
    public void ThenPlaying() => viewer.Paused.ShouldBe(false);

    [Then("the viewer has the whole screen")]
    public void ThenFullScreen() => viewer.FullScreen.ShouldBeTrue();

    [When("F3 is pressed over the viewer's picture")]
    public void WhenF3() => viewer.Press(PhysicalKey.F3);

    [Then("the viewer's picture says how many frames a second it draws")]
    public void ThenItSays() => viewer.Stats.ShouldNotBeNull("nothing is showing").ShouldContain("fps");

    [Then("the viewer's picture counts no ops")]
    public void ThenItCountsNoOps() => viewer.Stats.ShouldNotBeNull("nothing is showing").ShouldNotContain("ops");

    [Then("the viewer's picture says the sound is worked out at {int}× the output rate")]
    public void ThenItSaysTheOversampling(int factor) => viewer.Stats.ShouldNotBeNull("nothing is showing").ShouldContain($"{factor}× oversampling");

    [Then("the viewer's picture says nothing of oversampling")]
    public void ThenItSaysNothingOfOversampling() => viewer.Stats.ShouldNotBeNull("nothing is showing").ShouldNotContain("oversampl");

    [Then("the viewer's picture says the sound is not oversampled")]
    public void ThenItSaysNoOversampling() => viewer.Stats.ShouldNotBeNull("nothing is showing").ShouldContain("no oversampling");

    [Then("the viewer's transport waits at the top of its picture")]
    public void ThenTheTransportAtTheTop() => viewer.TransportEdge.ShouldBe(Avalonia.Layout.VerticalAlignment.Top);

    [Then("the viewer's transport has no sound button")]
    public void ThenNoSoundButton() => viewer.TransportHasSound.ShouldBe(false);

    [Then("the viewer's transport has a sound button")]
    public void ThenASoundButton() => viewer.TransportHasSound.ShouldBe(true);

    [Then("the viewer's transport waits at the bottom of its picture")]
    public void ThenTheTransportAtTheBottom() => viewer.TransportEdge.ShouldBe(Avalonia.Layout.VerticalAlignment.Bottom);

    [Then("the viewer's picture says nothing about how it is drawn")]
    public void ThenItSaysNothing() => viewer.Stats.ShouldBeNull();
}
