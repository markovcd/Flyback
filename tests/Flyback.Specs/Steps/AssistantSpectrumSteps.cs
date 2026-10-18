using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The assistant's <c>listen</c>, on a workbench with a tone wired to the Output.</summary>
[Binding]
public sealed class AssistantSpectrumSteps
{
    private readonly PatchWorkbench bench = new(NodeCatalog.BuiltIn, new Patch(), vision: false, Listener.Itself);
    private ToolOutcome? answered;

    [Given("the assistant has wired a {int} Hz tone to the speakers")]
    public async Task GivenATone(int hz)
    {
        await Call("write_patch", $$"""{"source":"let tone1 = t |> sine(freq: {{hz}})\ntone1 |> out.left"}""");
    }

    [When("the assistant listens to it")]
    public async Task WhenListened() => answered = await Call("listen", """{"seconds":1}""");

    [Then("it is told the sound has a tone at {int} Hz")]
    public void ThenTone(int hz)
    {
        answered!.Ok.ShouldBeTrue(answered.Text);
        answered.Text.ShouldContain($"Tones, narrow peaks");
        answered.Text.ShouldContain($"{hz} Hz 0 dB");
    }

    private async Task<ToolOutcome> Call(string tool, string arguments)
    {
        var outcome = await bench.InvokeAsync(tool, JsonSerializer.Deserialize<JsonElement>(arguments), CancellationToken.None);

        outcome.Ok.ShouldBeTrue(outcome.Text);

        return outcome;
    }
}
