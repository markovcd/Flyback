namespace Flyback.Server;

/// <summary>How a preset or plugin is rated: the mean of its stars, and how many gave them.</summary>
internal sealed record Rating(double Average, int Count)
{
    public static readonly Rating None = new(0, 0);
}