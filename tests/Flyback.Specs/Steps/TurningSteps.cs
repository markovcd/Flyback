using Flyback.Core.Graph;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>Turning an unwired input's value by its well on the canvas.</summary>
[Binding]
public sealed class TurningSteps(PatchContext context, EditorDriver editor)
{
    private float was;

    private NodeInstance Sine => context.Node("sine");

    private static int Freq => NodeCatalog.BuiltIn.Require(NodeCatalog.SineTypeId).Inputs.ToList().FindIndex(port => port.Name == "freq");

    private static float Default => NodeCatalog.BuiltIn.Require(NodeCatalog.SineTypeId).Inputs[Freq].Default;

    [Given("a sine on its own")]
    public void GivenASine()
    {
        var sine = context.Add("sine", NodeCatalog.SineTypeId);
        sine.X = 200;
        sine.Y = 120;
    }

    [Given("the sine's freq is turned away from its default")]
    public void GivenTurnedAway() => Sine.InputValues[Freq] = Default + 1;

    [When("the sine's freq well is dragged up")]
    public void WhenDragged()
    {
        was = editor.InputOf(Sine.Id, Freq);
        editor.DragWell(Sine.Id, Freq, 40);
    }

    [When("the sine's freq well is Alt-clicked")]
    public void WhenAltClicked() => editor.AltClickWell(Sine.Id, Freq);

    [Then("the sine's freq is higher than it was")]
    public void ThenHigher() => editor.InputOf(Sine.Id, Freq).ShouldBeGreaterThan(was);

    [Then("the sine's freq is back where it was")]
    public void ThenBack() => editor.InputOf(Sine.Id, Freq).ShouldBe(was);

    [Then("the sine's freq is back at its default")]
    public void ThenDefault() => editor.InputOf(Sine.Id, Freq).ShouldBe(Default);
}
