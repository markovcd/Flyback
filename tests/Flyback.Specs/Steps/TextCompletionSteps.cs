using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The list of what may be typed, popped up under the caret in the text view.</summary>
[Binding]
public sealed class TextCompletionSteps(EditorDriver editor)
{
    [Given("the text view is open and empty")]
    public void GivenEmpty() => editor.StartTyping();

    [When("{string} is typed into the text")]
    public void WhenTyped(string said) => editor.TypeInText(said);

    [When("what the list has picked is chosen")]
    public void WhenChosen() => editor.ChooseOffered();

    [Then("the list under the caret offers {string}")]
    public void ThenOffers(string word) => editor.Offered.ShouldNotBeNull("the list should be open").ShouldContain(word);

    [Then("nothing is offered")]
    public void ThenNothing() => editor.Offered.ShouldBeNull();

    [Then("the sockets above the caret mark {string} as the one being filled")]
    public void ThenFilling(string socket) => editor.SignatureFilling.ShouldBe(socket);

    [Then("resting on {string} says {string}")]
    public void ThenHover(string word, string said) => editor.HoverOn(word).ShouldNotBeNull().ShouldStartWith(said);

    [Then("the text reads {string}")]
    public void ThenReads(string text) => editor.Text.ShouldBe(text);
}
