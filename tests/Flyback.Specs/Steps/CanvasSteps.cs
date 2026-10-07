using Avalonia;
using Avalonia.Input;
using Reqnroll;
using Shouldly;
using Flyback.Core.Graph;
using Flyback.Specs.Support;
using Flyback.Editor.Canvas;

namespace Flyback.Specs.Steps;

/// <summary>How the canvas draws a module, and what the mouse does on it.</summary>
[Binding]
public sealed class CanvasSteps(PatchContext context, EditorDriver editor)
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

    private Point originBefore;

    [Given("drag to pan is switched on")]
    public void GivenDragToPanIsOn()
    {
        editor.OpenSettings("Canvas");
        editor.Tick("Drag empty canvas to pan", on: true);
        editor.Answer("Save");

        originBefore = Origin();
    }

    [When("empty canvas beside the Filter is dragged")]
    public void WhenEmptyCanvasIsDragged()
    {
        var bounds = Bounds();

        editor.DragCanvas(bounds.TopLeft - new Vector(20, 20), bounds.BottomLeft + new Vector(-20, 0), MouseButton.Left);
    }

    [When("the right button is dragged across the Filter")]
    public void WhenTheRightButtonIsDraggedAcross()
    {
        var bounds = Bounds();

        editor.DragCanvas(bounds.TopLeft - new Vector(20, 20), bounds.BottomRight + new Vector(20, 20), MouseButton.Right);
    }

    [Then("the view follows the drag")]
    public void ThenTheViewHasMoved() => Origin().ShouldNotBe(originBefore);

    [Then("the view stays put")]
    public void ThenTheViewHasNotMoved() => Origin().ShouldBe(originBefore);

    [Then("nothing is selected")]
    public void ThenNothingIsSelected() => editor.Read(canvas => canvas.Selection.Count).ShouldBe(0);

    [Then("the Filter is selected")]
    public void ThenTheFilterIsSelected()
    {
        var filter = context.Node("Filter").Id;

        editor.Read(canvas => canvas.Selection.Contains(filter)).ShouldBeTrue();
    }

    private Point Origin() => editor.Read(canvas => canvas.GraphToScreen.Transform(new Point(0, 0)));

    private Rect Bounds()
    {
        var filter = context.Node("Filter");

        return editor.Read(canvas => canvas.Geometry.Bounds(filter, Filter));
    }

    /// <summary>How tall the canvas draws the Filter now.</summary>
    private double Drawn()
    {
        var filter = context.Node("Filter");

        return editor.Read(canvas => canvas.Geometry.Bounds(filter, Filter).Height);
    }
}
