using Flyback.Plugins.Assist;

namespace Flyback.Plugins.Codex;

/// <summary>One question for Codex.</summary>
/// <param name="Model">A model id, or <see cref="CodexCli.DefaultModel"/> for the one Codex picks.</param>
/// <param name="Effort">How hard to think.</param>
/// <param name="Prompt">The conversation as text, with each picture's place marked.</param>
/// <param name="Pictures">PNGs in the order the prompt marks them.</param>
internal sealed record CodexRequest(string Model, AssistantEffort Effort, string Prompt, IReadOnlyList<byte[]> Pictures);
