using Reqnroll;
using Shouldly;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

/// <summary>How the canvas draws a module: its rows, its size and what hovering it says.</summary>
/// <remarks>
/// Measured with the layout's metrics rather than by turning <see cref="NodeGeometry.Compact"/>
/// on, which is one switch for the whole program and would reach scenarios running beside
/// this one. The app's tests hold the two to the same numbers.
/// </remarks>
[Binding]
public sealed class CanvasSteps(PatchContext context)
{
    private static NodeDef Filter => NodeCatalog.Require(NodeCatalog.FilterTypeId);

    private static readonly PatchLayout.Metrics Full = PatchLayout.Metrics.Default;

    private PatchLayout.Metrics drawn = Full;

    [Given("a Filter")]
    public void GivenAFilter() => context.Add("Filter", NodeCatalog.FilterTypeId);

    [Given("a Filter whose cutoff is set to {float}")]
    public void GivenAFilterWhoseCutoffIs(float value)
    {
        GivenAFilter();
        context.Node("Filter").InputValues[Cutoff] = value;
    }

    [When("modules are drawn compact")]
    public void WhenModulesAreDrawnCompact() => drawn = Full with { SharedRows = true };

    [Then("the Filter is shorter than when drawn in full")]
    public void ThenTheFilterIsShorter() => drawn.Height(Filter).ShouldBeLessThan(Full.Height(Filter));

    [Then("the Filter's first input is level with its first output")]
    public void ThenTheFirstInputIsLevelWithTheFirstOutput() =>
        drawn.InputPort(Filter, 0).ShouldBe(drawn.OutputPort(0));

    [Then("hovering the Filter's cutoff shows {word} before what the socket is for")]
    public void ThenHoveringTheCutoffShows(string value) =>
        SocketTips.Say(context.Patch, context.Node("Filter"), Filter, Cutoff, isOutput: false)
            .ShouldBe($"{value}\n{Filter.Inputs[Cutoff].Help}");

    private static int Cutoff => Filter.Inputs.ToList().FindIndex(p => p.Name == "cutoff");
}
