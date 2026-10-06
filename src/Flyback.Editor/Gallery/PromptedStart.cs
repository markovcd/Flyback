using Flyback.Core.Graph;

namespace Flyback.Editor.Gallery;

/// <summary>What the prompt card answers with: words to start a new patch from, not a preset.</summary>
/// <param name="Written">Whether the words are a brief the assistant already wrote out, rather than an idea still to be.</param>
internal sealed record PromptedStart(string Prompt, bool Written) : IPreset
{
    public string Name => "Start with a prompt";

    public string Description => Prompt;
}
