using Flyback.Plugins.Settings;

namespace Flyback.Plugins.Assist;

/// <summary>
/// An example prompt that worked well for a specific model.
/// </summary>
/// <param name="Name">Short name for the dropdown.</param>
/// <param name="Prompt">The full prompt text.</param>
/// <param name="Description">What kind of patch this makes (shown as tooltip).</param>
public sealed record ExamplePrompt(
    string Name,
    string Prompt,
    string Description);

/// <summary>
/// Extension for <see cref="IPatchAssistant"/> to provide example prompts.
/// </summary>
public interface IExamplePrompts
{
    /// <summary>
    /// Example prompts that worked well for this provider/model.
    /// </summary>
    /// <param name="model">The model selected, or null for provider-wide examples.</param>
    IReadOnlyList<ExamplePrompt> Examples(string? model = null);
}
