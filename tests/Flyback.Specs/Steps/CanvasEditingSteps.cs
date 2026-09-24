using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Shouldly;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

/// <summary>
/// The canvas's own editing commands, run on the services the editor builds its
/// canvas from (ADR-0150), with no window around them.
/// </summary>
/// <remarks>
/// The canvas holds the patch it shows, so every step hands it back to the scenario
/// afterward: an undo swaps it for another object, and the screen is rendered from
/// whatever the scenario holds.
/// </remarks>
[Binding]
public sealed class CanvasEditingSteps(PatchContext context)
{
    private readonly IServiceProvider canvas = new ServiceCollection().AddCanvas().BuildServiceProvider();

    private readonly List<string> said = [];

    private CanvasHistory History => canvas.GetRequiredService<CanvasHistory>();

    private CanvasSelection Selection => canvas.GetRequiredService<CanvasSelection>();

    private CanvasEdits Edits => canvas.GetRequiredService<CanvasEdits>();

    [Given("the patch is open on the canvas")]
    public void GivenOpenOnTheCanvas()
    {
        canvas.GetRequiredService<CanvasReport>().Said += (_, message) => said.Add(message);

        History.Open(context.Patch);
        Handed();
    }

    [When("the level and the halving module are selected and deleted")]
    public void WhenThePairIsDeleted()
    {
        Select("level", "halve");
        Edits.DeleteSelected();
        Handed();
    }

    [When("everything on the canvas is selected and deleted")]
    public void WhenEverythingIsDeleted()
    {
        Selection.SelectAll();
        Edits.DeleteSelected();
        Handed();
    }

    [When("the halving module is selected and switched off")]
    public void WhenTheHalverIsSwitchedOff()
    {
        Select("halve");
        Edits.SwitchSelected();
        Handed();
    }

    [When("the selection is switched again")]
    public void WhenSwitchedAgain()
    {
        Edits.SwitchSelected();
        Handed();
    }

    [When("the level and the halving module are grouped")]
    public void WhenThePairIsGrouped()
    {
        Select("level", "halve");
        Edits.GroupSelected();
        Handed();
    }

    [When("the halving module alone is grouped")]
    public void WhenOneModuleIsGrouped()
    {
        Select("halve");
        Edits.GroupSelected();
        Handed();
    }

    [When("the patch is laid out")]
    public void WhenLaidOut()
    {
        Edits.Tidy();
        Handed();
    }

    [When("the halving module is selected and duplicated")]
    public void WhenTheHalverIsDuplicated()
    {
        Select("halve");
        Edits.DuplicateSelection();
        Handed();
    }

    [When("the canvas undoes once")]
    public void WhenUndone()
    {
        History.Undo().ShouldBeTrue("there was nothing to undo");
        Handed();
    }

    [Then("the Output is still selected")]
    public void ThenTheOutputIsSelected() =>
        Selection.Nodes.ShouldHaveSingleItem().TypeId.ShouldBe(NodeCatalog.OutputTypeId);

    [Then("the level and the halving module are one group")]
    public void ThenThePairIsOneGroup() =>
        context.Patch.Groups.ShouldNotBeNull().ShouldHaveSingleItem().Members
            .ShouldBe([context.Node("level").Id, context.Node("halve").Id], ignoreOrder: true);

    [Then("the patch has no groups")]
    public void ThenNoGroups() => (context.Patch.Groups ?? []).ShouldBeEmpty();

    [Then("the canvas says {string}")]
    public void ThenTheCanvasSays(string words) => said.ShouldContain(line => line.StartsWith(words, StringComparison.Ordinal));

    [Then("there are two halving modules, and only the new one is selected")]
    public void ThenTwoHalversTheNewOneSelected()
    {
        Halvers.Length.ShouldBe(2);

        var copy = Selection.Nodes.ShouldHaveSingleItem();

        copy.TypeId.ShouldBe("math.mul");
        copy.Id.ShouldNotBe(context.Node("halve").Id);
    }

    [Then("there is one halving module")]
    public void ThenOneHalver() => Halvers.ShouldHaveSingleItem();

    [Then("the canvas has unsaved changes")]
    public void ThenModified() => History.IsModified.ShouldBeTrue();

    [Then("the canvas has no unsaved changes")]
    public void ThenUnmodified() => History.IsModified.ShouldBeFalse();

    private NodeInstance[] Halvers => [.. context.Patch.Nodes.Where(node => node.TypeId == "math.mul")];

    private void Select(params string[] names) =>
        Selection.Take([.. names.Select(name => context.Node(name).Id)]);

    /// <summary>Gives the scenario the patch the canvas now shows.</summary>
    private void Handed() => context.Replace(History.Patch);
}
