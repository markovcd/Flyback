using Reqnroll;
using Shouldly;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

/// <summary>How the canvas draws a module: its rows, its size and what hovering it says.</summary>
[Binding]
public sealed class CanvasSteps(PatchContext context, Editor editor)
{
    private static NodeDef Filter => NodeCatalog.Require(NodeCatalog.FilterTypeId);

    private static int Cutoff => Filter.Inputs.ToList().FindIndex(p => p.Name == "cutoff");

    private double fullHeight;

    [Given("a Filter")]
    public void GivenAFilter() => context.Add("Filter", NodeCatalog.FilterTypeId);

    [Given("a Filter whose cutoff is set to {float}")]
    public void GivenAFilterWhoseCutoffIs(float value)
    {
        GivenAFilter();
        context.Node("Filter").InputValues[Cutoff] = value;
    }

    [When("modules are drawn compact")]
    public void WhenModulesAreDrawnCompact()
    {
        fullHeight = Drawn();

        editor.OpenSettings("Canvas");
        editor.Tick("Compact modules", on: true);
        editor.Answer("Save");
    }

    [Then("the Filter is shorter than when drawn in full")]
    public void ThenTheFilterIsShorter() => Drawn().ShouldBeLessThan(fullHeight);

    [Then("the Filter's first input is level with its first output")]
    public void ThenTheFirstInputIsLevelWithTheFirstOutput()
    {
        var filter = context.Node("Filter");

        editor.Read(canvas => canvas.Geometry.InputPort(filter, Filter, 0).Y).ShouldBe(NodeGeometry.OutputPort(filter, 0).Y);
    }

    [Then("hovering the Filter's cutoff shows {word} before what the socket is for")]
    public void ThenHoveringTheCutoffShows(string value)
    {
        var filter = context.Node("Filter");

        // On the row's name, clear of the socket's dot.
        editor.Hover(canvas => canvas.Geometry.InputPort(filter, Filter, Cutoff) + new Avalonia.Vector(30, 0));
        editor.Tip.ShouldBe($"{value}\n{Filter.Inputs[Cutoff].Help}");
    }

    /// <summary>How tall the canvas draws the Filter now.</summary>
    private double Drawn()
    {
        var filter = context.Node("Filter");

        return editor.Read(canvas => canvas.Geometry.Bounds(filter, Filter).Height);
    }
}
