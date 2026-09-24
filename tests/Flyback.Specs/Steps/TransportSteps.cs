using Avalonia.Input;
using Reqnroll;
using Shouldly;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

/// <summary>The editor's transport: pausing the patch and moving its clock along the seek bar.</summary>
[Binding]
public sealed class TransportSteps(Editor editor)
{
    /// <summary>How far the clock may run on between the seek and the look at it.</summary>
    private const double Slack = 1;

    [Given("the patch is paused")]
    public void GivenPaused() => editor.PressCtrl(PhysicalKey.P);

    [When("the seek bar is clicked at {int} seconds")]
    public void WhenSought(int seconds) => editor.Seek(seconds);

    [When("the seek bar's length is set to {string}")]
    public void WhenTheLengthIsSet(string typed) => editor.SetSeekLength(typed);

    [Then("the patch's clock is at about {int} seconds")]
    public void ThenTheClockIsAt(int seconds) => editor.Clock.ShouldBeInRange(seconds - Slack, seconds + Slack);

    [Then("the patch is still paused")]
    public void ThenStillPaused() => editor.Paused.ShouldBeTrue();

    [Then("the seek bar reaches {int} seconds")]
    public void ThenTheBarReaches(int seconds) => editor.SeekLength.ShouldBe(seconds);
}
