namespace Flyback.Engine.Language.Values;

/// <summary>What a def with several results hands back.</summary>
internal sealed record Several(IReadOnlyList<Value> Items) : Value;
