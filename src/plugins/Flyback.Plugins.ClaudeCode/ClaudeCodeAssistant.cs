using Flyback.Plugins.Assist;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.ClaudeCode;

public sealed class ClaudeCodeAssistant : IPatchAssistant
{
    private readonly Func<IClaudeCli?> cli;

    public ClaudeCodeAssistant()
        : this(() => ClaudeLocator.Find() is { } found ? new ClaudeCli(found) : null)
    {
    }

    internal ClaudeCodeAssistant(Func<IClaudeCli?> cli) => this.cli = cli;

    public string Id => "claude-code";

    public string Name => "Claude Code";

    public int Priority => 40;

    /// <summary>Aliases, not ids: Claude Code resolves each to its newest model, so nothing here ages.</summary>
    internal AssistantSchema Schema { get; } = new(
        "sonnet",
        [new AssistantModel("sonnet"), new AssistantModel("opus"), new AssistantModel("haiku")],
        string.Empty,
        "Not needed: Flyback runs the Claude Code you are signed in to.");

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

        if (!ClaudeCli.IsModel(model))
            return $"'{model}' is not a model name Claude Code takes. Pick one in Settings.";

        return cli() is null
            ? "Claude Code is not installed. Install it from claude.com/code, run `claude` once and sign in; then this works with no key."
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

    private ClaudeCodeSession Session(PatchWorkbench workbench, AssistantConfig config) =>
        new(workbench, Schema.Read(config.Values), cli() ?? new Missing());

    /// <summary>What a session is given where Claude Code is not installed: every question is a failure that says so.</summary>
    private sealed class Missing : IClaudeCli
    {
        public Task<ClaudeAnswer> Ask(ClaudeRequest request, CancellationToken cancel) =>
            throw new ClaudeCodeFailure("Claude Code is not installed.");
    }
}
