using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The tutorials' column, started from its card and checked after edits made in the text view.</summary>
[Binding]
public sealed class TutorialSteps(EditorDriver editor)
{
    [Given("the tutorial {string} is started")]
    [When("the tutorial {string} is started")]
    public void WhenStarted(string title) => editor.StartTutorial(title);

    [When("the patch is written as {string}")]
    public void WhenWritten(string source) => editor.ApplyText(source);

    [Then("the tutorial says to {string}")]
    public void ThenSaysTo(string step) => editor.TutorialStep.ShouldBe(step);

    [Then("the tutorial shows {string}")]
    public void ThenShows(string hint) => editor.TutorialHint.ShouldBe(hint);

    [Then("the tutorial has ticked off {string}")]
    public void ThenTicked(string step) => editor.TutorialTicked.ShouldContain(step);
}
