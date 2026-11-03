using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The tag in the canvas's corner that says the text owns the patch.</summary>
[Binding]
public sealed class OwnerTagSteps(EditorDriver editor)
{
    [Given("the canvas is shown")]
    [When("the canvas is shown")]
    public void WhenTheCanvasIsShown() => editor.ShowCanvas();

    [When("the tag in the canvas's corner is pressed")]
    public void WhenTheTagIsPressed() => editor.PressOwnerTag();

    [Then("the canvas's corner says {string}")]
    public void ThenTheCornerSays(string said) => editor.OwnerTagSays.ShouldBe(said);

    [Then("the canvas's corner says nothing about who owns the patch")]
    public void ThenTheCornerSaysNothing() => editor.OwnerTagSays.ShouldBeNull();

    [Then("the text view is showing")]
    public void ThenTheTextViewIsShowing() => editor.TextShowing.ShouldBeTrue();
}
