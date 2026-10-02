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
// ReSharper disable once UnusedType.Global - found by reflection
public sealed class OpenAiPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "flyback.openai",
        "OpenAI-compatible",
        "Builds patches through any endpoint that speaks the chat-completions format.");

    public void Register(IPluginRegistry registry) => registry.AddPatchAssistant(new OpenAiAssistant());
}