using System.Runtime.CompilerServices;
using System.Text.Json;
using Flyback.Plugins.Assist;

namespace Flyback.Plugins.FakeAssistant;

/// <summary>
/// A plugin that offers an assistant which has already made up its mind.
/// </summary>
/// <remarks>
/// It exists to be loaded off disk by the tests, and it doubles as the worked
/// example: this is the whole of what contributing an assistant takes, minus
/// the part where a provider is asked anything. Everything it does goes through
/// <see cref="PatchWorkbench"/> and the contract, so if it can build a patch
/// from out here then so can a real one.
/// </remarks>
public sealed class RehearsedAssistantPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "flyback.rehearsed",
        "Rehearsed assistant",
        "Replays a fixed sequence of edits, so the tests can drive the whole path without a network.");

    public void Register(IPluginRegistry registry) => registry.AddPatchAssistant(new RehearsedAssistant());
}

public sealed class RehearsedAssistant : IPatchAssistant
{
    public string Id => "rehearsed";

    public string Name => "Rehearsed assistant";

    public int Priority => -100;

    public AssistantSchema Schema { get; } = new(
        "rehearsal",
        ["rehearsal"],
        "FLYBACK_REHEARSED_KEY",
        "No key is needed; this one has already decided what it is going to do.");

    /// <summary>Always ready. It has nowhere to connect to and nothing to pay.</summary>
    public string? Unavailable(AssistantConfig config) => null;

    public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) => new Rehearsal(workbench);
}

internal sealed class Rehearsal(PatchWorkbench workbench) : IPatchSession
{
    /// <summary>
    /// A grey field on the screen. Small on purpose: what is being proved is
    /// that the vocabulary reaches across the plugin boundary intact, not that
    /// anything clever can be built with it.
    /// </summary>
    private static readonly (string Tool, string Arguments)[] Script =
    [
        ("add_module", """{"type_id":"value","handle":"knob1","knobs":[{"port":"value","value":0.5}]}"""),

        // No sink is added: every patch arrives with its Output already placed,
        // under the handle the workbench gave it.
        ("connect", """{"from":"knob1","to":"output1","to_port":"colour"}"""),
        ("render", """{"times":[0.5]}"""),
        ("propose", """{"summary":"a flat grey field"}"""),
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
