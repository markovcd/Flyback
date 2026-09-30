using Flyback.App;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Flyback.Specs.Support;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The editor as a browser page holds it: <see cref="App.EditorHost.InPage"/>.</summary>
[Binding]
public sealed class PageSteps(Editor editor, PatchContext context)
{
    private readonly HandedViewer viewer = new();

    [Given("the editor is in a page")]
    public void GivenInAPage()
    {
        editor.Setup = editor.Setup with { Host = editor.Setup.Host with { InPage = true } };
        editor.Services += services => services.AddSingleton<IViewer>(viewer);
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
        editor.OpenShared(name, fileName, System.Text.Encoding.UTF8.GetBytes(Flyback.Core.Graph.PatchIO.ToJson(context.Patch)));

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
