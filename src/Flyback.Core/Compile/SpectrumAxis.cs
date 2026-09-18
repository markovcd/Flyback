namespace Flyback.Core.Compile;

/// <summary>
/// The frequency axis an Analyzer's chart is laid out along: logarithmic, from
/// the bottom of hearing to the top of it.
/// </summary>
/// <remarks>
/// Apart from the transform that fills the chart, because the module drawing it
/// has to know the axis too — where the decade lines go, and where a given
/// frequency falls — and knows nothing else about how the buffer was filled.
/// </remarks>
public static class SpectrumAxis
{
    /// <summary>The frequency at the left-hand edge of the chart, in hertz.</summary>
    public const double Lowest = 20d;

    /// <summary>The frequency at the right-hand edge, in hertz.</summary>
    public const double Highest = 20_000d;

    /// <summary>The frequency a point of a buffer <paramref name="points"/> long stands for.</summary>
    public static double FrequencyAt(int point, int points) =>
        Lowest * Math.Pow(Highest / Lowest, point / (double)Math.Max(points - 1, 1));

    /// <summary>Where along a buffer <paramref name="points"/> long a frequency falls, in points.</summary>
    public static double PointOf(double hertz, int points) =>
        Math.Log(hertz / Lowest) / Math.Log(Highest / Lowest) * (points - 1);
}
