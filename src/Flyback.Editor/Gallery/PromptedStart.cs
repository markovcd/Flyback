using Flyback.Core.Graph;

namespace Flyback.Editor.Gallery;

/// <summary>What the prompt card answers with: words to start a new patch from, not a preset.</summary>
internal sealed record PromptedStart(string Prompt) : IPreset
{
    public string Name => "Start with a prompt";

    public string Description => Prompt;
}
