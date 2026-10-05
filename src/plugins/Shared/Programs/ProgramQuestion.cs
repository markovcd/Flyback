using Flyback.Plugins.Assist;

namespace Flyback.Plugins.Programs;

/// <summary>The conversation as it stands, for a program to be asked its next turn.</summary>
/// <param name="Model">What the person chose, for the program to understand.</param>
/// <param name="Effort">How hard to think.</param>
/// <param name="Preamble">The briefing, the way to call tools and the tools: the same bytes every time.</param>
/// <param name="Turns">Everything said so far, oldest first.</param>
internal sealed record ProgramQuestion(string Model, AssistantEffort Effort, string Preamble, IReadOnlyList<Turn> Turns);
