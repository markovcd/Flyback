namespace Flyback.Plugins.Gemini;

/// <summary>
/// Offers an assistant that speaks Google's generateContent format. A second
/// adapter rather than a second base url, which is the whole reason it is worth
/// having: what it buys is in <see cref="Wire.Answers"/> — a sound is an ordinary
/// part of a turn here, so the model building the patch can be played the patch.
/// </summary>
public sealed class GeminiPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "flyback.gemini",
        "Gemini",
        "Builds patches through Google's Gemini models, which hear the patch as well as see it.");

    public void Register(IPluginRegistry registry) => registry.AddPatchAssistant(new GeminiAssistant());
}
