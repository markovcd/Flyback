using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The editor as a browser page holds it: <see cref="App.EditorSetup.InPage"/>.</summary>
[Binding]
public sealed class PageSteps(Editor editor)
{
    [Given("the editor is in a page")]
    public void GivenInAPage() => editor.Setup = editor.Setup with { InPage = true };

    [Then("the toolbar has none of {string}")]
    public void ThenHasNone(string names) => editor.ToolbarButtons.ShouldNotContain(name => Names(names).Contains(name));

    [Then("the toolbar still has {string}")]
    public void ThenStillHas(string names)
    {
        var offered = editor.ToolbarButtons;

        foreach (var name in Names(names)) offered.ShouldContain(name);
    }

    [When("the picture is double-clicked")]
    public void WhenDoubleClicked() => editor.FullScreen();

    [Then("the picture does not have the whole window")]
    public void ThenNotFullScreen() => editor.PictureFullScreen.ShouldBeFalse();

    private static string[] Names(string names) => names.Split(", ");
}
