using Avalonia.Input;
using Flyback.Core.Graph;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>Rolling the selected module's knobs in the editor, through its shortcut.</summary>
[Binding]
public sealed class ModuleRollSteps(PatchContext context, EditorDriver editor)
{
    private float noted;

    [Given("a sine with its phase wired from a clock")]
    public void GivenASine()
    {
        context.Add("clock", NodeCatalog.TimeTypeId);
        context.Add("sine", NodeCatalog.SineTypeId);
        context.Wire("clock", "t", "sine", "phase");
    }

    [Given("the sine is selected")]
    public void GivenSelected() => editor.Select(context.Node("sine").Id);

    [When("the selection's knobs are rolled")]
    public void WhenRolled()
    {
        editor.RollModulesOnASeededDie();
        editor.PressCtrl(PhysicalKey.D, shift: true);
    }

    [When("the sine's {string} is noted")]
    public void WhenNoted(string input) => noted = At(input);

    [Then("the sine's {string} has moved from {float}")]
    public void ThenMoved(string input, float from) => At(input).ShouldNotBe(from);

    [Then("the sine's {string} still rests at {float}")]
    public void ThenStill(string input, float at) => At(input).ShouldBe(at, 1e-6f);

    [Then("the sine's {string} is what was noted")]
    public void ThenNotedAgain(string input) => At(input).ShouldBe(noted);

    private float At(string input) =>
        editor.InputOf(context.Node("sine").Id, NodeCatalog.Get(NodeCatalog.SineTypeId)!.Inputs.ToList().FindIndex(port => port.Name == input));
}
