using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The editor's columns: what the toolbar puts away, swaps and brings back.</summary>
[Binding]
public sealed class LayoutSteps(Editor editor)
{
    private double canvasWas;
    private double previewWas;

    [When("the side column is put away")]
    public void WhenPutAway()
    {
        canvasWas = editor.CanvasWidth;
        previewWas = editor.PreviewWidth;
        editor.Toggle("side", false);
    }

    [When("the side column is brought back")]
    public void WhenBroughtBack() => editor.Toggle("side", true);

    [When("the preview and the canvas are swapped")]
    public void WhenSwapped() => editor.Toggle("swap", true);

    [Then("the canvas has the preview's width as well as its own")]
    public void ThenWider()
    {
        editor.PreviewWidth.ShouldBe(0, 0.5);
        editor.CanvasWidth.ShouldBeGreaterThanOrEqualTo(canvasWas + previewWas);
    }

    [Then("the canvas is as wide as it was")]
    public void ThenAsWide() => editor.CanvasWidth.ShouldBe(canvasWas, 0.5);

    [Then("the side column is showing and cannot be put away")]
    public void ThenStays()
    {
        editor.Toggled("side").ShouldBe((true, false));
        editor.CanvasWidth.ShouldBeGreaterThan(0);
    }
}
