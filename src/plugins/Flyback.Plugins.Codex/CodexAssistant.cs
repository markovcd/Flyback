using Flyback.Plugins.Assist;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.Codex;

public sealed class CodexAssistant : IPatchAssistant
{
    private readonly Func<ICodexCli?> cli;

    public CodexAssistant()
        : this(() => CodexLocator.Find() is { } found ? new CodexCli(found) : null)
    {
    }

    internal CodexAssistant(Func<ICodexCli?> cli) => this.cli = cli;

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

    /// <summary>
    /// The schema's questions less the two that have no answer here: there is no
    /// address to set, and no model takes a sound.
    /// </summary>
    public IReadOnlyList<SettingField> Form(SettingValues values) =>
        [.. Schema.Form(values).Where(field => field.Key is not (AssistantSchema.EndpointKey or AssistantSchema.HearingKey))];

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

    public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) => Session(workbench, config);

    public IPatchSession? Resume(PatchWorkbench workbench, AssistantConfig config, string saved)
    {
        var session = Session(workbench, config);

        if (session.Take(saved)) return session;

        session.Dispose();
        return null;
    }

    private CodexSession Session(PatchWorkbench workbench, AssistantConfig config) =>
        new(workbench, Schema.Read(config.Values), cli() ?? new Missing());

    /// <summary>What a session is given where Codex is not installed: every question is a failure that says so.</summary>
    private sealed class Missing : ICodexCli
    {
        public Task<CodexAnswer> Ask(CodexRequest request, CancellationToken cancel) =>
            throw new CodexFailure("Codex is not installed.");
    }
}
