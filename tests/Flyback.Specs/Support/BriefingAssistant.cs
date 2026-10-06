using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Settings;

namespace Flyback.Specs.Support;

/// <summary>
/// An assistant with no model behind it: asked to write an idea out, it answers with
/// <see cref="Brief"/>; asked anything else, it says it is on it. It remembers what
/// it was sent.
/// </summary>
internal sealed class BriefingAssistant : IPatchAssistant, IPatchSession
{
    public const string Brief = "Night Freight: slow dub techno at 118 bpm in F minor, 48 bars, in five sections.";

    private readonly AssistantSchema schema = new("briefing", [new AssistantModel("briefing")], "NONE", "none needed");

    /// <summary>Every message it was sent, as it arrived.</summary>
    public ConcurrentQueue<string> Heard { get; } = [];

    /// <summary>The model each run it was started for was configured with, in order.</summary>
    public ConcurrentQueue<string> Models { get; } = [];

    public string Id => "briefing";

    public string Name => "Briefing";

    public int Priority => 0;

    public bool NeedsKey => false;

    public AssistantCredential Credential => schema.Credential;

    public Uri? Endpoint(SettingValues values) => new("https://assistant.test/");

    public IReadOnlyList<SettingField> Form(SettingValues values) => schema.Form(values);

    public AssistantSenses Senses(SettingValues values) => schema.Senses(values);

    public string? Unavailable(AssistantConfig config) => null;

    public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config)
    {
        Models.Enqueue(schema.Read(config.Values).Model);
        return this;
    }

    public async IAsyncEnumerable<PatchEvent> Ask(string instruction, [EnumeratorCancellation] CancellationToken cancel)
    {
        Heard.Enqueue(instruction);

        await Task.Yield();

        yield return new PatchEvent.Said(instruction.Contains("build nothing") ? Brief : "On it.");
    }

    public void Dispose()
    {
    }
}
