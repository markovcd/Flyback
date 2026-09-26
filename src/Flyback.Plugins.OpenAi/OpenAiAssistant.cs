using Flyback.Plugins.Assist;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.OpenAi;

public sealed class OpenAiAssistant : IPatchAssistant
{
    public string Id => "openai";

    public string Name => "OpenAI-compatible";

    public int Priority => 50;

    /// <remarks>
    /// The default is a model that can see, because looking is what most of this
    /// is. The three audio models take a sound and not a picture, which is why they
    /// are recorded with sight off and are an ear rather than a driver — see
    /// <see cref="AssistantChoices.EarModel"/>. The last two are the text-only
    /// weights of a local runtime, said so because a picture sent to either is a
    /// 400.
    /// <para>
    /// All of which is a guess about a service nobody named, and this is the
    /// adapter where that matters most, the endpoint being a field.
    /// <see cref="IModelSurvey"/> replaces the lot with what the endpoint said —
    /// see <see cref="AssistantSchema.Surveyed"/>.
    /// </para>
    /// </remarks>
    public AssistantSchema Schema { get; } = new(
        "gpt-4o",
        [
            new AssistantModel("gpt-4o"),
            new AssistantModel("gpt-4o-mini"),
            new AssistantModel("gpt-4.1"),
            new AssistantModel("gpt-4o-audio-preview", Vision: false, Hearing: true),
            new AssistantModel("gpt-4o-mini-audio-preview", Vision: false, Hearing: true),
            new AssistantModel("gpt-audio", Vision: false, Hearing: true),
            new AssistantModel("llama3.1", Vision: false),
            new AssistantModel("qwen2.5", Vision: false),
        ],
        "OPENAI_API_KEY",
        "Any endpoint that speaks chat completions. A local runtime such as Ollama "
        + "will accept any value as a key.",
        "https://api.openai.com/v1",
        BaseUrlEditable: true);

    /// <summary>What this one's key comes from. The host holds it; see ADR-0034.</summary>
    public AssistantCredential Credential => Schema.Credential;

    /// <summary>
    /// The ordinary five questions, declared by the schema rather than written
    /// out here — see <see cref="AssistantSchema.Form"/>. There is nothing
    /// peculiar about this provider's form, which is exactly why the declaration
    /// of it is shared.
    /// </summary>
    public IReadOnlyList<SettingField> Form(SettingValues values) => Schema.Surveyed(values).Form(values);

    public AssistantSenses Senses(SettingValues values) => Schema.Surveyed(values).Senses(values);

    /// <summary>
    /// Answered from the configuration alone — no request, no client, nothing
    /// that costs anything. The endpoint is only found out to be wrong when
    /// somebody actually asks it something, which is the honest moment for it.
    /// </summary>
    public string? Unavailable(AssistantConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            return "No key yet — set OPENAI_API_KEY, or put one in Settings.";

        var chosen = Schema.Surveyed(config.Values).Read(config.Values);

        if (string.IsNullOrWhiteSpace(chosen.Model))
            return "No model chosen. Put one in Settings.";

        var endpoint = chosen.BaseUrl ?? Schema.DefaultBaseUrl;

        if (string.IsNullOrWhiteSpace(endpoint))
            return "No endpoint. Put one in Settings.";

        return Uri.TryCreate(endpoint, UriKind.Absolute, out var parsed)
               && parsed.Scheme is "http" or "https"
            ? null
            : $"'{endpoint}' is not an http or https address.";
    }

    public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) => Session(workbench, config);

    public IPatchSession? Resume(PatchWorkbench workbench, AssistantConfig config, string saved)
    {
        var session = Session(workbench, config);

        if (session.Take(saved)) return session;

        session.Dispose();
        return null;
    }

    private OpenAiSession Session(PatchWorkbench workbench, AssistantConfig config) =>
        new(
            workbench,
            Schema.Surveyed(config.Values).Read(config.Values),
            config.ApiKey,
            Schema.DefaultBaseUrl!);
    
    public async Task<IReadOnlyList<ModelReport>> Survey(
        AssistantConfig config,
        SurveyOptions options,
        IProgress<string>? said = null,
        CancellationToken cancel = default)
    {
        var chosen = Schema.Read(config.Values);

        using var probe = new OpenAiProbe(config.ApiKey, chosen.BaseUrl ?? Schema.DefaultBaseUrl!);

        return await probe.Run(Schema.Asking(options, config.Values), said, cancel).ConfigureAwait(false);
    }
}