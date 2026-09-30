using Avalonia;
using Flyback.App.Canvas;
using Flyback.Core.Graph;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>Fingers on the canvas: what one, two and a held one do.</summary>
[Binding]
public sealed class TouchSteps(PatchContext context, Editor editor)
{
    private double zoomWas;
    private Point panWas;
    private Point clockWas;
    private Point heldAt;

    [Given("a sine beside the clock")]
    public void GivenASineBesideTheClock()
    {
        var clock = context.Node("clock");
        var sine = context.Add("sine", NodeCatalog.SineTypeId);

        sine.X = clock.X + 400;
        sine.Y = clock.Y;
    }

    [When("two fingers spread apart on the canvas")]
    public void WhenTwoFingersSpread()
    {
        Remember();

        var middle = Middle();
        var near = new Vector(30, 0);
        var far = new Vector(120, 0);

        editor.Touch((middle - near, middle - far), (middle + near, middle + far));
    }

    [When("two fingers drag across the canvas")]
    public void WhenTwoFingersDrag()
    {
        Remember();

        var middle = Middle();
        var apart = new Vector(0, 40);
        var across = new Vector(150, 60);

        editor.Touch((middle - apart, middle - apart + across), (middle + apart, middle + apart + across));
    }

    [When("a finger is held on bare canvas")]
    public void WhenAFingerIsHeld()
    {
        var clock = context.Node("clock");

        heldAt = new Point(clock.X + 400, clock.Y + 250);
        editor.HoldFinger(heldAt);
    }

    [When("bare canvas is right-clicked")]
    public void WhenRightClicked()
    {
        var clock = context.Node("clock");

        editor.RightClick(new Point(clock.X + 400, clock.Y + 250));
    }

    [When("a finger draws a wire from the clock to just short of the sine's first input")]
    public void WhenAFingerDrawsAWire()
    {
        var clock = context.Node("clock");
        var sine = context.Node("sine");

        var (from, to) = editor.Read(canvas =>
        {
            var input = canvas.Geometry.InputPort(sine, NodeCatalog.Require(NodeCatalog.SineTypeId), 0);

            return (canvas.GraphToScreen.Transform(NodeGeometry.OutputPort(clock, 0)),
                canvas.GraphToScreen.Transform(input) - new Vector(Fingers.Reach - 4, 0));
        });

        editor.Touch((from, to));
    }

    [Then("the list's filter box waits to be tapped")]
    public void ThenTheFilterWaits() => editor.ListTakesKeys.ShouldBeFalse();

    [Then("the list's filter box takes what is typed")]
    public void ThenTheFilterTakesKeys() => editor.ListTakesKeys.ShouldBeTrue();

    [Then("the canvas is drawn larger")]
    public void ThenLarger() => editor.Read(canvas => canvas.View.Zoom).ShouldBeGreaterThan(zoomWas);

    [Then("the view has moved")]
    public void ThenTheViewMoved() => editor.Read(canvas => canvas.View.Pan).ShouldNotBe(panWas);

    [Then("the clock has not moved")]
    public void ThenTheClockStays()
    {
        var clock = context.Node("clock");

        new Point(clock.X, clock.Y).ShouldBe(clockWas);
    }

    [Then("a sine stands where the finger was held")]
    public void ThenASineStandsThere()
    {
        var sine = context.Patch.Nodes.Where(node => node.TypeId == NodeCatalog.SineTypeId).ShouldHaveSingleItem();

        editor.Read(canvas => canvas.Geometry.Bounds(sine, NodeCatalog.Require(NodeCatalog.SineTypeId)))
            .Inflate(1).Contains(heldAt).ShouldBeTrue();
    }

    private void Remember()
    {
        var clock = context.Node("clock");

        zoomWas = editor.Read(canvas => canvas.View.Zoom);
        panWas = editor.Read(canvas => canvas.View.Pan);
        clockWas = new Point(clock.X, clock.Y);
    }

    /// <summary>The middle of the canvas, on its own control.</summary>
    private Point Middle() => editor.Read(canvas => new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2));
}
