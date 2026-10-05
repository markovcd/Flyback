using System.Text.Json.Nodes;
using Flyback.Plugins.Assist;

namespace Flyback.Plugins.ClaudeCode;

/// <summary>One question for Claude Code.</summary>
/// <param name="Model">An alias such as <c>sonnet</c>, or a full model id.</param>
/// <param name="Effort">How hard to think.</param>
/// <param name="Content">The user message's content blocks: text and pictures.</param>
internal sealed record ClaudeRequest(string Model, AssistantEffort Effort, JsonArray Content);
