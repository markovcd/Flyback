using Avalonia.Input;
using Reqnroll;
using Shouldly;
using Flyback.Core.Graph;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

/// <summary>
/// What the canvas's keys do to the patch: deleting, switching off, grouping, laying
/// out and duplicating, pressed in the editor's own window.
/// </summary>
[Binding]
public sealed class CanvasEditingSteps(PatchContext context, Editor editor)
{
    [Given("the patch is open in the editor")]
    public void GivenOpenInTheEditor() => editor.Open();

    [Given("the level and the halving module are selected")]
    public void GivenThePairIsSelected() => editor.Select(context.Node("level").Id, context.Node("halve").Id);

    [Given("the halving module is selected")]
    public void GivenTheHalverIsSelected() => editor.Select(context.Node("halve").Id);

    [When("the selection is deleted")]
    public void WhenDeleted() => editor.Press(PhysicalKey.Delete);

    [When("the selection is switched off")]
    [When("the selection is switched on again")]
    public void WhenSwitched() => editor.PressCtrl(PhysicalKey.B);

    [When("the selection is grouped")]
    public void WhenGrouped() => editor.PressCtrl(PhysicalKey.G);

    [When("the patch is laid out")]
    public void WhenLaidOut() => editor.PressCtrl(PhysicalKey.L);

    [When("the selection is duplicated")]
    public void WhenDuplicated() => editor.PressCtrl(PhysicalKey.D);

    [Given("the clock on its own")]
    public void GivenTheClock() => context.Add("clock", "time");

    [When("a wire from the clock is dropped on bare canvas")]
    public void WhenAWireIsDropped() => editor.DropWireFrom(context.Node("clock").Id, 0);

    [When("{string} is picked from the list that opens")]
    public void WhenPicked(string name) => editor.PickFromList(name);

    [Then("a sine is fed by the clock")]
    public void ThenASineIsFed()
    {
        var sine = context.Patch.Nodes.Where(node => node.TypeId == NodeCatalog.SineTypeId).ShouldHaveSingleItem();

        context.Patch.Connections.ShouldContain(wire => wire.SourceNode == context.Node("clock").Id && wire.TargetNode == sine.Id);
    }

    [Then("the level and the halving module are drawn as one box")]
    public void ThenThePairIsOneBox() =>
        context.Patch.Groups.ShouldNotBeNull().ShouldHaveSingleItem().Members
            .ShouldBe([context.Node("level").Id, context.Node("halve").Id], ignoreOrder: true);

    [Then("there are two halving modules, and the new one is selected")]
    public void ThenTwoHalversTheNewOneSelected()
    {
        Halvers.Length.ShouldBe(2);

        var copy = editor.Selected.ShouldHaveSingleItem();

        copy.TypeId.ShouldBe("math.mul");
        copy.Id.ShouldNotBe(context.Node("halve").Id);
    }

    [Then("there is one halving module")]
    public void ThenOneHalver() => Halvers.ShouldHaveSingleItem();

    private NodeInstance[] Halvers => [.. context.Patch.Nodes.Where(node => node.TypeId == "math.mul")];
}
