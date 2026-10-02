namespace Flyback.Engine.Measure;

/// <summary>Running totals for one number watched over time, kept without storing what it was.</summary>
/// <remarks>
/// A repeat is counted where the value rises through the middle of the range seen
/// so far, by a tenth of that range, so noise near the middle is not a cycle. The
/// first rise is spent finding the range and is not timed, so a repeat needs three
/// and a bit cycles in the window to be called one.
/// </remarks>
/// <param name="steppedShare">The fraction of evaluations that may change for a signal to count as held and stepped.</param>
/// <param name="fastest">The highest repeat that can be told apart at this rate; anything faster is called changing.</param>
internal sealed class ComponentTracker(double steppedShare, double fastest)
{
    /// <summary>Rises left out of the timing while the range settles.</summary>
    private const int Settling = 1;

    /// <summary>How much the gaps between rises may vary for the signal to count as repeating.</summary>
    private const double Regularity = 1.25;

    private long count;
    private long changes;
    private double min = double.PositiveInfinity;
    private double max = double.NegativeInfinity;
    private double sum;
    private double last;
    private double lastTime;
    private double slope;
    private double firstChange;
    private double lastChange;

    private bool below;
    private int rises;
    private double firstRise;
    private double lastRise;
    private double shortest = double.PositiveInfinity;
    private double longest;

    public double Min => min;

    public double Max => max;

    public double Sum => sum;

    public long Count => count;

    public bool Changed => changes > 0;

    public void Add(double value, double time)
    {
        if (!double.IsFinite(value)) value = 0d;

        if (count > 0 && value != last)
        {
            if (changes++ == 0) firstChange = time;
            lastChange = time;

            var interval = time - lastTime;
            if (interval > 0d) slope = Math.Max(slope, Math.Abs(value - last) / interval);
        }

        min = Math.Min(min, value);
        max = Math.Max(max, value);
        sum += value;
        count++;
        last = value;
        lastTime = time;

        Cross(value, time);
    }

    private void Cross(double value, double time)
    {
        var middle = (min + max) * 0.5d;
        var margin = (max - min) * 0.1d;

        if (margin <= 0d) return;

        if (value < middle - margin)
        {
            below = true;
            return;
        }

        if (!below || value < middle + margin) return;

        below = false;
        rises++;

        if (rises <= Settling) return;

        if (rises == Settling + 1)
        {
            firstRise = lastRise = time;
            return;
        }

        var gap = time - lastRise;

        shortest = Math.Min(shortest, gap);
        longest = Math.Max(longest, gap);
        lastRise = time;
    }

    /// <summary>What the tracked number did, over a window <paramref name="seconds"/> long.</summary>
    /// <param name="seconds"></param>
    /// <param name="across">Whether it also varied across the picture, which the caller knows and this does not.</param>
    /// <param name="overall">The min, max and mean to report in place of this one's, for a picture's many pixels.</param>
    public ComponentStats Finish(double seconds, bool across = false, (double Min, double Max, double Mean)? overall = null)
    {
        var (low, high, mean) = overall ?? (count == 0 ? (0d, 0d, 0d) : (min, max, sum / count));

        if (changes == 0) return new ComponentStats(low, high, mean, OverTime: false, across);

        var timed = rises - Settling - 1;

        if (timed >= 2 && longest <= shortest * Regularity && lastRise > firstRise)
        {
            var hz = timed / (lastRise - firstRise);

            if (hz <= fastest) return new ComponentStats(low, high, mean, true, across, Hz: hz);
        }

        if (changes < count * steppedShare && seconds > 0d)
        {
            // Timed between the first jump and the last, so the hold before the
            // first and after the last do not dilute the rate.
            var steps = changes >= 3 && lastChange > firstChange
                ? (changes - 1) / (lastChange - firstChange)
                : changes / seconds;

            return new ComponentStats(low, high, mean, true, across, StepsPerSecond: steps);
        }

        return new ComponentStats(low, high, mean, true, across, Slope: slope);
    }
}
