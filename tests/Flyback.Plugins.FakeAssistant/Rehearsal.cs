using System.Runtime.CompilerServices;
using System.Text.Json;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.FakeAssistant;

internal sealed class Rehearsal(PatchWorkbench workbench) : IPatchSession
{
    /// <summary>
    /// A gray field on the screen. Small on purpose: what is being proved is
    /// that the vocabulary reaches across the plugin boundary intact, not that
    /// anything clever can be built with it.
    /// </summary>
    private static readonly (string Tool, string Arguments)[] Script =
    [
        ("add_module", """{"type_id":"value","handle":"knob1","knobs":[{"port":"value","value":0.5}]}"""),

        // No sink is added: every patch arrives with its Output already placed,
        // under the handle the workbench gave it.
        ("connect", """{"from":"knob1","to":"output1","to_port":"color"}"""),
        ("render", """{"times":[0.5]}"""),
        ("propose", """{"summary":"a flat gray field"}"""),
    ];

    public async IAsyncEnumerable<PatchEvent> Ask(
        string instruction,
        [EnumeratorCancellation] CancellationToken cancel)
    {
        yield return new PatchEvent.Said($"Rehearsing: {instruction}");

        // A way for a test to ask for the unhappy path without a second fixture.
        if (instruction.Contains("fail", StringComparison.OrdinalIgnoreCase))
        {
            yield return new PatchEvent.Failed("asked to fail, so it did.");
            yield break;
        }

        foreach (var (tool, arguments) in Script)
        {
            var outcome = await workbench
                .InvokeAsync(tool, JsonSerializer.Deserialize<JsonElement>(arguments), cancel)
                .ConfigureAwait(false);

            if (!outcome.Ok)
            {
                yield return new PatchEvent.Failed(outcome.Text);
                yield break;
            }

            yield return outcome.Png is { } png
                ? new PatchEvent.Saw(png, outcome.Text)
                : new PatchEvent.Did(outcome.Text);
        }

        yield return new PatchEvent.Cost(0, 0, 0);

        if (workbench.HasProposal)
            yield return new PatchEvent.Proposed(workbench.Snapshot(), workbench.ProposalSummary);
    }

    public void Dispose()
    {
    }
}
