using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The groups of gestures the empty panel lists, and folding them.</summary>
[Binding]
public sealed class ShortcutSteps(EditorDriver editor)
{
    [When("the panel's {string} group is pressed")]
    public void WhenGroupPressed(string title) => editor.PressShortcutGroup(title);

    [Then("the panel's {string} group is open")]
    public void ThenGroupOpen(string title) => editor.ShortcutGroupOpen(title).ShouldBeTrue();

    [Then("the panel's {string} group is folded")]
    public void ThenGroupFolded(string title) => editor.ShortcutGroupOpen(title).ShouldBeFalse();
}
