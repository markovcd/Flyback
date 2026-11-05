using Avalonia;
using Avalonia.Input;
using Flyback.Editor.Shell;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>Two fingers on a Mac's trackpad: a scroll moves the view, a pinch zooms it.</summary>
[Binding]
public sealed class TrackpadSteps(PatchContext context, EditorDriver editor)
{
    private double zoomWas;
    private Point panWas;

    /// <summary>Whether the scroll the host reports comes from a mouse wheel; a trackpad until a step says so.</summary>
    private bool mouse;

    [Given("the editor runs on a Mac")]
    public void GivenAMac()
    {
        editor.Setup = editor.Setup with { Host = editor.Setup.Host with { ScrollPans = true, ScrollIsMouse = () => mouse } };

        zoomWas = editor.Read(canvas => canvas.View.Zoom);
        panWas = editor.Read(canvas => canvas.View.Pan);
    }

    [When("two fingers scroll on the trackpad")]
    public void WhenTwoFingersScroll() => editor.Scroll(Beside(), new Vector(0.5, -1.5));

    [When("a mouse wheel turns")]
    public void WhenAMouseWheelTurns()
    {
        mouse = true;
        editor.Scroll(Beside(), new Vector(0, 1));
    }

    [When("two fingers spread on the trackpad")]
    public void WhenTwoFingersSpread() => editor.Pinch(Beside(), 0.3);

    [When("the wheel turns with Cmd held")]
    public void WhenTheWheelTurnsWithCmd() => editor.Scroll(Beside(), new Vector(0, 1), RawInputModifiers.Meta);

    [Then("the view has scrolled with them")]
    public void ThenTheViewScrolled()
    {
        var pan = editor.Read(canvas => canvas.View.Pan);

        pan.X.ShouldBeGreaterThan(panWas.X);
        pan.Y.ShouldBeLessThan(panWas.Y);
    }

    [Then("the zoom is as it was")]
    public void ThenTheZoomIsAsItWas() => editor.Read(canvas => canvas.View.Zoom).ShouldBe(zoomWas);

    [Then("the view is zoomed in")]
    public void ThenZoomedIn() => editor.Read(canvas => canvas.View.Zoom).ShouldBeGreaterThan(zoomWas);

    /// <summary>Bare canvas just above and left of the Filter.</summary>
    private Point Beside()
    {
        var filter = context.Node("Filter");

        return new Point(filter.X - 40, filter.Y - 40);
    }
}
