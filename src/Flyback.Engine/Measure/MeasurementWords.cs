using System.Globalization;

namespace Flyback.Engine.Measure;

/// <summary>A measurement in words, the same wherever one is printed.</summary>
public static class MeasurementWords
{
    /// <summary>One number's behavior in a line: its value or its range, and how fast it moves.</summary>
    /// <param name="stats"></param>
    /// <param name="seconds">The window it was watched over.</param>
    public static string Describe(ComponentStats stats, double seconds)
    {
        ArgumentNullException.ThrowIfNull(stats);

        var window = $"{Number(seconds)} s";

        if (stats.Static) return $"{Number(stats.Min)}, no change in {window}";

        var parts = new List<string> { $"{Number(stats.Min)}..{Number(stats.Max)}", $"mean {Number(stats.Mean)}" };

        if (stats.Across) parts.Add("varies across the picture");

        if (!stats.OverTime) parts.Add($"no change in {window}");
        else if (stats.Hz is { } hz) parts.Add($"{Number(hz)} Hz");
        else if (stats.StepsPerSecond is { } steps) parts.Add($"{Number(steps)} steps/s");
        else if (stats.Slope is { } slope) parts.Add($"changing, up to {Number(slope)}/s");

        return string.Join(" · ", parts);
    }

    /// <summary>A half's numbers, one per line under a label: a color's as r, g and b.</summary>
    public static IEnumerable<string> Half(string label, IReadOnlyList<ComponentStats> components, double seconds)
    {
        ArgumentNullException.ThrowIfNull(components);

        for (var c = 0; c < components.Count; c++)
        {
            var head = c == 0 ? label : string.Empty;
            var part = components.Count == 3 ? "rgb"[c] + " " : string.Empty;

            yield return $"{head,-8} {part}{Describe(components[c], seconds)}";
        }
    }

    /// <summary>Four places at most, and never "-0".</summary>
    public static string Number(double value)
    {
        var rounded = Math.Round(value, 4);

        return (rounded == 0d ? 0d : rounded).ToString("0.####", CultureInfo.InvariantCulture);
    }
}
