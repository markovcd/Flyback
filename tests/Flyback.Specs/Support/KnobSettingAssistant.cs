using System.Runtime.CompilerServices;
using System.Text.Json;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Settings;

namespace Flyback.Specs.Support;

/// <summary>
/// An assistant with no model behind it: each turn it sets the knobs it was told to,
/// if any, proposes what it has, and remembers what it was sent.
/// </summary>
internal sealed class KnobSettingAssistant : IPatchAssistant, IPatchSession
{
    private readonly AssistantSchema schema = new("knobs", [new AssistantModel("knobs")], "NONE", "none needed");
    private PatchWorkbench? bench;

    /// <summary>Every message it was sent, as it arrived.</summary>
    public List<string> Heard { get; } = [];

    /// <summary><c>set_knobs</c> arguments for the next turn, or null to set none.</summary>
    public string? Knobs { get; set; }

    /// <summary>What each turn reports it cost, or null to report none.</summary>
    public PatchEvent.Cost? Costs { get; set; }

    public string Id => "knobs";

    public string Name => "Knobs";

    public int Priority => 0;

    public AssistantCredential Credential => schema.Credential;

    public Uri? Endpoint(SettingValues values) => new("https://assistant.test/");

    public IReadOnlyList<SettingField> Form(SettingValues values) => schema.Form(values);

    public AssistantSenses Senses(SettingValues values) => schema.Senses(values);

    public string? Unavailable(AssistantConfig config) => null;

    public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config)
    {
        bench = workbench;
        return this;
    }

    public async IAsyncEnumerable<PatchEvent> Ask(string instruction, [EnumeratorCancellation] CancellationToken cancel)
    {
        Heard.Add(instruction);

        if (bench is null) yield break;

        if (Knobs is { } knobs)
        {
            await bench.InvokeAsync("set_knobs", JsonSerializer.Deserialize<JsonElement>(knobs), cancel);
            Knobs = null;
        }

        if (Costs is { } cost) yield return cost;

        yield return new PatchEvent.Proposed(bench.Snapshot(), "as it stands");
    }

    public void Dispose()
    {
    }
}
