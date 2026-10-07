using Flyback.Plugins.Assist;
using Flyback.Plugins.Programs;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.ClaudeCode;

public sealed class ClaudeCodeAssistant : IPatchAssistant
{
    private readonly Func<IProgram?> cli;

    public ClaudeCodeAssistant()
        : this(() => ClaudeLocator.Find() is { } found ? new ClaudeCli(found) : null)
    {
    }

    internal ClaudeCodeAssistant(Func<IProgram?> cli) => this.cli = cli;

    public string Id => "claude-code";

    public string Name => "Claude Code";

    public int Priority => 40;

    /// <summary>Aliases, not ids: Claude Code resolves each to its newest model, so nothing here ages.</summary>
    internal AssistantSchema Schema { get; } = new(
        "sonnet",
        [new AssistantModel("sonnet"), new AssistantModel("opus"), new AssistantModel("haiku")],
        string.Empty,
        "Not needed: Flyback runs the Claude Code you are signed in to.",
        HearingAsked: false);

    public AssistantCredential Credential => Schema.Credential;

    public bool NeedsKey => false;

    /// <summary>Nowhere: nothing is sent from here, so there is no origin for a key to be bound to.</summary>
    public Uri? Endpoint(SettingValues values) => null;

    public IReadOnlyList<SettingField> Form(SettingValues values) => Schema.Form(values);

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

    public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) =>
        ProgramAssistants.Start(Name, workbench, Schema, config, cli());

    public IPatchSession? Resume(PatchWorkbench workbench, AssistantConfig config, string saved) =>
        ProgramAssistants.Resume(Name, workbench, Schema, config, cli(), saved);
}
