namespace Flyback.Assist.Decide;

/// <summary>What answering cost, in the model's tokens.</summary>
public sealed record DecisionUsage(int InputTokens, int OutputTokens);
