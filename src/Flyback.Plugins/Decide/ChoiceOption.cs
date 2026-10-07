namespace Flyback.Plugins.Decide;

/// <summary>One option of a <see cref="Question.Choice"/>.</summary>
/// <param name="Label">What the answer names it by.</param>
/// <param name="Description">What it means, for the model to read.</param>
public readonly record struct ChoiceOption(string Label, string Description);
