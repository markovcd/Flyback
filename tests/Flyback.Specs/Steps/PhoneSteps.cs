using Avalonia;
using Flyback.App.Canvas;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The editor on a phone's screen: what is in reach, and what a finger opens.</summary>
[Binding]
public sealed class PhoneSteps(PatchContext context, Editor editor)
{
    /// <summary>A common phone's screen, held upright, in the pixels a page is laid out in.</summary>
    private static readonly Size Phone = new(390, 844);

    [Given("the screen is a phone held upright")]
    public void GivenAPhone() => editor.Screen = Phone;

    [When("a finger taps bare canvas")]
    public void WhenBareCanvasIsTapped()
    {
        var clock = context.Node("clock");

        editor.TapFinger(new Point(clock.X, clock.Y + 250));
    }

    [When("a finger taps the sine")]
    public void WhenTheSineIsTapped()
    {
        var sine = context.Node("sine");

        editor.TapFinger(new Point(sine.X + NodeGeometry.Width / 2, sine.Y + NodeGeometry.HeaderHeight / 2));
    }

    [When("the side button is pressed in")]
    public void WhenSideIn() => editor.Toggle("side", true);

    [When("the side button is let out")]
    public void WhenSideOut() => editor.Toggle("side", false);

    [When("the preset gallery is opened")]
    public void WhenTheGalleryIsOpened() => editor.OpenGallery();

    [When("the module panel's {string} button is pressed")]
    public void WhenPanelButtonPressed(string name) => editor.PressPanelButton(name);

    [When("{string} is picked from the toolbar's menu")]
    public void WhenPicked(string label) => editor.PickFromToolbarMenu(label);

    [Then("the toolbar's menu offers {string}")]
    public void ThenMenuOffers(string labels)
    {
        var offered = editor.ToolbarMenu;

        foreach (var label in labels.Split(", ")) offered.ShouldContain(label);
    }

    [Then("the toolbar has no menu")]
    public void ThenNoMenu() => editor.ToolbarMenu.ShouldBeEmpty();

    [Then("the settings are up")]
    public void ThenSettingsUp() => editor.SettingsUp.ShouldBeTrue();

    [Then("every toolbar button is on the screen")]
    public void ThenEveryToolbarButtonShows() => editor.ToolbarButtonsOffScreen.ShouldBeEmpty();

    [Then("the canvas has no width")]
    public void ThenNoCanvas() => editor.CanvasWidth.ShouldBe(0);

    [Then("the canvas has the window's width")]
    public void ThenWholeWidth() => editor.CanvasWidth.ShouldBe(Phone.Width, tolerance: 1);

    [Then("the module panel's {string} button is on the screen")]
    public void ThenPanelButtonShows(string name) => editor.PanelButtonOnScreen(name).ShouldBeTrue();

    [Then("the status line leaves the report at least {int} pixels")]
    public void ThenReportHasRoom(int width) => editor.ReportWidth.ShouldBeGreaterThanOrEqualTo(width);

    [Then("the module panel says {string}")]
    public void ThenPanelSays(string text) => editor.PanelText.ShouldContain(text);

    [Then("the module panel does not say {string}")]
    public void ThenPanelDoesNotSay(string text) => editor.PanelText.ShouldNotContain(text);

    [Then("the gallery's filter box waits to be tapped")]
    public void ThenGalleryWaits() => editor.GalleryTakesKeys.ShouldBeFalse();

    [Then("the question offers {string}, each on the screen")]
    public void ThenOffers(string labels)
    {
        var answers = editor.Answers;

        answers.Select(a => a.Label).ShouldBe(labels.Split(", "));
        answers.ShouldAllBe(a => a.OnScreen);
    }
}
