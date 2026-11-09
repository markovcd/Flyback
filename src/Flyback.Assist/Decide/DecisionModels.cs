namespace Flyback.Assist.Decide;

/// <summary>The decision models a host offers.</summary>
internal sealed class DecisionModels(IReadOnlyList<IDecisionModel> all)
{
    /// <summary>What Flyback has: the Decision server, reached over HTTP.</summary>
    public static DecisionModels Shipped { get; } = new([new SystemOneModel()]);

    public static DecisionModels None { get; } = new([]);

    public IReadOnlyList<IDecisionModel> All { get; } = all;
}
