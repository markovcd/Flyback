using Flyback.Plugins.Assist;

namespace Flyback.Plugins.OpenAi;

/// <summary>
/// Offers an assistant that speaks the chat-completions format.
/// </summary>
/// <remarks>
/// Deliberately not "an OpenAI plugin". That format is spoken by a great many
/// services and by every local runtime worth the name, so the endpoint is a
/// field rather than a constant — which is what makes one adapter reach Groq,
/// Together, Fireworks, DeepSeek, xAI, OpenRouter, Ollama and LM Studio without
/// knowing any of their names.
/// </remarks>
public sealed class OpenAiPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "flyback.openai",
        "OpenAI-compatible",
        "Builds patches through any endpoint that speaks the chat-completions format.");

    public void Register(IPluginRegistry registry) => registry.AddPatchAssistant(new OpenAiAssistant());
}

public sealed class OpenAiAssistant : IPatchAssistant
{
    public string Id => "openai";

    public string Name => "OpenAI-compatible";

    public int Priority => 50;

    /// <remarks>
    /// <para>
    /// The default is a model that can see, because looking is what most of this
    /// is. The two audio models are listed rather than defaulted to for the
    /// reason <see cref="AssistantConfig.Hearing"/> is off by default: they are
    /// the only ones here that take a sound, and they are not the ones to reach
    /// for otherwise.
    /// </para>
    /// <para>
    /// The three audio models take a sound and <em>not</em> a picture, which is
    /// why they are recorded with sight off and why they are an ear rather than
    /// a driver — see <see cref="AssistantConfig.EarModel"/>. Chosen as the
    /// model in the box they still work, and the form takes sight away rather
    /// than sending them something they will refuse.
    /// </para>
    /// <para>
    /// The last two are named as a local runtime names them, and both are the
    /// text-only weights — the multimodal ones are separate models under
    /// separate names. Saying so is the point of recording this at all: a
    /// picture sent to either is a 400, and until it was written down the shell
    /// had no way to know that and offered to send one.
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

    /// <summary>
    /// Answered from the configuration alone — no request, no client, nothing
    /// that costs anything. The endpoint is only found out to be wrong when
    /// somebody actually asks it something, which is the honest moment for it.
    /// </summary>
    public string? Unavailable(AssistantConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            return "No key yet — set OPENAI_API_KEY, or put one in Settings.";

        if (string.IsNullOrWhiteSpace(config.Model))
            return "No model chosen. Put one in Settings.";

        var endpoint = config.BaseUrl ?? Schema.DefaultBaseUrl;

        if (string.IsNullOrWhiteSpace(endpoint))
            return "No endpoint. Put one in Settings.";

        return Uri.TryCreate(endpoint, UriKind.Absolute, out var parsed)
            && parsed.Scheme is "http" or "https"
                ? null
                : $"'{endpoint}' is not an http or https address.";
    }

    public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) =>
        new OpenAiSession(workbench, config, Schema.DefaultBaseUrl!);
}
