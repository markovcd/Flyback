namespace Flyback.Engine.Measure;

/// <summary>What one number of an output did over the window: a scalar's value, or one of a color's r, g and b.</summary>
/// <param name="Min">The lowest it went.</param>
/// <param name="Max">The highest it went.</param>
/// <param name="Mean">Its average.</param>
/// <param name="OverTime">Whether it changed while the window ran.</param>
/// <param name="Across">Whether it differs across the picture; always false for the sound.</param>
/// <param name="Hz">How often it repeats, where it does.</param>
/// <param name="StepsPerSecond">How often it jumps, for a signal that holds and jumps and does not repeat.</param>
/// <param name="Slope">The steepest it moved, a second, for a signal that changes and is neither.</param>
public sealed record ComponentStats(
    double Min,
    double Max,
    double Mean,
    bool OverTime,
    bool Across,
    double? Hz = null,
    double? StepsPerSecond = null,
    double? Slope = null)
{
    /// <summary>Whether it held one value throughout, everywhere.</summary>
    public bool Static => !OverTime && !Across;

    /// <summary>The values a stepped signal held, lowest first, where there were few enough to list.</summary>
    public IReadOnlyList<double>? Held { get; init; }

    /// <summary>The share of the window a signal of many values sat flat at its highest, as a clipped one does.</summary>
    public double? FlatTop { get; init; }

    /// <summary>The share of the window it sat flat at its lowest.</summary>
    public double? FlatBottom { get; init; }

    public bool Equals(ComponentStats? other) =>
        other is not null
        && (Min, Max, Mean, OverTime, Across, Hz, StepsPerSecond, Slope)
            == (other.Min, other.Max, other.Mean, other.OverTime, other.Across, other.Hz, other.StepsPerSecond, other.Slope)
        && (Held ?? []).SequenceEqual(other.Held ?? [])
        && Held is null == other.Held is null
        && (FlatTop, FlatBottom) == (other.FlatTop, other.FlatBottom);

    public override int GetHashCode() => HashCode.Combine(Min, Max, Mean, OverTime, Across, Hz, StepsPerSecond, Slope);
}
