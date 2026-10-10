using System.Runtime.CompilerServices;
using System.Text.Json;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.FakeAssistant;

public sealed class RehearsedAssistant : IPatchAssistant
{
    public string Id => "rehearsed";

    public string Name => "Rehearsed assistant";

    public int Priority => -100;

    public AssistantSchema Schema { get; } = new(
        "rehearsal",

        // Neither, and truthfully so: this one answers from a script and has
        // nowhere to send a picture or a sound even if it wanted one.
        [new AssistantModel("rehearsal", Vision: false)],
        "FLYBACK_REHEARSED_KEY",
        "No key is needed; this one has already decided what it is going to do.");

    public AssistantCredential Credential => Schema.Credential;

    public Uri Endpoint(SettingValues values) => new("https://assistant.test/");

    /// <summary>
    /// The ordinary form, declared by the schema. Nothing here reads any of it —
    /// this one has made up its mind — but a worked example that skipped the
    /// settings would be a worked example of half the contract.
    /// </summary>
    public IReadOnlyList<SettingField> Form(SettingValues values) => Schema.Form(values);

    public AssistantSenses Senses(SettingValues values) => Schema.Senses(values);

    /// <summary>Always ready. It has nowhere to connect to and nothing to pay.</summary>
    public string? Unavailable(AssistantConfig config) => null;

    public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) => new Rehearsal(workbench);
}
