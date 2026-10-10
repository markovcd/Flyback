using System.Text.Json.Nodes;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>What a script driving the editor is told: the page's <c>window.flyback.state()</c> and the Android editor's broadcast.</summary>
[Binding]
public sealed class ReadoutSteps(EditorDriver editor)
{
    private JsonObject? told;

    [Given("the editor is started on the interpreter")]
    public void GivenInterpreted() => editor.Setup = editor.Setup with { Launch = editor.Setup.Launch with { Interpreted = true } };

    [When("a script asks the editor how it is doing")]
    public void WhenAsked() => told = editor.Readout;

    [Then("the script is told the sound runs on {string}")]
    public void ThenRunsOn(string backend) => ((string?)Told["soundRunsOn"]).ShouldBe(backend);

    [Then("the script is told the sound renders faster than real time")]
    public void ThenFaster() => ((double)Told["soundSpeed"]!).ShouldBeGreaterThan(1);

    [Then("the script is told nothing of how fast the sound renders")]
    public void ThenNoSpeed() => ((double)Told["soundSpeed"]!).ShouldBe(0);

    private JsonObject Told => told ?? throw new InvalidOperationException("No script has asked the editor yet.");
}
