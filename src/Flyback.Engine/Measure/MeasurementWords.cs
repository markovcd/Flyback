using System.Globalization;

namespace Flyback.Engine.Measure;

/// <summary>A measurement in words, the same wherever one is printed.</summary>
public static class MeasurementWords
{
    /// <summary>One number's behavior in a line: its value or its range, and how fast it moves.</summary>
    /// <param name="stats"></param>
    /// <param name="seconds">The window it was watched over.</param>
    /// <param name="pictured">Whether a picture is shown with it, which already says it varies across the picture.</param>
    public static string Describe(ComponentStats stats, double seconds, bool pictured = false)
    {
        ArgumentNullException.ThrowIfNull(stats);

        var window = $"{Number(seconds)} s";

        if (stats.Static) return $"{Number(stats.Min)}, no change in {window}";

        var parts = new List<string> { $"{Number(stats.Min)}..{Number(stats.Max)}", $"mean {Number(stats.Mean)}" };

        if (stats.Across && !pictured) parts.Add("varies across the picture");

        if (!stats.OverTime) parts.Add($"no change in {window}");
        else if (stats.Hz is { } hz) parts.Add($"{Number(hz)} Hz");
        else if (stats.StepsPerSecond is { } steps) parts.Add($"{Number(steps)} steps/s");
        else if (stats.Slope is { } slope) parts.Add($"changing, up to {Number(slope)}/s");

        return string.Join(" · ", parts);
    }

    /// <summary>A half's numbers, one per line under a label: a color's as r, g and b.</summary>
    public static IEnumerable<string> Half(string label, IReadOnlyList<ComponentStats> components, double seconds, bool pictured = false)
    {
        ArgumentNullException.ThrowIfNull(components);

        for (var c = 0; c < components.Count; c++)
        {
            var head = c == 0 ? label : string.Empty;
            var part = components.Count == 3 ? "rgb"[c] + " " : string.Empty;

            yield return $"{head,-8} {part}{Describe(components[c], seconds, pictured)}";
        }
    }

    /// <summary>
    /// A label short enough to pin beside a socket: <c>0.25</c> when it holds still,
    /// <c>-1..1 · 2 Hz</c> when it moves, and r, g and b for a color that holds.
    /// </summary>
    public static string Brief(IReadOnlyList<ComponentStats> components, bool pictured = false)
    {
        ArgumentNullException.ThrowIfNull(components);

        if (components.Count == 3)
        {
            return components.All(c => c.Static)
                ? string.Join(" ", components.Select(c => Number(c.Min)))
                : components.Any(c => c.Across) ? "varies across" : "changing";
        }

        if (components.Count != 1) return string.Empty;

        var stats = components[0];

        if (stats.Static) return Number(stats.Min);

        var range = $"{Number(stats.Min)}..{Number(stats.Max)}";

        if (stats.Hz is { } hz) return $"{range} · {Number(hz)} Hz";
        if (stats.StepsPerSecond is { } steps) return $"{range} · {Number(steps)} steps/s";

        return stats.Across && !stats.OverTime && !pictured ? $"{range} across" : range;
    }

    /// <summary>Four places at most, and never "-0".</summary>
    public static string Number(double value)
    {
        var rounded = Math.Round(value, 4);

        return (rounded == 0d ? 0d : rounded).ToString("0.####", CultureInfo.InvariantCulture);
    }
}
