using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The assistant's <c>measure</c>, on a workbench with nothing wired to the Output.</summary>
[Binding]
public sealed class AssistantMeasureSteps
{
    private readonly PatchWorkbench bench = new(NodeCatalog.BuiltIn, new Patch(), vision: false, Listener.None);
    private ToolOutcome? answered;

    [Given("the assistant has added an LFO turning {int} times a second")]
    public async Task GivenAnLfo(int hz)
    {
        var added = await Call("add_module", $$"""{"type_id":"osc.sine","handle":"lfo1","knobs":[{"port":"freq","value":{{hz}}}]}""");

        added.Ok.ShouldBeTrue(added.Text);
    }

    [When("the assistant measures it")]
    public async Task WhenMeasured() => answered = await Call("measure", """{"handles":["lfo1"]}""");

    [Then("it is told the LFO repeats {int} times a second")]
    public void ThenRepeats(int hz)
    {
        answered!.Ok.ShouldBeTrue(answered.Text);
        answered.Text.ShouldContain($"lfo1.out");
        answered.Text.ShouldContain($"{hz} Hz");
    }

    private Task<ToolOutcome> Call(string tool, string arguments) =>
        bench.InvokeAsync(tool, JsonSerializer.Deserialize<JsonElement>(arguments), CancellationToken.None);
}
