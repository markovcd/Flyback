namespace Flyback.Plugins.Decide;

/// <summary>What answering cost, in the model's tokens.</summary>
public sealed record DecisionUsage(int InputTokens, int OutputTokens)
{
    /// <summary>Nothing counted.</summary>
    public static DecisionUsage None { get; } = new(0, 0);
}
