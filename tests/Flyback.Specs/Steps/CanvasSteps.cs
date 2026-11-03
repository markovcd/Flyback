using Avalonia;
using Avalonia.Input;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>How the canvas draws a module, and what the mouse does on it.</summary>
[Binding]
public sealed class CanvasSteps(PatchContext context, EditorDriver editor) : IDisposable
{
    /// <summary>The schemes folder a scenario made, removed after it.</summary>
    private string? schemes;

    /// <summary>The settings file a scenario that restarts keeps, removed after it.</summary>
    private string? settings;

    /// <summary>How many times Flyback was asked to start again.</summary>
    private int restarts;

    public void Dispose()
    {
        if (schemes is not null) Directory.Delete(schemes, recursive: true);
        if (settings is not null && File.Exists(settings)) File.Delete(settings);
    }

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
        editor.Hover(canvas => canvas.Geometry.InputPort(filter, Filter, Cutoff) + new Vector(30, 0));
        editor.Tip.ShouldBe($"{value}\n{Filter.Inputs[Cutoff].Help}");
    }

    [Given("a color scheme called {word} in the schemes folder")]
    public void GivenASchemeInTheFolder(string name)
    {
        schemes = Directory.CreateTempSubdirectory("flyback-scheme-specs").FullName;
        File.WriteAllText(Path.Combine(schemes, $"{name.ToLowerInvariant()}.json"), $$"""{ "format": 1, "name": "{{name}}", "accent": "#FF9A1F" }""");

        editor.Setup = editor.Setup with { Folders = editor.Setup.Folders with { SchemeFolder = schemes } };
    }

    [When("{word} is chosen as the editor's colors")]
    public void WhenColorsAreChosen(string scheme)
    {
        editor.OpenSettings("Canvas");
        editor.PickSetting("colorScheme", scheme);
        editor.Answer("Save");
    }

    [Then("the editor starts in {word} the next time")]
    public void ThenTheEditorStartsIn(string scheme) => editor.NextColorScheme.ShouldBe(scheme);

    [Given("Flyback can start itself again")]
    public void GivenFlybackCanRestart()
    {
        settings = Path.Combine(Path.GetTempPath(), $"flyback-restart-{Guid.NewGuid():N}.json");
        editor.Setup = editor.Setup with
        {
            Folders = editor.Setup.Folders with { SettingsPath = settings },
            Host = editor.Setup.Host with { Relaunch = _ => restarts++ },
        };
    }

    [When("{word} is chosen as the editor's colors, leaving the offer to restart ticked")]
    public void WhenColorsAreChosenWithTheRestart(string scheme)
    {
        editor.OpenSettings("Canvas");
        editor.PickSetting("colorScheme", scheme);
        editor.Tick("Restart Flyback to use the new colors", on: true);
        editor.Answer("Save");
    }

    [Then("Flyback starts again, in {word}")]
    public void ThenFlybackStartsAgainIn(string scheme)
    {
        restarts.ShouldBe(1);
        CanvasSettings.Load(settings!).ColorScheme.ShouldBe(scheme);
    }

    [Then("the editor is drawn in Graphite until then")]
    public static void ThenStillGraphite() => Ui.Controls.Colors.Current.ShouldBe(Ui.Controls.SchemeLibrary.Default);

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
