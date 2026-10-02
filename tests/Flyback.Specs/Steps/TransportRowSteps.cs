using Avalonia;
using Flyback.Editor.Bars;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The row along the foot of the window that plays the patch.</summary>
[Binding]
public sealed class TransportRowSteps(EditorDriver editor)
{
    private double canvasBefore;

    [Given("the screen is {int} pixels wide")]
    public void GivenWide(int width) => editor.Screen = new Size(width, 800);

    [When("a finger slides along the seek bar from {int} to {int} seconds")]
    public void WhenFingerSlides(int from, int to) => editor.SlideFingerAlongSeekBar(from, to);

    [When("the transport button is let out")]
    public void WhenOut()
    {
        canvasBefore = editor.CanvasHeight;
        editor.Toggle("transport", false);
    }

    [When("the transport button is pressed in")]
    public void WhenIn() => editor.Toggle("transport", true);

    [When("the transport row's more button is pressed")]
    public void WhenMore() => editor.OpenTransportMore();

    [Then("the toolbar is one row tall")]
    public void ThenOneRow() => editor.ToolbarRows.ShouldBe(1);

    [Then("every transport button is on the screen and big enough for a finger")]
    public void ThenInReach() => editor.TransportButtonsOutOfReach.ShouldBeEmpty();

    [Then("the transport row is away, and the playhead line shows")]
    public void ThenAway() => editor.TransportShown.ShouldBe((false, true));

    [Then("the transport row is back, and the playhead line is gone")]
    public void ThenBack() => editor.TransportShown.ShouldBe((true, false));

    [Then("the canvas has grown by the row's height")]
    public void ThenGrown() => (editor.CanvasHeight - canvasBefore).ShouldBeGreaterThanOrEqualTo(TransportRow.Reach - 2);

    [Then("the seek bar is at least {int} pixels wide")]
    public void ThenSeekWidth(int width) => editor.SeekBarWidth.ShouldBeGreaterThanOrEqualTo(width);

    [Then("the length is not on the screen")]
    public void ThenNoLength() => editor.LengthOnScreen.ShouldBeFalse();

    [Then("the length is on the screen")]
    public void ThenLength() => editor.LengthOnScreen.ShouldBeTrue();

    [Then("the transport row has {string} and none of {string}")]
    public void ThenRowHas(string has, string hasNot)
    {
        var offered = editor.TransportButtons;

        foreach (var name in has.Split(", ")) offered.ShouldContain(name);
        foreach (var name in hasNot.Split(", ")) offered.ShouldNotContain(name);
    }
}
