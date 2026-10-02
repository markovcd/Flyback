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
}
