using Flyback.Plugins.Assist;
using Flyback.Plugins.Programs;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.Codex;

public sealed class CodexAssistant : IPatchAssistant
{
    private readonly Func<IProgram?> cli;

    public CodexAssistant()
        : this(() => CodexLocator.Find() is { } found ? new CodexCli(found) : null)
    {
    }

    internal CodexAssistant(Func<IProgram?> cli) => this.cli = cli;

    public string Id => "codex";

    public string Name => "Codex";

    public int Priority => 45;

    /// <summary>
    /// "default" leaves the model to Codex, which knows what the person's plan offers.
    /// The others are what it listed at the time, and anything else may be typed.
    /// </summary>
    internal AssistantSchema Schema { get; } = new(
        CodexCli.DefaultModel,
        [
            new AssistantModel(CodexCli.DefaultModel),
            new AssistantModel("gpt-5.5"),
            new AssistantModel("gpt-5.6-terra"),
            new AssistantModel("gpt-5.6-luna"),
            new AssistantModel("gpt-6-luna"),
        ],
        string.Empty,
        "Not needed: Flyback runs the Codex you are signed in to.");

    public AssistantCredential Credential => Schema.Credential;

    public bool NeedsKey => false;

    /// <summary>Nowhere: nothing is sent from here, so there is no origin for a key to be bound to.</summary>
    public Uri? Endpoint(SettingValues values) => null;

    public IReadOnlyList<SettingField> Form(SettingValues values) => ProgramAssistants.Form(Schema, values);

    public AssistantSenses Senses(SettingValues values) => Schema.Senses(values);

    /// <summary>Answered from what is on disk alone: nothing is started and nothing is asked.</summary>
    public string? Unavailable(AssistantConfig config)
    {
        var model = Schema.Read(config.Values).Model;

        if (!CodexCli.IsModel(model))
            return $"'{model}' is not a model name Codex takes. Pick one in Settings.";

        return cli() is null
            ? "Codex is not installed. Install it from developers.openai.com/codex, run `codex login` once and sign in with ChatGPT; then this works with no key."
            : null;
    }

    public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) =>
        ProgramAssistants.Start(Name, workbench, Schema, config, cli());

    public IPatchSession? Resume(PatchWorkbench workbench, AssistantConfig config, string saved) =>
        ProgramAssistants.Resume(Name, workbench, Schema, config, cli(), saved);
}
