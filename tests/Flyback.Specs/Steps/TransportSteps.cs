using Avalonia.Input;
using Reqnroll;
using Shouldly;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

/// <summary>The editor's transport and its picture: pausing, moving the clock along the seek bar, and the picture full screen.</summary>
[Binding]
public sealed class TransportSteps(Editor editor)
{
    /// <summary>How far the clock may run on between the seek and the look at it.</summary>
    private const double Slack = 1;

    [Given("the patch is paused")]
    public void GivenPaused() => editor.PressCtrl(PhysicalKey.P);

    [When("the seek bar is clicked at {int} seconds")]
    public void WhenSought(int seconds) => editor.Seek(seconds);

    [Given("the seek bar loops")]
    public void GivenLooping() => editor.LoopSeekBar();

    /// <summary>Seeks to the very end, then gives the bar the tick it comes round on, capped since the tick is a tenth of a second.</summary>
    [When("the patch plays on past the end of the seek bar")]
    public void WhenPastTheEnd()
    {
        editor.Seek(editor.SeekLength);
        editor.WaitForClock(seconds => seconds < Slack, TimeSpan.FromSeconds(2));
    }

    [When("the seek bar's length is set to {string}")]
    public void WhenTheLengthIsSet(string typed) => editor.SetSeekLength(typed);

    [Then("the patch's clock is at about {int} seconds")]
    public void ThenTheClockIsAt(int seconds) => editor.Clock.ShouldBeInRange(seconds - Slack, seconds + Slack);

    [Then("the patch is still paused")]
    public void ThenStillPaused() => editor.Paused.ShouldBeTrue();

    [When("the picture is given the whole window")]
    public void WhenFullScreen() => editor.FullScreen();

    [When("F3 is pressed")]
    public void WhenF3() => editor.Press(PhysicalKey.F3);

    [Then("the editor's picture says how many frames a second it draws")]
    public void ThenTheEditorSays() => editor.Stats.ShouldNotBeNull("nothing is showing").ShouldContain("fps");

    [Then("the seek bar reaches {int} seconds")]
    public void ThenTheBarReaches(int seconds) => editor.SeekLength.ShouldBe(seconds);
}
