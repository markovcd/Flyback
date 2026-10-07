using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Flyback.Editor;
using Flyback.Specs.Support;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The editor as a browser page holds it: <see cref="App.EditorHost.InPage"/>.</summary>
[Binding]
public sealed class PageSteps(EditorDriver editor, PatchContext context)
{
    private readonly HandedViewer viewer = new();

    /// <summary>The page's local storage.</summary>
    private readonly KeptByBrowser browser = new();

    private int homed;

    [Given("the editor is in a page")]
    public void GivenInAPage()
    {
        editor.Setup = editor.Setup with { Host = editor.Setup.Host with { InPage = true, Home = () => homed++ } };
        editor.Services += services =>
        {
            services.AddSingleton<IViewer>(viewer);
            services.AddSingleton<IBrowserStore>(browser);
        };
    }

    [When("View it is pressed")]
    public void WhenViewItPressed() => editor.PressPanelButton("view-it");

    [Then("the viewer is handed the patch under the editor's title, as a bundle whose text has {string}")]
    public void ThenViewerHanded(string text)
    {
        var (handed, bundle) = viewer.Handed.ShouldNotBeNull();

        editor.Title.ShouldStartWith(handed);
        PatchPrinter.Print(PatchBundle.Read(new MemoryStream(bundle)).Patch).ShouldContain(text);
    }

    [Then("the toolbar has none of {string}")]
    public void ThenHasNone(string names) => editor.ToolbarButtons.ShouldNotContain(name => Names(names).Contains(name));

    [Then("the toolbar still has {string}")]
    public void ThenStillHas(string names)
    {
        var offered = editor.ToolbarButtons;

        foreach (var name in Names(names)) offered.ShouldContain(name);
    }

    [Then("the toolbar has {string} right after {string}")]
    public void ThenRightAfter(string name, string before)
    {
        var offered = editor.ToolbarButtons.ToList();
        var at = offered.IndexOf(before);

        at.ShouldBeGreaterThanOrEqualTo(0, $"the toolbar has no {before}");
        offered.ElementAtOrDefault(at + 1).ShouldBe(name);
    }

    [Then("the page's settings offer {string}")]
    public void ThenPageSettingsOffer(string labels) =>
        editor.PageSettings.ShouldBe(labels.Split(", "));

    [When("{string} is ticked on the page's settings")]
    public void WhenTickedOnPage(string label) => editor.TickOnPage(label, on: true);

    [Then("the left button pans empty canvas")]
    public void ThenLeftPans() => editor.Read(canvas => canvas.Gestures.DragToPan).ShouldBeTrue();

    [Then("the page's browser keeps it for the next visit")]
    public void ThenBrowserKeeps() =>
        Editor.Canvas.CanvasSettings.Parse(browser.Read(Editor.Canvas.CanvasSettings.Section)).DragToPan.ShouldBeTrue();

    [When("the Flyback mark is pressed")]
    public void WhenMarkPressed() => editor.PressPanelButton("home");

    [Then("the page leaves for the site's front page")]
    public void ThenHomed() => homed.ShouldBe(1);

    [Then("the status bar offers no letter, and no rule before one")]
    public void ThenNoLetter() => editor.StatusBarLetter.ShouldBe((false, false));

    [Then("the status bar offers a letter, set apart by a rule")]
    public void ThenLetter() => editor.StatusBarLetter.ShouldBe((true, true));

    [Then("the gallery has no preset site section")]
    public void ThenNoSiteSection() => editor.GalleryListsSite.ShouldBeFalse();

    [Then("the page may be left without asking")]
    public void ThenLeavesQuietly() => editor.SomethingToLose.ShouldBeFalse();

    [Then("the page asks before it is left")]
    public void ThenAsks() => editor.SomethingToLose.ShouldBeTrue();

    [When("the picture is double-clicked")]
    public void WhenDoubleClicked() => editor.FullScreen();

    [Then("the picture does not have the whole window")]
    public void ThenNotFullScreen() => editor.PictureFullScreen.ShouldBeFalse();

    [Then("the picture is drawn at {int} x {int}")]
    public void ThenDrawnAt(int width, int height) => editor.PictureSize.ShouldBe(new Avalonia.PixelSize(width, height));

    private string? answer;

    [When("a script applies the page's text with {string} changed to {string}")]
    public void WhenScriptEdits(string from, string to)
    {
        var text = editor.ScriptedText;

        text.ShouldContain(from);
        answer = editor.ApplyScripted(text.Replace(from, to));
    }

    [When("a script applies the text {string}")]
    public void WhenScriptApplies(string text) => answer = editor.ApplyScripted(text);

    [Then("the script is told nothing is wrong")]
    public void ThenNothingWrong() => answer.ShouldBeNull();

    [Then("the script is told what is wrong on line {int}")]
    public void ThenToldWhatIsWrong(int line) => answer.ShouldNotBeNull().ShouldStartWith($"{line}:");

    [Then("the page's text has {string}")]
    public void ThenTextHas(string text) => editor.ScriptedText.ShouldContain(text);

    [When("the page hands the editor the shared preset {string} as {string}")]
    public void WhenHandedShared(string name, string fileName) =>
        editor.OpenShared(name, fileName, System.Text.Encoding.UTF8.GetBytes(PatchIO.ToJson(context.Patch)));

    [Then("the editor says it opened {string} from the preset site")]
    public void ThenOpenedShared(string name) => editor.Reported.ShouldContain($"Opened “{name}” from the preset site.");

    private bool? opened;

    [When("the page hands the editor a shared preset needing the {string} plugin")]
    public void WhenHandedNeeding(string plugin) => opened = editor.OpenShared(plugin, plugin + ".fbk", PluginPatch.Needing(plugin));

    [Then("the editor does not open it, and says it needs {string}")]
    public void ThenNotOpened(string plugin)
    {
        opened.ShouldBe(false);
        editor.Reported[^1].ShouldStartWith("Not opened.");
        editor.Reported[^1].ShouldContain(plugin);
    }

    [Then("the editor is titled {string}")]
    public void ThenTitled(string name) => editor.Title.ShouldStartWith(name);

    [Then("the sine is at {float} Hz")]
    public void ThenSineAt(float frequency)
    {
        // Found by its type, since text names a module afresh.
        context.Name("sine", context.Patch.Nodes.Single(node => node.TypeId == "osc.sine"));
        context.StoredInput("sine", "freq").ShouldBe(frequency);
    }

    private static string[] Names(string names) => names.Split(", ");
}
