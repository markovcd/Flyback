namespace Flyback.Engine.Language.Values;

/// <summary>What a def with several results hands back, or a list.</summary>
internal sealed record Several(IReadOnlyList<Value> Items) : Value
{
    /// <summary>Written as a list or made by a loop, rather than a def's or a bracket's tuple.</summary>
    public bool Listed { get; init; }
}
