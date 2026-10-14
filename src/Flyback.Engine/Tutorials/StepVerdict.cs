namespace Flyback.Engine.Tutorials;

/// <summary>Whether a patch has done what a step asks, and if not, the next thing to do.</summary>
internal sealed record StepVerdict(bool Done, string? Hint)
{
    public static StepVerdict Met { get; } = new(true, null);

    public static StepVerdict Unmet(string hint) => new(false, hint);
}
